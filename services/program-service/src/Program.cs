using Microsoft.EntityFrameworkCore;
using ProgramService.Auth;
using ProgramService.Contracts;
using ProgramService.Data;
using ProgramService.Domain;
using static ProgramService.Contracts.Mappers;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("ProgramDb")
    ?? throw new InvalidOperationException("Missing connection string 'ProgramDb'.");

builder.Services.AddDbContext<ProgramDbContext>(options => options.UseNpgsql(connectionString));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ProgramDbContext>();
    db.Database.Migrate();
}

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

// =====================================================================================
// Exercises (trainer-owned)
// =====================================================================================

app.MapPost("/api/exercises", async (CreateExerciseRequest req, HttpContext http, ProgramDbContext db) =>
{
    var caller = http.GetCaller();
    if (caller is null) return Results.Unauthorized();
    if (!caller.IsTrainer) return Forbidden("Only trainers can create exercises.");

    if (string.IsNullOrWhiteSpace(req.Name)) return BadReq("Name is required.");
    if (string.IsNullOrWhiteSpace(req.YoutubeVideoId)) return BadReq("YoutubeVideoId is required.");

    var exercise = new Exercise
    {
        Id = Guid.NewGuid(),
        TrainerId = caller.UserId,
        Name = req.Name.Trim(),
        YoutubeVideoId = req.YoutubeVideoId.Trim(),
        Instructions = string.IsNullOrWhiteSpace(req.Instructions) ? null : req.Instructions.Trim(),
    };
    db.Exercises.Add(exercise);
    await db.SaveChangesAsync();
    return Results.Created($"/api/exercises/{exercise.Id}", ToDto(exercise));
});

app.MapGet("/api/exercises", async (HttpContext http, ProgramDbContext db) =>
{
    var caller = http.GetCaller();
    if (caller is null) return Results.Unauthorized();
    if (!caller.IsTrainer) return Forbidden("Only trainers can list exercises.");

    var (page, pageSize) = ReadPaging(http.Request);
    var query = db.Exercises.Where(e => e.TrainerId == caller.UserId).OrderBy(e => e.Name);
    var total = await query.CountAsync();
    var items = await query.Skip((page - 1) * pageSize).Take(pageSize)
        .Select(e => new ExerciseDto(e.Id, e.TrainerId, e.Name, e.YoutubeVideoId, e.Instructions)).ToListAsync();
    return Results.Ok(new PagedResult<ExerciseDto>(items, page, pageSize, total));
});

app.MapGet("/api/exercises/{id:guid}", async (Guid id, HttpContext http, ProgramDbContext db) =>
{
    var caller = http.GetCaller();
    if (caller is null) return Results.Unauthorized();

    var exercise = await db.Exercises.FindAsync(id);
    if (exercise is null) return NotFoundErr("Exercise not found.");
    if (exercise.TrainerId != caller.UserId) return Forbidden("You do not own this exercise.");
    return Results.Ok(ToDto(exercise));
});

app.MapPut("/api/exercises/{id:guid}", async (Guid id, UpdateExerciseRequest req, HttpContext http, ProgramDbContext db) =>
{
    var caller = http.GetCaller();
    if (caller is null) return Results.Unauthorized();
    if (!caller.IsTrainer) return Forbidden("Only trainers can edit exercises.");

    var exercise = await db.Exercises.FindAsync(id);
    if (exercise is null) return NotFoundErr("Exercise not found.");
    if (exercise.TrainerId != caller.UserId) return Forbidden("You do not own this exercise.");

    if (string.IsNullOrWhiteSpace(req.Name)) return BadReq("Name is required.");
    if (string.IsNullOrWhiteSpace(req.YoutubeVideoId)) return BadReq("YoutubeVideoId is required.");

    exercise.Name = req.Name.Trim();
    exercise.YoutubeVideoId = req.YoutubeVideoId.Trim();
    exercise.Instructions = string.IsNullOrWhiteSpace(req.Instructions) ? null : req.Instructions.Trim();
    await db.SaveChangesAsync();
    return Results.Ok(ToDto(exercise));
});

// =====================================================================================
// Programs
// =====================================================================================

