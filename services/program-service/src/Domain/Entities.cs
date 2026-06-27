namespace ProgramService.Domain;

// Schema mirrors architecture-guide §5 exactly. Cross-service references (trainer_id,
// client_id, exercise references that originate from a trainer) are plain UUIDs with no
// FK to other services' databases.

/// <summary>A reusable exercise a trainer defines, with its unlisted YouTube demo video.</summary>
public class Exercise
{
    public Guid Id { get; set; }
    public Guid TrainerId { get; set; }
    public string Name { get; set; } = null!;
    public string YoutubeVideoId { get; set; } = null!;
    public string? Instructions { get; set; }
}

/// <summary>A program: a sellable/assignable container of workouts. Table name "programs".</summary>
public class TrainingProgram
{
    public Guid Id { get; set; }
    public Guid TrainerId { get; set; }
    public string Title { get; set; } = null!;

    /// <summary>"online" or "one_on_one" — see <see cref="ProgramTypes"/>.</summary>
    public string Type { get; set; } = null!;

    /// <summary>Required for online programs, null for one_on_one.</summary>
    public int? PriceCents { get; set; }

    /// <summary>"draft" or "published" — see <see cref="ProgramStatuses"/>.</summary>
    public string Status { get; set; } = null!;

    public List<Workout> Workouts { get; set; } = [];
    public List<Assignment> Assignments { get; set; } = [];
}

/// <summary>An ordered workout within a program.</summary>
public class Workout
{
    public Guid Id { get; set; }
    public Guid ProgramId { get; set; }
    public string Title { get; set; } = null!;
    public int OrderIndex { get; set; }

    public TrainingProgram? Program { get; set; }
    public List<WorkoutExercise> Exercises { get; set; } = [];
}

/// <summary>An exercise placed in a workout with its prescription (sets/reps/rest).</summary>
public class WorkoutExercise
{
    public Guid Id { get; set; }
    public Guid WorkoutId { get; set; }
    public Guid ExerciseId { get; set; }
    public int PrescribedSets { get; set; }
    public int PrescribedReps { get; set; }
    public int PrescribedRestSeconds { get; set; }
    public int OrderIndex { get; set; }

    public Workout? Workout { get; set; }
    public Exercise? Exercise { get; set; }
}

/// <summary>Grants a client access to a program (manual by a trainer, or via purchase).</summary>
public class Assignment
{
    public Guid Id { get; set; }
    public Guid ProgramId { get; set; }
    public Guid ClientId { get; set; }
    public DateTime GrantedAt { get; set; }

    /// <summary>"manual" or "purchase" — see <see cref="AssignmentSources"/>.</summary>
    public string Source { get; set; } = null!;

    public TrainingProgram? Program { get; set; }
}

public static class ProgramTypes
{
    public const string Online = "online";
    public const string OneOnOne = "one_on_one";
    public static bool IsValid(string? v) => v is Online or OneOnOne;
}

public static class ProgramStatuses
{
    public const string Draft = "draft";
    public const string Published = "published";
    public static bool IsValid(string? v) => v is Draft or Published;
}

public static class AssignmentSources
{
    public const string Manual = "manual";
    public const string Purchase = "purchase";
}
