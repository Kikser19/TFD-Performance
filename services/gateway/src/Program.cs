using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using Yarp.ReverseProxy.Transforms;

var builder = WebApplication.CreateBuilder(args);

// --- Configuration (all from env vars in deployment, see architecture-guide §8) ---
// The Gateway shares the JWT signing key/issuer/audience with the Identity Service so it
// can validate tokens locally (architecture-guide §7: the Gateway validates JWTs).
var jwtIssuer = builder.Configuration["Jwt:Issuer"]
    ?? throw new InvalidOperationException("Missing 'Jwt:Issuer'.");
var jwtAudience = builder.Configuration["Jwt:Audience"]
    ?? throw new InvalidOperationException("Missing 'Jwt:Audience'.");
var jwtSigningKey = builder.Configuration["Jwt:SigningKey"];
if (string.IsNullOrWhiteSpace(jwtSigningKey))
    throw new InvalidOperationException("Missing 'Jwt:SigningKey'.");

// --- Authentication: validate the bearer token locally ---
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Keep claim names exactly as they appear in the token ("sub", "email", role URI)
        // so the transforms below can read them deterministically.
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            RoleClaimType = ClaimTypes.Role,
        };
    });

// Default policy: every routed request must be authenticated unless its route opts out
// with AuthorizationPolicy "anonymous" (login/signup).
builder.Services.AddAuthorization(options =>
{
    options.DefaultPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

// --- Reverse proxy (YARP) ---
// Routes/clusters come from config (appsettings.json). After validating the JWT we strip
// the Authorization header and forward trusted internal identity headers instead
// (architecture-guide §7), so downstream services never re-parse the token.
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddTransforms(context =>
    {
        context.AddRequestTransform(transform =>
        {
            // Always drop any client-supplied identity headers / token before forwarding,
            // so a caller can't spoof them.
            transform.ProxyRequest.Headers.Remove("Authorization");
            transform.ProxyRequest.Headers.Remove(InternalHeaders.UserId);
            transform.ProxyRequest.Headers.Remove(InternalHeaders.UserRole);
            transform.ProxyRequest.Headers.Remove(InternalHeaders.UserEmail);

            var user = transform.HttpContext.User;
            if (user.Identity?.IsAuthenticated == true)
            {
                var id = user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
                var role = user.FindFirst(ClaimTypes.Role)?.Value;
                var email = user.FindFirst(JwtRegisteredClaimNames.Email)?.Value;

                if (!string.IsNullOrEmpty(id)) transform.ProxyRequest.Headers.Add(InternalHeaders.UserId, id);
                if (!string.IsNullOrEmpty(role)) transform.ProxyRequest.Headers.Add(InternalHeaders.UserRole, role);
                if (!string.IsNullOrEmpty(email)) transform.ProxyRequest.Headers.Add(InternalHeaders.UserEmail, email);
            }

            return default;
        });
    });

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.UseAuthentication();
app.UseAuthorization();

app.MapReverseProxy();

app.Run();

/// <summary>
/// Trusted identity headers the Gateway injects after validating the JWT. Downstream
/// services read these instead of parsing tokens. Duplicated (not shared) per
/// architecture-guide §12 — the only shared code allowed is the event-contract library.
/// </summary>
internal static class InternalHeaders
{
    public const string UserId = "X-User-Id";
    public const string UserRole = "X-User-Role";
    public const string UserEmail = "X-User-Email";
}
