using Microsoft.EntityFrameworkCore;
using TrackingService.Domain;

namespace TrackingService.Data;

public class TrackingDbContext(DbContextOptions<TrackingDbContext> options) : DbContext(options)
{
    public DbSet<WorkoutLog> WorkoutLogs => Set<WorkoutLog>();
    public DbSet<SetLog> SetLogs => Set<SetLog>();
    public DbSet<NutritionEntry> NutritionEntries => Set<NutritionEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WorkoutLog>(e =>
        {
            e.ToTable("workout_logs");
            e.HasKey(x => x.Id);
            e.Property(x => x.PerformedAt).IsRequired();
            e.HasIndex(x => x.ClientId);
            e.HasIndex(x => x.WorkoutId);
            e.HasMany(x => x.Sets).WithOne(s => s.WorkoutLog!).HasForeignKey(s => s.WorkoutLogId);
        });

        modelBuilder.Entity<SetLog>(e =>
        {
            e.ToTable("set_logs");
            e.HasKey(x => x.Id);
            e.Property(x => x.WeightUsed).HasPrecision(7, 2);
            e.HasIndex(x => x.WorkoutLogId);
        });

        modelBuilder.Entity<NutritionEntry>(e =>
        {
            e.ToTable("nutrition_entries");
            e.HasKey(x => x.Id);
            e.Property(x => x.FoodName).IsRequired().HasMaxLength(200);
            e.Property(x => x.ProteinG).HasPrecision(7, 2);
            e.Property(x => x.CarbsG).HasPrecision(7, 2);
            e.Property(x => x.FatG).HasPrecision(7, 2);
            e.HasIndex(x => x.ClientId);
            e.HasIndex(x => new { x.ClientId, x.LoggedAt });
        });
    }
}
