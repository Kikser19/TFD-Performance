namespace TrackingService.Auth;

/// <summary>
/// The authenticated caller, from the trusted headers the Gateway injects (§7). This
/// service never parses JWTs. Header names are duplicated here (not shared) per §12.
/// </summary>
public record Caller(Guid UserId, string Role)
{
    public bool IsTrainer => Role == "trainer";
    public bool IsClient => Role == "client";
}

public static class CallerContextExtensions
{
    public const string UserIdHeader = "X-User-Id";
    public const string UserRoleHeader = "X-User-Role";

    public static Caller? GetCaller(this HttpContext http)
    {
        var id = http.Request.Headers[UserIdHeader].FirstOrDefault();
        var role = http.Request.Headers[UserRoleHeader].FirstOrDefault();
        if (!Guid.TryParse(id, out var userId) || string.IsNullOrWhiteSpace(role))
            return null;
        return new Caller(userId, role);
    }
}
