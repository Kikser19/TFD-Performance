using IdentityService.Auth;
using IdentityService.Contracts;
using IdentityService.Data;
using IdentityService.Domain;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// --- Configuration (all from env vars in deployment, see architecture-guide §8) ---
var connectionString = builder.Configuration.GetConnectionString("IdentityDb")
    ?? throw new InvalidOperationException("Missing connection string 'IdentityDb'.");

// Identity issues JWTs (it does NOT validate them — the Gateway does that, see §7).
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException("Missing 'Jwt' configuration section.");
if (string.IsNullOrWhiteSpace(jwt.SigningKey))
    throw new InvalidOperationException("Missing 'Jwt:SigningKey'.");

// --- Services ---
builder.Services.AddDbContext<IdentityDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddSingleton<JwtTokenService>();

var app = builder.Build();

// --- Apply migrations on startup (convenient for V1 docker-compose; see architecture-guide §8) ---
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
    db.Database.Migrate();
}

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

// --- POST /api/auth/signup ---
app.MapPost("/api/auth/signup", async (SignupRequest request, IdentityDbContext db, JwtTokenService tokens) =>
{
    if (string.IsNullOrWhiteSpace(request.Name))
        return Error("Name is required.");
    if (string.IsNullOrWhiteSpace(request.Email) || !request.Email.Contains('@'))
        return Error("A valid email is required.");
    if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
        return Error("Password must be at least 8 characters.");
    if (!Roles.IsValid(request.Role))
        return Error($"Role must be '{Roles.Trainer}' or '{Roles.Client}'.");

    var email = request.Email.Trim().ToLowerInvariant();
    if (await db.Users.AnyAsync(u => u.Email == email))
        return Results.Conflict(new { error = "An account with this email already exists." });

    var user = new User
    {
        Id = Guid.NewGuid(),
        Name = request.Name.Trim(),
        Email = email,
        PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
        Role = request.Role,
        CreatedAt = DateTime.UtcNow,
    };

    db.Users.Add(user);
    await db.SaveChangesAsync();

    var token = tokens.CreateToken(user);
    return Results.Created($"/api/users/{user.Id}", ToAuthResponse(user, token));
});

// --- POST /api/auth/login ---
app.MapPost("/api/auth/login", async (LoginRequest request, IdentityDbContext db, JwtTokenService tokens) =>
{
    if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        return Error("Email and password are required.");

    var email = request.Email.Trim().ToLowerInvariant();
    var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email);

    // Same response whether the email is unknown or the password is wrong.
    if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        return Results.Json(new { error = "Invalid email or password." }, statusCode: StatusCodes.Status401Unauthorized);

    var token = tokens.CreateToken(user);
    return Results.Ok(ToAuthResponse(user, token));
});

// --- GET /api/auth/me ---
// The Gateway has already validated the JWT and forwarded the user id as a trusted
// header (architecture-guide §7). This service trusts that header; it does not re-parse
// tokens. Reachable only via the Gateway (its port is not exposed to the frontend, §10).
app.MapGet("/api/auth/me", async (HttpContext http, IdentityDbContext db) =>
{
    var userIdHeader = http.Request.Headers["X-User-Id"].FirstOrDefault();
    if (!Guid.TryParse(userIdHeader, out var userId))
        return Results.Unauthorized();

    var user = await db.Users.FindAsync(userId);
    if (user is null)
        return Results.Unauthorized();

    return Results.Ok(new UserInfo(user.Id, user.Name, user.Email, user.Role));
});

app.Run();

static IResult Error(string message) => Results.BadRequest(new { error = message });

static AuthResponse ToAuthResponse(User user, AuthTokenResult token) =>
    new(token.Token, "Bearer", token.ExpiresInSeconds,
        new UserInfo(user.Id, user.Name, user.Email, user.Role));
