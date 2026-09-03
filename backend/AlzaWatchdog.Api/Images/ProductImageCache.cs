using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace AlzaWatchdog.Api.Images;

/// <summary>
/// Downloads product images from the alza.cz CDN and serves them back as data URIs,
/// so the API never leaks which products a user is watching to a third-party CDN.
/// Results are cached in memory with FIFO eviction and in-flight request dedup.
/// </summary>
public sealed class ProductImageCache
{
    public const string HttpClientName = "AlzaImages";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly FifoCache _cache;
    private readonly ILogger<ProductImageCache> _logger;

    public ProductImageCache(
        IHttpClientFactory httpClientFactory,
        ILogger<ProductImageCache> logger,
        int maxEntries = 1000)
    {
        _httpClientFactory = httpClientFactory;
        _cache = new FifoCache(maxEntries);
        _logger = logger;
    }

    /// <summary>Current number of cached image entries.</summary>
    public int CacheEntryCount => _cache.EntryCount;

    /// <summary>Total cache hits since process start.</summary>
    public long CacheHits => _cache.Hits;

    /// <summary>Total cache misses since process start.</summary>
    public long CacheMisses => _cache.Misses;

    public Task<string?> GetDataUriAsync(string? imageUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
            return Task.FromResult<string?>(null);

        return _cache.GetOrAddAsync(imageUrl, () => DownloadAsDataUriAsync(imageUrl, ct));
    }

    private async Task<string?> DownloadAsDataUriAsync(string imageUrl, CancellationToken ct)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.GetAsync(imageUrl, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Image download for {Url} returned {Status}.", imageUrl, (int)response.StatusCode);
                return null;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            var mime = response.Content.Headers.ContentType?.MediaType ?? InferMimeType(imageUrl);
            return $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to download product image from {Url}.", imageUrl);
            return null;
        }
    }

    private static string InferMimeType(string url)
    {
        var extension = Path.GetExtension(new Uri(url).AbsolutePath).ToLowerInvariant();
        return extension switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".avif" => "image/avif",
            ".gif" => "image/gif",
            _ => "image/jpeg",
        };
    }

    /// <summary>
    /// Size-limited string-keyed cache with FIFO eviction. Concurrent lookups for the same key are
    /// deduplicated — all callers await the same in-flight task. Null results are stored as
    /// completed tasks to avoid repeated failed lookups.
    /// </summary>
    private sealed class FifoCache
    {
        private readonly int _maxEntries;
        private readonly ConcurrentDictionary<string, Task<string?>> _entries = new();
        private readonly ConcurrentQueue<string> _order = new();
        private readonly object _evictLock = new();
        private long _hits;
        private long _misses;

        public FifoCache(int maxEntries) => _maxEntries = maxEntries;

        public int EntryCount => _entries.Count;
        public long Hits => Interlocked.Read(ref _hits);
        public long Misses => Interlocked.Read(ref _misses);

        public Task<string?> GetOrAddAsync(string key, Func<Task<string?>> factory)
        {
            if (_entries.TryGetValue(key, out var existing))
            {
                Interlocked.Increment(ref _hits);
                return existing;
            }

            Interlocked.Increment(ref _misses);

            var newTask = CaptureAsync(key, factory);
            var winner = _entries.GetOrAdd(key, newTask);

            if (winner == newTask)
            {
                _order.Enqueue(key);
                EvictIfNeeded();
            }

            return winner;
        }

        private async Task<string?> CaptureAsync(string key, Func<Task<string?>> factory)
        {
            try { return await factory(); }
            catch { return null; }
        }

        private void EvictIfNeeded()
        {
            lock (_evictLock)
            {
                while (_order.Count > _maxEntries)
                {
                    if (_order.TryDequeue(out var oldest))
                        _entries.TryRemove(oldest, out _);
                }
            }
        }
    }
}
