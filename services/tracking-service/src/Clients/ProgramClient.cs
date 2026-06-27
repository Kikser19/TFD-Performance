using System.Net;
using TrackingService.Auth;

namespace TrackingService.Clients;

/// <summary>
/// Synchronous cross-service calls into the Program Service (architecture-guide §4).
/// Tracking owns no program data, so it asks Program two questions before trusting a
/// request: can this client see this workout, and does this trainer coach this client.
///
/// Internal calls reuse the same trusted identity headers the Gateway issues (§7): we
/// forward the caller's X-User-Id / X-User-Role so Program applies its own access rules.
/// </summary>
public class ProgramClient(HttpClient http)
{
    /// <summary>
    /// Returns the set of workout-exercise ids the caller is allowed to log against for
    /// the given workout, or null if the caller has no access to it. Implemented by
    /// reusing Program's own access-controlled endpoint.
    /// </summary>
    public async Task<HashSet<Guid>?> GetAccessibleWorkoutExerciseIdsAsync(Guid workoutId, Caller caller, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/workouts/{workoutId}/exercises");
        Forward(request, caller);

        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();

        var items = await response.Content.ReadFromJsonAsync<List<WorkoutExerciseRef>>(cancellationToken: ct) ?? [];
        return items.Select(i => i.Id).ToHashSet();
    }

    /// <summary>True if the given client is coached by the calling trainer.</summary>
    public async Task<bool> IsCoachingAsync(Guid clientId, Caller trainer, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/clients/{clientId}/coaching");
        Forward(request, trainer);

        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return false;

        var result = await response.Content.ReadFromJsonAsync<CoachingResponse>(cancellationToken: ct);
        return result?.Coached ?? false;
    }

    private static void Forward(HttpRequestMessage request, Caller caller)
    {
        request.Headers.Add(CallerContextExtensions.UserIdHeader, caller.UserId.ToString());
        request.Headers.Add(CallerContextExtensions.UserRoleHeader, caller.Role);
    }

    private record WorkoutExerciseRef(Guid Id);
    private record CoachingResponse(Guid ClientId, bool Coached);
}
