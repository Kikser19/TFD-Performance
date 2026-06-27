using ProgramService.Domain;

namespace ProgramService.Contracts;

/// <summary>
/// Entity → DTO mapping. These run in memory (after materializing queries), so they may
/// be regular overloaded methods — unlike local functions, which can't be overloaded and
/// can't be used inside EF expression trees.
/// </summary>
public static class Mappers
{
    public static ExerciseDto ToDto(Exercise e) => new(e.Id, e.TrainerId, e.Name, e.YoutubeVideoId, e.Instructions);

    public static ProgramDto ToDto(TrainingProgram p) => new(p.Id, p.TrainerId, p.Title, p.Type, p.PriceCents, p.Status);

    public static WorkoutDto ToDto(Workout w) => new(w.Id, w.ProgramId, w.Title, w.OrderIndex);

    public static AssignmentDto ToDto(Assignment a) => new(a.Id, a.ProgramId, a.ClientId, a.GrantedAt, a.Source);

    public static WorkoutExerciseDto ToDto(WorkoutExercise we, Exercise e) =>
        new(we.Id, we.WorkoutId, we.ExerciseId, e.Name, e.YoutubeVideoId,
            we.PrescribedSets, we.PrescribedReps, we.PrescribedRestSeconds, we.OrderIndex);
}
