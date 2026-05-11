using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using WebhookEngine.Core.Options;
using WebhookEngine.Infrastructure.Repositories;

namespace WebhookEngine.Infrastructure.Services;

/// <summary>
/// Per-app lookup of (PortalSigningKey, AllowedOrigins) used by the portal
/// JWT auth + CORS middlewares on every request. Returns <see langword="null"/>
/// when the application doesn't exist or the portal is not enabled
/// (PortalSigningKey is null).
///
/// Mirrors the change-token + TTL invalidation pattern from
/// <see cref="DeliveryLookupCache"/> so a future rotation endpoint can call
/// <see cref="InvalidateApplication"/> for instant local invalidation
/// without waiting for the TTL to expire.
/// </summary>
public sealed record PortalAppLookup(string PortalSigningKey, string[] AllowedOrigins);

public class PortalLookupCache
{
    private static readonly ConcurrentDictionary<Guid, CancellationTokenSource> AppTokens = new();

    private readonly IMemoryCache _cache;
    private readonly ApplicationRepository _applicationRepository;
    private readonly TimeSpan _ttl;

    public PortalLookupCache(
        IMemoryCache cache,
        ApplicationRepository applicationRepository,
        IOptions<PortalAuthOptions> options)
    {
        _cache = cache;
        _applicationRepository = applicationRepository;
        _ttl = TimeSpan.FromSeconds(options.Value.LookupCacheTtlSeconds);
    }

    public async Task<PortalAppLookup?> GetAsync(Guid appId, CancellationToken ct)
    {
        var key = $"portal:app:{appId}";
        if (_cache.TryGetValue(key, out PortalAppLookup? cached))
        {
            return cached;
        }

        var app = await _applicationRepository.GetByIdAsync(appId, ct);
        if (app is null || string.IsNullOrEmpty(app.PortalSigningKey))
        {
            return null;
        }

        var origins = ParseOrigins(app.AllowedPortalOriginsJson);
        var lookup = new PortalAppLookup(app.PortalSigningKey, origins);
        Set(appId, key, lookup);
        return lookup;
    }

    /// <summary>
    /// Drops every cached portal lookup for the given application synchronously
    /// on this node. Other nodes still rely on <see cref="PortalAuthOptions.LookupCacheTtlSeconds"/>.
    /// </summary>
    public static void InvalidateApplication(Guid appId)
    {
        if (AppTokens.TryRemove(appId, out var source))
        {
            CancelAndDispose(source);
        }
    }

    private void Set(Guid appId, string key, PortalAppLookup value)
    {
        // A fresh CTS per Set so a previous InvalidateApplication that
        // already cancelled+disposed the prior token can't cause a
        // CancellationChangeToken to be constructed over a disposed
        // source. AddOrUpdate atomically swaps; the previous token is
        // cancelled (so any in-flight cache entries bound to it are
        // invalidated) and disposed.
        var fresh = new CancellationTokenSource();
        var current = AppTokens.AddOrUpdate(
            appId,
            fresh,
            (_, existing) =>
            {
                CancelAndDispose(existing);
                return fresh;
            });

        var entryOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = _ttl,
            Size = 1
        };
        entryOptions.AddExpirationToken(new CancellationChangeToken(current.Token));
        _cache.Set(key, value, entryOptions);
    }

    private static void CancelAndDispose(CancellationTokenSource source)
    {
        try
        {
            source.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Concurrent Invalidate / Set already disposed it; safe to swallow.
        }
        finally
        {
            source.Dispose();
        }
    }

    private static string[] ParseOrigins(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<string[]>(json);
            return parsed ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
