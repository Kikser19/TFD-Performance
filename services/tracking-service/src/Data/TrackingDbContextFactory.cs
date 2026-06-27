using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TrackingService.Data;

/// <summary>Design-time factory for `dotnet ef` only (see identity-service for rationale).</summary>
public class TrackingDbContextFactory : IDesignTimeDbContextFactory<TrackingDbContext>
{
    public TrackingDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TrackingDbContext>()
            .UseNpgsql("Host=localhost;Database=tracking;Username=postgres;Password=postgres")
            .Options;

        return new TrackingDbContext(options);
    }
}
