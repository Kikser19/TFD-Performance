using Microsoft.EntityFrameworkCore;
using TrackingService.Auth;
using TrackingService.Clients;
using TrackingService.Contracts;
using TrackingService.Data;
using TrackingService.Domain;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("TrackingDb")
    ?? throw new InvalidOperationException("Missing connection string 'TrackingDb'.");

var programBaseUrl = builder.Configuration["Services:ProgramBaseUrl"]
    ?? throw new InvalidOperationException("Missing 'Services:ProgramBaseUrl'.");

builder.Services.AddDbContext<TrackingDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddHttpClient<ProgramClient>(c => c.BaseAddress = new Uri(programBaseUrl));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TrackingDbContext>();
    db.Database.Migrate();
}

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

// =====================================================================================
// Workout logs (sets/reps/weight)
// =====================================================================================

// A client logs a completed workout session. Access to the workout is verified with the
// Program Service (architecture-guide §4) before anything is stored.
app.MapPost("/api/workout-logs", async (LogWorkoutRequest req, HttpContext http, TrackingDbContext db, ProgramClient program, CancellationToken ct) =>
{
    var caller = http.GetCaller();
    if (caller is null) return Results.Unauthorized();
    if (!caller.IsClient) return Forbidden("Only clients can log workouts.");

    if (req.WorkoutId == Guid.Empty) return BadReq("WorkoutId is required.");
    if (req.Sets is null || req.Sets.Count == 0) return BadReq("At least one set is required.");

    var accessibleExerciseIds = await program.GetAccessibleWorkoutExerciseIdsAsync(req.WorkoutId, caller, ct);
    if (accessibleExerciseIds is null)
        return Forbidden("You do not have access to this workout.");

    foreach (var s in req.Sets)
    {
        if (!accessibleExerciseIds.Contains(s.WorkoutExerciseId))
            return BadReq($"Workout exercise {s.WorkoutExerciseId} does not belong to this workout.");
        if (s.SetNumber <= 0) return BadReq("SetNumber must be positive.");
        if (s.RepsCompleted < 0) return BadReq("RepsCompleted cannot be negative.");
        if (s.WeightUsed < 0) return BadReq("WeightUsed cannot be negative.");
    }

    var log = new WorkoutLog
    {
        Id = Guid.NewGuid(),
        ClientId = caller.UserId,
        WorkoutId = req.WorkoutId,
        PerformedAt = req.PerformedAt?.ToUniversalTime() ?? DateTime.UtcNow,
        Sets = req.Sets.Select(s => new SetLog
        {
            Id = Guid.NewGuid(),
            WorkoutExerciseId = s.WorkoutExerciseId,
            SetNumber = s.SetNumber,
            RepsCompleted = s.RepsCompleted,
            WeightUsed = s.WeightUsed,
        }).ToList(),
    };

    db.WorkoutLogs.Add(log);
    await db.SaveChangesAsync(ct);
    return Results.Created($"/api/workout-logs/{log.Id}", ToWorkoutLogDto(log));
});

// A client reads back their own workout history.
app.MapGet("/api/workout-logs", async (HttpContext http, TrackingDbContext db) =>
{
    var caller = http.GetCaller();
    if (caller is null) return Results.Unauthorized();
    if (!caller.IsClient) return Forbidden("Only clients have a personal workout history.");

    var (page, pageSize) = ReadPaging(http.Request);
    return Results.Ok(await QueryWorkoutLogs(db, caller.UserId, http.Request, page, pageSize));
});

// A trainer reads a specific client's workout history (dashboard). Authorized by asking
// the Program Service whether this trainer actually coaches the client.
app.MapGet("/api/clients/{clientId:guid}/workout-logs", async (Guid clientId, HttpContext http, TrackingDbContext db, ProgramClient program, CancellationToken ct) =>
{
    var caller = http.GetCaller();
    if (caller is null) return Results.Unauthorized();
    if (!caller.IsTrainer) return Forbidden("Only trainers can read another user's logs.");
    if (!await program.IsCoachingAsync(clientId, caller, ct))
        return Forbidden("This client is not assigned to any of your programs.");

    var (page, pageSize) = ReadPaging(http.Request);
    return Results.Ok(await QueryWorkoutLogs(db, clientId, http.Request, page, pageSize));
});

// =====================================================================================
// Nutrition entries
// =====================================================================================

