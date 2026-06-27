using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace IdentityService.Data;

/// <summary>
/// Used only by the EF Core CLI (dotnet ef) at design time so migrations can be
/// generated without booting the full app or needing a live database. The runtime
/// connection string always comes from environment variables (architecture-guide §8).
/// </summary>
public class IdentityDbContextFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    public IdentityDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql("Host=localhost;Database=identity;Username=postgres;Password=postgres")
            .Options;

        return new IdentityDbContext(options);
    }
}
