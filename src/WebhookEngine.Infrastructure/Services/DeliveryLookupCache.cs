using Microsoft.Extensions.Caching.Memory;
using WebhookEngine.Core.Entities;
using WebhookEngine.Infrastructure.Repositories;

namespace WebhookEngine.Infrastructure.Services;

/// <summary>
/// Short-TTL cache for the per-message lookups that the public send / batch
/// endpoints make on every call: event-type-by-name, event-type-by-id, and
/// the set of endpoints subscribed to a given event type. The 30 s TTL is a
/// trade-off — operators that just added a new endpoint or a new event type
/// see it pick up traffic within at most 30 s, and same-node mutation paths
/// invalidate the cache synchronously via Invalidate.
/// </summary>
public sealed class DeliveryLookupCache
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

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
            _cache.Set(key, fresh, Ttl);
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
            _cache.Set(key, fresh, Ttl);
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
        _cache.Set(key, fresh, Ttl);
        return fresh;
    }

}
