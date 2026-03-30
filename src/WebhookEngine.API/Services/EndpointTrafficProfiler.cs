using WebhookEngine.Core.Enums;
using EndpointEntity = WebhookEngine.Core.Entities.Endpoint;

namespace WebhookEngine.API.Services;

/// <summary>
/// Traffic profile for a single endpoint, capturing rate and failure candidacy.
/// </summary>
internal sealed record EndpointTrafficProfile(
    EndpointEntity Endpoint,
    int EffectiveRatePerMinute,
    bool IsFailureCandidate);

/// <summary>
/// Stateless helper that builds traffic profiles and selects endpoints for a tick.
/// Contains endpoint selection and scoring logic extracted from DevTrafficGenerator.
/// </summary>
internal static class EndpointTrafficProfiler
{
    /// <summary>
    /// Builds a traffic profile for a single endpoint using its effective rate and failure candidacy.
    /// </summary>
    public static EndpointTrafficProfile BuildProfile(EndpointEntity endpoint, int effectiveRatePerMinute)
    {
        return new EndpointTrafficProfile(
            endpoint,
            effectiveRatePerMinute,
            IsFailureCandidate(endpoint));
    }

    /// <summary>
    /// Selects which endpoint profiles to send to in a given tick.
    /// Ensures at least one success candidate and one failure candidate when possible.
    /// </summary>
    public static List<EndpointTrafficProfile> SelectForTick(List<EndpointTrafficProfile> readyProfiles, int maxPerTick)
    {
        if (readyProfiles.Count <= maxPerTick)
            return readyProfiles;

        var result = new List<EndpointTrafficProfile>(maxPerTick);

        var successCandidates = readyProfiles.Where(p => !p.IsFailureCandidate).OrderBy(_ => Random.Shared.Next()).ToList();
        var failureCandidates = readyProfiles.Where(p => p.IsFailureCandidate).OrderBy(_ => Random.Shared.Next()).ToList();

        if (maxPerTick >= 2)
        {
            if (successCandidates.Count > 0)
            {
                result.Add(successCandidates[0]);
                successCandidates.RemoveAt(0);
            }

            if (failureCandidates.Count > 0)
            {
                result.Add(failureCandidates[0]);
                failureCandidates.RemoveAt(0);
            }
        }

        var remaining = readyProfiles
            .Except(result)
            .OrderBy(_ => Random.Shared.Next())
            .Take(Math.Max(0, maxPerTick - result.Count))
            .ToList();

        result.AddRange(remaining);
        return result;
    }

    private static bool IsFailureCandidate(EndpointEntity endpoint)
    {
        if (endpoint.Status == EndpointStatus.Failed || endpoint.Status == EndpointStatus.Degraded)
            return true;

        var url = endpoint.Url.ToLowerInvariant();

        return url.Contains("fail")
            || url.Contains("invalid")
            || url.Contains("unreachable")
            || url.Contains(":5999")
            || url.Contains(":5998")
            || url.Contains(":1/");
    }
}