app.MapPost("/api/nutrition-entries", async (LogNutritionRequest req, HttpContext http, TrackingDbContext db) =>
{
    var caller = http.GetCaller();
    if (caller is null) return Results.Unauthorized();
    if (!caller.IsClient) return Forbidden("Only clients can log nutrition.");

    if (string.IsNullOrWhiteSpace(req.FoodName)) return BadReq("FoodName is required.");
    if (req.Calories < 0) return BadReq("Calories cannot be negative.");
    if (req.ProteinG < 0 || req.CarbsG < 0 || req.FatG < 0) return BadReq("Macros cannot be negative.");

    var entry = new NutritionEntry
    {
        Id = Guid.NewGuid(),
        ClientId = caller.UserId,
        LoggedAt = req.LoggedAt?.ToUniversalTime() ?? DateTime.UtcNow,
        FoodName = req.FoodName.Trim(),
        Calories = req.Calories,
        ProteinG = req.ProteinG,
        CarbsG = req.CarbsG,
        FatG = req.FatG,
    };
    db.NutritionEntries.Add(entry);
    await db.SaveChangesAsync();
    return Results.Created($"/api/nutrition-entries/{entry.Id}", ToNutritionDto(entry));
});

app.MapGet("/api/nutrition-entries", async (HttpContext http, TrackingDbContext db) =>
{
    var caller = http.GetCaller();
    if (caller is null) return Results.Unauthorized();
    if (!caller.IsClient) return Forbidden("Only clients have a personal nutrition history.");

    var (page, pageSize) = ReadPaging(http.Request);
    return Results.Ok(await QueryNutrition(db, caller.UserId, page, pageSize));
});

app.MapGet("/api/clients/{clientId:guid}/nutrition-entries", async (Guid clientId, HttpContext http, TrackingDbContext db, ProgramClient program, CancellationToken ct) =>
{
    var caller = http.GetCaller();
    if (caller is null) return Results.Unauthorized();
    if (!caller.IsTrainer) return Forbidden("Only trainers can read another user's logs.");
    if (!await program.IsCoachingAsync(clientId, caller, ct))
        return Forbidden("This client is not assigned to any of your programs.");

    var (page, pageSize) = ReadPaging(http.Request);
    return Results.Ok(await QueryNutrition(db, clientId, page, pageSize));
});

app.Run();

// =====================================================================================
// Helpers
// =====================================================================================

static IResult Forbidden(string message) => Results.Json(new { error = message }, statusCode: StatusCodes.Status403Forbidden);
static IResult BadReq(string message) => Results.BadRequest(new { error = message });

static async Task<PagedResult<WorkoutLogDto>> QueryWorkoutLogs(TrackingDbContext db, Guid clientId, HttpRequest request, int page, int pageSize)
{
    var query = db.WorkoutLogs.Where(l => l.ClientId == clientId);
    if (Guid.TryParse(request.Query["workoutId"], out var workoutId))
        query = query.Where(l => l.WorkoutId == workoutId);

    var ordered = query.OrderByDescending(l => l.PerformedAt);
    var total = await ordered.CountAsync();
    var items = await ordered.Skip((page - 1) * pageSize).Take(pageSize)
        .Select(l => new WorkoutLogDto(
            l.Id, l.ClientId, l.WorkoutId, l.PerformedAt,
            l.Sets.OrderBy(s => s.SetNumber)
                .Select(s => new SetLogDto(s.Id, s.WorkoutExerciseId, s.SetNumber, s.RepsCompleted, s.WeightUsed))
                .ToList()))
        .ToListAsync();
    return new PagedResult<WorkoutLogDto>(items, page, pageSize, total);
}

static async Task<PagedResult<NutritionEntryDto>> QueryNutrition(TrackingDbContext db, Guid clientId, int page, int pageSize)
{
    var query = db.NutritionEntries.Where(n => n.ClientId == clientId).OrderByDescending(n => n.LoggedAt);
    var total = await query.CountAsync();
    var items = await query.Skip((page - 1) * pageSize).Take(pageSize)
        .Select(n => new NutritionEntryDto(
            n.Id, n.ClientId, n.LoggedAt, n.FoodName, n.Calories, n.ProteinG, n.CarbsG, n.FatG))
        .ToListAsync();
    return new PagedResult<NutritionEntryDto>(items, page, pageSize, total);
}

static (int page, int pageSize) ReadPaging(HttpRequest request)
{
    var page = 1;
    var pageSize = 20;
    if (int.TryParse(request.Query["page"], out var p) && p > 0) page = p;
    if (int.TryParse(request.Query["pageSize"], out var ps) && ps is > 0 and <= 100) pageSize = ps;
    return (page, pageSize);
}

static WorkoutLogDto ToWorkoutLogDto(WorkoutLog l) => new(
    l.Id, l.ClientId, l.WorkoutId, l.PerformedAt,
    l.Sets.OrderBy(s => s.SetNumber)
        .Select(s => new SetLogDto(s.Id, s.WorkoutExerciseId, s.SetNumber, s.RepsCompleted, s.WeightUsed))
        .ToList());

static NutritionEntryDto ToNutritionDto(NutritionEntry n) => new(
    n.Id, n.ClientId, n.LoggedAt, n.FoodName, n.Calories, n.ProteinG, n.CarbsG, n.FatG);
