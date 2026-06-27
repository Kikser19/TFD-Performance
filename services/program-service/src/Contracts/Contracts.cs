namespace ProgramService.Contracts;

// --- Requests ---
public record CreateExerciseRequest(string Name, string YoutubeVideoId, string? Instructions);
public record UpdateExerciseRequest(string Name, string YoutubeVideoId, string? Instructions);

public record CreateProgramRequest(string Title, string Type, int? PriceCents);
public record UpdateProgramRequest(string Title, string Type, int? PriceCents, string Status);

public record CreateWorkoutRequest(string Title, int OrderIndex);

public record AddWorkoutExerciseRequest(
    Guid ExerciseId, int PrescribedSets, int PrescribedReps, int PrescribedRestSeconds, int OrderIndex);

public record CreateAssignmentRequest(Guid ClientId);

// --- Responses ---
public record ExerciseDto(Guid Id, Guid TrainerId, string Name, string YoutubeVideoId, string? Instructions);

public record ProgramDto(Guid Id, Guid TrainerId, string Title, string Type, int? PriceCents, string Status);

public record WorkoutDto(Guid Id, Guid ProgramId, string Title, int OrderIndex);

public record WorkoutExerciseDto(
    Guid Id, Guid WorkoutId, Guid ExerciseId, string ExerciseName, string YoutubeVideoId,
    int PrescribedSets, int PrescribedReps, int PrescribedRestSeconds, int OrderIndex);

public record AssignmentDto(Guid Id, Guid ProgramId, Guid ClientId, DateTime GrantedAt, string Source);

public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);

public record CoachingResponse(Guid ClientId, bool Coached);