app.MapPost("/api/programs", async (CreateProgramRequest req, HttpContext http, ProgramDbContext db) =>
{
    var caller = http.GetCaller();
    if (caller is null) return Results.Unauthorized();
    if (!caller.IsTrainer) return Forbidden("Only trainers can create programs.");

    var validation = ValidateProgramFields(req.Title, req.Type, req.PriceCents);
    if (validation is not null) return validation;

    var program = new TrainingProgram
    {
        Id = Guid.NewGuid(),
        TrainerId = caller.UserId,
        Title = req.Title.Trim(),
        Type = req.Type,
        PriceCents = req.Type == ProgramTypes.Online ? req.PriceCents : null,
        Status = ProgramStatuses.Draft,
    };
    db.Programs.Add(program);
    await db.SaveChangesAsync();
    return Results.Created($"/api/programs/{program.Id}", ToDto(program));
});

app.MapGet("/api/programs", async (HttpContext http, ProgramDbContext db) =>
{
    var caller = http.GetCaller();
    if (caller is null) return Results.Unauthorized();
    if (!caller.IsTrainer) return Forbidden("Only trainers can list their programs.");

    var (page, pageSize) = ReadPaging(http.Request);
    var query = db.Programs.Where(p => p.TrainerId == caller.UserId).OrderBy(p => p.Title);
    var total = await query.CountAsync();
    var items = await query.Skip((page - 1) * pageSize).Take(pageSize)
        .Select(p => new ProgramDto(p.Id, p.TrainerId, p.Title, p.Type, p.PriceCents, p.Status)).ToListAsync();
    return Results.Ok(new PagedResult<ProgramDto>(items, page, pageSize, total));
});

app.MapGet("/api/programs/{id:guid}", async (Guid id, HttpContext http, ProgramDbContext db) =>
{
    var caller = http.GetCaller();
    if (caller is null) return Results.Unauthorized();

    var program = await db.Programs.FindAsync(id);
    if (program is null) return NotFoundErr("Program not found.");
    if (!await CanViewProgram(db, program, caller)) return Forbidden("You do not have access to this program.");
    return Results.Ok(ToDto(program));
});

app.MapPut("/api/programs/{id:guid}", async (Guid id, UpdateProgramRequest req, HttpContext http, ProgramDbContext db) =>
{
    var caller = http.GetCaller();
    if (caller is null) return Results.Unauthorized();
    if (!caller.IsTrainer) return Forbidden("Only trainers can edit programs.");

    var program = await db.Programs.FindAsync(id);
    if (program is null) return NotFoundErr("Program not found.");
    if (program.TrainerId != caller.UserId) return Forbidden("You do not own this program.");

    var validation = ValidateProgramFields(req.Title, req.Type, req.PriceCents);
    if (validation is not null) return validation;
    if (!ProgramStatuses.IsValid(req.Status)) return BadReq("Status must be 'draft' or 'published'.");

    program.Title = req.Title.Trim();
    program.Type = req.Type;
    program.PriceCents = req.Type == ProgramTypes.Online ? req.PriceCents : null;
    program.Status = req.Status;
    await db.SaveChangesAsync();
    return Results.Ok(ToDto(program));
});

// =====================================================================================
// Workouts (nested under a program)
// =====================================================================================

app.MapPost("/api/programs/{programId:guid}/workouts", async (Guid programId, CreateWorkoutRequest req, HttpContext http, ProgramDbContext db) =>
{
    var caller = http.GetCaller();
    if (caller is null) return Results.Unauthorized();
    if (!caller.IsTrainer) return Forbidden("Only trainers can add workouts.");

    var program = await db.Programs.FindAsync(programId);
    if (program is null) return NotFoundErr("Program not found.");
    if (program.TrainerId != caller.UserId) return Forbidden("You do not own this program.");

    if (string.IsNullOrWhiteSpace(req.Title)) return BadReq("Title is required.");
    if (req.OrderIndex < 0) return BadReq("OrderIndex must be zero or positive.");

    var workout = new Workout
    {
        Id = Guid.NewGuid(),
        ProgramId = programId,
        Title = req.Title.Trim(),
        OrderIndex = req.OrderIndex,
    };
    db.Workouts.Add(workout);
    await db.SaveChangesAsync();
    return Results.Created($"/api/workouts/{workout.Id}", ToDto(workout));
});

app.MapGet("/api/programs/{programId:guid}/workouts", async (Guid programId, HttpContext http, ProgramDbContext db) =>
{
    var caller = http.GetCaller();
    if (caller is null) return Results.Unauthorized();

    var program = await db.Programs.FindAsync(programId);
    if (program is null) return NotFoundErr("Program not found.");
    if (!await CanViewProgram(db, program, caller)) return Forbidden("You do not have access to this program.");

    var workouts = await db.Workouts.Where(w => w.ProgramId == programId)
        .OrderBy(w => w.OrderIndex)
        .Select(w => new WorkoutDto(w.Id, w.ProgramId, w.Title, w.OrderIndex)).ToListAsync();
    return Results.Ok(workouts);
});

