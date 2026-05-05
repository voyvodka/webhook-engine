using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
using WebhookEngine.Core.Entities;
using WebhookEngine.Infrastructure.Repositories;

namespace WebhookEngine.Infrastructure.Services;

/// <summary>
/// Short-TTL cache for the per-message lookups that the public send / batch
/// endpoints make on every call: event-type-by-name, event-type-by-id, and
/// the set of endpoints subscribed to a given event type.
///
/// Invalidation: a per-application <see cref="CancellationTokenSource"/>
/// is registered as a change-token on every entry. Calling
/// <see cref="InvalidateApplication"/> cancels the token and the cache
/// drops every entry for that app on next access. TTL stays as a
/// safety net for multi-node deployments where another instance mutates.
/// </summary>
public sealed class DeliveryLookupCache
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

    // The CancellationTokenSource per application is shared across all
    // entries for that app. Replacing the entry on cancel resets the
    // source so future entries get a live token.
    private static readonly ConcurrentDictionary<Guid, CancellationTokenSource> AppTokens = new();

    private readonly IMemoryCache _cache;
    private readonly EventTypeRepository _eventTypeRepository;
    private readonly EndpointRepository _endpointRepository;

    public DeliveryLookupCache(
        IMemoryCache cache,
        EventTypeRepository eventTypeRepository,
        EndpointRepository endpointRepository)
    {
        _cache = cache;
        _eventTypeRepository = eventTypeRepository;
        _endpointRepository = endpointRepository;
    }

    public async Task<EventType?> GetEventTypeByIdAsync(Guid appId, Guid eventTypeId, CancellationToken ct)
    {
        var key = $"et:id:{appId}:{eventTypeId}";
        if (_cache.TryGetValue(key, out EventType? cached))
        {
            return cached;
        }

        var fresh = await _eventTypeRepository.GetByIdAsync(appId, eventTypeId, ct);
        if (fresh is not null)
        {
            Set(appId, key, fresh);
        }
        return fresh;
    }

    public async Task<EventType?> GetEventTypeByNameAsync(Guid appId, string name, CancellationToken ct)
    {
        var key = $"et:name:{appId}:{name}";
        if (_cache.TryGetValue(key, out EventType? cached))
        {
            return cached;
        }

        var fresh = await _eventTypeRepository.GetByNameAsync(appId, name, ct);
        if (fresh is not null)
        {
            Set(appId, key, fresh);
        }
        return fresh;
    }

    public async Task<List<Endpoint>> GetSubscribedEndpointsAsync(Guid appId, Guid eventTypeId, CancellationToken ct)
    {
        var key = $"ep:sub:{appId}:{eventTypeId}";
        if (_cache.TryGetValue(key, out List<Endpoint>? cached))
        {
            return cached!;
        }

        var fresh = await _endpointRepository.GetSubscribedEndpointsAsync(appId, eventTypeId, ct);
        Set(appId, key, fresh);
        return fresh;
    }

    /// <summary>
    /// Drops every cached lookup for the given application synchronously on
    /// this node. Other nodes still rely on the 30 s TTL.
    /// </summary>
    public static void InvalidateApplication(Guid appId)
    {
        if (AppTokens.TryRemove(appId, out var source))
        {
            try
            {
                source.Cancel();
            }
            finally
            {
                source.Dispose();
            }
        }
    }

    private void Set(Guid appId, string key, object value)
    {
        var token = AppTokens.GetOrAdd(appId, _ => new CancellationTokenSource());
        var entryOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = Ttl,
            Size = 1
        };
        entryOptions.AddExpirationToken(new CancellationChangeToken(token.Token));
        _cache.Set(key, value, entryOptions);
    }
}
