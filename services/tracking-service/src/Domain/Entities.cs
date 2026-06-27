namespace TrackingService.Domain;

// Schema mirrors architecture-guide §5. client_id, workout_id and workout_exercise_id are
// plain UUIDs referencing rows owned by Identity/Program services — no cross-DB FK.

/// <summary>One recorded session of a client performing a workout.</summary>
public class WorkoutLog
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public Guid WorkoutId { get; set; }
    public DateTime PerformedAt { get; set; }

    public List<SetLog> Sets { get; set; } = [];
}

/// <summary>One performed set within a workout log, against a prescribed workout exercise.</summary>
public class SetLog
{
    public Guid Id { get; set; }
    public Guid WorkoutLogId { get; set; }
    public Guid WorkoutExerciseId { get; set; }
    public int SetNumber { get; set; }
    public int RepsCompleted { get; set; }
    public decimal WeightUsed { get; set; }

    public WorkoutLog? WorkoutLog { get; set; }
}

/// <summary>A manually entered nutrition record (no third-party food DB in V1, §2).</summary>
public class NutritionEntry
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public DateTime LoggedAt { get; set; }
    public string FoodName { get; set; } = null!;
    public int Calories { get; set; }
    public decimal ProteinG { get; set; }
    public decimal CarbsG { get; set; }
    public decimal FatG { get; set; }
}