// =====================================================================================
// Workout exercises (prescriptions, nested under a workout)
// =====================================================================================

app.MapPost("/api/workouts/{workoutId:guid}/exercises", async (Guid workoutId, AddWorkoutExerciseRequest req, HttpContext http, ProgramDbContext db) =>
{
    var caller = http.GetCaller();
    if (caller is null) return Results.Unauthorized();
    if (!caller.IsTrainer) return Forbidden("Only trainers can add exercises to a workout.");

    var workout = await db.Workouts.Include(w => w.Program).SingleOrDefaultAsync(w => w.Id == workoutId);
    if (workout is null) return NotFoundErr("Workout not found.");
    if (workout.Program!.TrainerId != caller.UserId) return Forbidden("You do not own this workout.");

    var exercise = await db.Exercises.FindAsync(req.ExerciseId);
    if (exercise is null) return BadReq("Exercise not found.");
    if (exercise.TrainerId != caller.UserId) return Forbidden("You do not own this exercise.");

    if (req.PrescribedSets <= 0) return BadReq("PrescribedSets must be positive.");
    if (req.PrescribedReps <= 0) return BadReq("PrescribedReps must be positive.");
    if (req.PrescribedRestSeconds < 0) return BadReq("PrescribedRestSeconds cannot be negative.");
    if (req.OrderIndex < 0) return BadReq("OrderIndex must be zero or positive.");

    var we = new WorkoutExercise
    {
        Id = Guid.NewGuid(),
        WorkoutId = workoutId,
        ExerciseId = req.ExerciseId,
        PrescribedSets = req.PrescribedSets,
        PrescribedReps = req.PrescribedReps,
        PrescribedRestSeconds = req.PrescribedRestSeconds,
        OrderIndex = req.OrderIndex,
    };
    db.WorkoutExercises.Add(we);
    await db.SaveChangesAsync();
    return Results.Created($"/api/workouts/{workoutId}/exercises/{we.Id}", ToDto(we, exercise));
});

app.MapGet("/api/workouts/{workoutId:guid}/exercises", async (Guid workoutId, HttpContext http, ProgramDbContext db) =>
{
    var caller = http.GetCaller();
    if (caller is null) return Results.Unauthorized();

    var workout = await db.Workouts.Include(w => w.Program).SingleOrDefaultAsync(w => w.Id == workoutId);
    if (workout is null) return NotFoundErr("Workout not found.");
    if (!await CanViewProgram(db, workout.Program!, caller)) return Forbidden("You do not have access to this workout.");

    var items = await db.WorkoutExercises.Where(we => we.WorkoutId == workoutId)
        .OrderBy(we => we.OrderIndex)
        .Select(we => new WorkoutExerciseDto(
            we.Id, we.WorkoutId, we.ExerciseId, we.Exercise!.Name, we.Exercise.YoutubeVideoId,
            we.PrescribedSets, we.PrescribedReps, we.PrescribedRestSeconds, we.OrderIndex))
        .ToListAsync();
    return Results.Ok(items);
});

// =====================================================================================
// Assignments (manual, 1:1 flow — purchase flow comes in step 7)
// =====================================================================================

app.MapPost("/api/programs/{programId:guid}/assignments", async (Guid programId, CreateAssignmentRequest req, HttpContext http, ProgramDbContext db) =>
{
    var caller = http.GetCaller();
    if (caller is null) return Results.Unauthorized();
    if (!caller.IsTrainer) return Forbidden("Only trainers can assign programs.");

    var program = await db.Programs.FindAsync(programId);
    if (program is null) return NotFoundErr("Program not found.");
    if (program.TrainerId != caller.UserId) return Forbidden("You do not own this program.");
    if (req.ClientId == Guid.Empty) return BadReq("ClientId is required.");

    if (await db.Assignments.AnyAsync(a => a.ProgramId == programId && a.ClientId == req.ClientId))
        return Results.Conflict(new { error = "This client is already assigned to this program." });

    var assignment = new Assignment
    {
        Id = Guid.NewGuid(),
        ProgramId = programId,
        ClientId = req.ClientId,
        GrantedAt = DateTime.UtcNow,
        Source = AssignmentSources.Manual,
    };
    db.Assignments.Add(assignment);
    await db.SaveChangesAsync();
    return Results.Created($"/api/programs/{programId}/assignments/{assignment.Id}", ToDto(assignment));
});

