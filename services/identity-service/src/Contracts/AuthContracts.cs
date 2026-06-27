namespace IdentityService.Contracts;

public record SignupRequest(string Name, string Email, string Password, string Role);

public record LoginRequest(string Email, string Password);

public record UserInfo(Guid Id, string Name, string Email, string Role);

public record AuthResponse(string Token, string TokenType, int ExpiresInSeconds, UserInfo User);
