namespace TrackingService.Contracts;

// --- Requests ---
public record LogSetRequest(Guid WorkoutExerciseId, int SetNumber, int RepsCompleted, decimal WeightUsed);

public record LogWorkoutRequest(Guid WorkoutId, DateTime? PerformedAt, List<LogSetRequest> Sets);

public record LogNutritionRequest(
    DateTime? LoggedAt, string FoodName, int Calories, decimal ProteinG, decimal CarbsG, decimal FatG);

// --- Responses ---
public record SetLogDto(Guid Id, Guid WorkoutExerciseId, int SetNumber, int RepsCompleted, decimal WeightUsed);

public record WorkoutLogDto(
    Guid Id, Guid ClientId, Guid WorkoutId, DateTime PerformedAt, IReadOnlyList<SetLogDto> Sets);

public record NutritionEntryDto(
    Guid Id, Guid ClientId, DateTime LoggedAt, string FoodName, int Calories,
    decimal ProteinG, decimal CarbsG, decimal FatG);

public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);