app.MapGet("/api/programs/{programId:guid}/assignments", async (Guid programId, HttpContext http, ProgramDbContext db) =>
{
    var caller = http.GetCaller();
    if (caller is null) return Results.Unauthorized();
    if (!caller.IsTrainer) return Forbidden("Only trainers can view assignments.");

    var program = await db.Programs.FindAsync(programId);
    if (program is null) return NotFoundErr("Program not found.");
    if (program.TrainerId != caller.UserId) return Forbidden("You do not own this program.");

    var items = await db.Assignments.Where(a => a.ProgramId == programId)
        .OrderByDescending(a => a.GrantedAt)
        .Select(a => new AssignmentDto(a.Id, a.ProgramId, a.ClientId, a.GrantedAt, a.Source)).ToListAsync();
    return Results.Ok(items);
});

// Programs assigned to the calling client (their "my programs" list).
app.MapGet("/api/me/programs", async (HttpContext http, ProgramDbContext db) =>
{
    var caller = http.GetCaller();
    if (caller is null) return Results.Unauthorized();
    if (!caller.IsClient) return Forbidden("Only clients have assigned programs.");

    var (page, pageSize) = ReadPaging(http.Request);
    var query = db.Assignments.Where(a => a.ClientId == caller.UserId)
        .Join(db.Programs, a => a.ProgramId, p => p.Id, (a, p) => p)
        .OrderBy(p => p.Title);
    var total = await query.CountAsync();
    var items = await query.Skip((page - 1) * pageSize).Take(pageSize)
        .Select(p => new ProgramDto(p.Id, p.TrainerId, p.Title, p.Type, p.PriceCents, p.Status)).ToListAsync();
    return Results.Ok(new PagedResult<ProgramDto>(items, page, pageSize, total));
});

// =====================================================================================
// Coaching relationship (internal — called by Tracking Service to authorize trainer reads)
// =====================================================================================

// True when the given client is assigned to at least one program owned by the calling
// trainer. Not exposed through the gateway; reached service-to-service with the trainer's
// forwarded identity headers (architecture-guide §4 sync pattern).
app.MapGet("/api/clients/{clientId:guid}/coaching", async (Guid clientId, HttpContext http, ProgramDbContext db) =>
{
    var caller = http.GetCaller();
    if (caller is null) return Results.Unauthorized();
    if (!caller.IsTrainer) return Forbidden("Only trainers can check coaching relationships.");

    var coached = await db.Assignments
        .Join(db.Programs, a => a.ProgramId, p => p.Id, (a, p) => new { a.ClientId, p.TrainerId })
        .AnyAsync(x => x.ClientId == clientId && x.TrainerId == caller.UserId);

    return Results.Ok(new CoachingResponse(clientId, coached));
});

app.Run();

// =====================================================================================
// Helpers
// =====================================================================================

static IResult Forbidden(string message) => Results.Json(new { error = message }, statusCode: StatusCodes.Status403Forbidden);
static IResult NotFoundErr(string message) => Results.NotFound(new { error = message });
static IResult BadReq(string message) => Results.BadRequest(new { error = message });

static IResult? ValidateProgramFields(string title, string type, int? priceCents)
{
    if (string.IsNullOrWhiteSpace(title)) return BadReq("Title is required.");
    if (!ProgramTypes.IsValid(type)) return BadReq("Type must be 'online' or 'one_on_one'.");
    if (type == ProgramTypes.Online && (priceCents is null || priceCents <= 0))
        return BadReq("Online programs require a positive PriceCents.");
    return null;
}

// A program is viewable by its owning trainer, or by a client who has an assignment to it.
static async Task<bool> CanViewProgram(ProgramDbContext db, TrainingProgram program, Caller caller)
{
    if (caller.IsTrainer) return program.TrainerId == caller.UserId;
    return await db.Assignments.AnyAsync(a => a.ProgramId == program.Id && a.ClientId == caller.UserId);
}

static (int page, int pageSize) ReadPaging(HttpRequest request)
{
    var page = 1;
    var pageSize = 20;
    if (int.TryParse(request.Query["page"], out var p) && p > 0) page = p;
    if (int.TryParse(request.Query["pageSize"], out var ps) && ps is > 0 and <= 100) pageSize = ps;
    return (page, pageSize);
}
