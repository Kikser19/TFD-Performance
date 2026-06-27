namespace IdentityService.Domain;

/// <summary>
/// A platform user. Owned exclusively by the Identity Service (architecture-guide §5).
/// Other services reference users only by <see cref="Id"/> as a plain UUID.
/// </summary>
public class User
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string PasswordHash { get; set; } = null!;

    /// <summary>"trainer" or "client" — see <see cref="Roles"/>.</summary>
    public string Role { get; set; } = null!;

    public DateTime CreatedAt { get; set; }
}

/// <summary>The two roles V1 supports. V1 is single-trainer (architecture-guide §2).</summary>
public static class Roles
{
    public const string Trainer = "trainer";
    public const string Client = "client";

    public static bool IsValid(string? role) => role is Trainer or Client;
}
