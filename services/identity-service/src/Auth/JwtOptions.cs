namespace IdentityService.Auth;

/// <summary>
/// JWT settings. All values come from environment variables in deployment
/// (architecture-guide §8) — never hardcode the signing key.
/// </summary>
public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "";
    public string Audience { get; set; } = "";
    public string SigningKey { get; set; } = "";
    public int ExpiryMinutes { get; set; } = 60;
}
