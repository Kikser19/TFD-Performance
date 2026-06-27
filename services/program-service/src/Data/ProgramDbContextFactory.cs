using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ProgramService.Data;

/// <summary>Design-time factory for `dotnet ef` only (see identity-service for rationale).</summary>
public class ProgramDbContextFactory : IDesignTimeDbContextFactory<ProgramDbContext>
{
    public ProgramDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ProgramDbContext>()
            .UseNpgsql("Host=localhost;Database=program;Username=postgres;Password=postgres")
            .Options;

        return new ProgramDbContext(options);
    }
}
