using Microsoft.EntityFrameworkCore;
using ProgramService.Domain;

namespace ProgramService.Data;

public class ProgramDbContext(DbContextOptions<ProgramDbContext> options) : DbContext(options)
{
    public DbSet<Exercise> Exercises => Set<Exercise>();
    public DbSet<TrainingProgram> Programs => Set<TrainingProgram>();
    public DbSet<Workout> Workouts => Set<Workout>();
    public DbSet<WorkoutExercise> WorkoutExercises => Set<WorkoutExercise>();
    public DbSet<Assignment> Assignments => Set<Assignment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Exercise>(e =>
        {
            e.ToTable("exercises");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(200);
            e.Property(x => x.YoutubeVideoId).IsRequired().HasMaxLength(64);
            e.Property(x => x.Instructions).HasMaxLength(4000);
            e.HasIndex(x => x.TrainerId);
        });

        modelBuilder.Entity<TrainingProgram>(e =>
        {
            e.ToTable("programs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).IsRequired().HasMaxLength(200);
            e.Property(x => x.Type).IsRequired().HasMaxLength(20);
            e.Property(x => x.Status).IsRequired().HasMaxLength(20);
            e.HasIndex(x => x.TrainerId);
            e.HasMany(x => x.Workouts).WithOne(w => w.Program!).HasForeignKey(w => w.ProgramId);
            e.HasMany(x => x.Assignments).WithOne(a => a.Program!).HasForeignKey(a => a.ProgramId);
        });

        modelBuilder.Entity<Workout>(e =>
        {
            e.ToTable("workouts");
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).IsRequired().HasMaxLength(200);
            e.HasIndex(x => x.ProgramId);
            e.HasMany(x => x.Exercises).WithOne(we => we.Workout!).HasForeignKey(we => we.WorkoutId);
        });

        modelBuilder.Entity<WorkoutExercise>(e =>
        {
            e.ToTable("workout_exercises");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.WorkoutId);
            // ExerciseId references the exercises table in THIS service's DB.
            e.HasOne(x => x.Exercise).WithMany().HasForeignKey(x => x.ExerciseId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Assignment>(e =>
        {
            e.ToTable("assignments");
            e.HasKey(x => x.Id);
            e.Property(x => x.Source).IsRequired().HasMaxLength(20);
            e.HasIndex(x => x.ClientId);
            // A client can't be assigned the same program twice.
            e.HasIndex(x => new { x.ProgramId, x.ClientId }).IsUnique();
        });
    }
}
