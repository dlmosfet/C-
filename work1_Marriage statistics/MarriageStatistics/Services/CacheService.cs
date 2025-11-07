using StackExchange.Redis;
using System.Text.Json;
using System.Collections.Concurrent;
using Serilog;

namespace MarriageStatistics.Services;

public class CacheService
{
    private readonly ConnectionMultiplexer? _redis;
    private readonly IDatabase? _db;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public CacheService(string? connectionString = null)
    {
        connectionString ??= Environment.GetEnvironmentVariable("REDIS_CONNECTION") ?? "localhost:6379";
        try
        {
            _redis = ConnectionMultiplexer.Connect(connectionString);
            _db = _redis.GetDatabase();
            Console.WriteLine($"[Redis] 已成功連線到快取伺服器");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Redis] 警告：無法連線到快取伺服器 ({ex.Message})，將使用記憶體快取");
            _redis = null;
            _db = null;
        }
    }

    // Simple in-memory fallback cache for when Redis is unavailable.
    private readonly ConcurrentDictionary<string, (string value, DateTimeOffset? expiry)> _memCache
        = new ConcurrentDictionary<string, (string, DateTimeOffset?)>();

    public async Task<T?> GetAsync<T>(string key)
    {
        try
        {
            if (_db != null)
            {
                var value = await _db.StringGetAsync(key);
                if (!value.HasValue) return default;
                Log.Debug("CacheService.GetAsync hit Redis key={Key}", key);
                return JsonSerializer.Deserialize<T>(value!, _jsonOptions);
            }

            if (_memCache.TryGetValue(key, out var entry))
            {
                if (entry.expiry.HasValue && entry.expiry.Value < DateTimeOffset.UtcNow)
                {
                    _memCache.TryRemove(key, out _);
                    Log.Debug("CacheService.GetAsync memcache expired key={Key}", key);
                    return default;
                }
                Log.Debug("CacheService.GetAsync hit memcache key={Key}", key);
                return JsonSerializer.Deserialize<T>(entry.value, _jsonOptions);
            }
            Log.Debug("CacheService.GetAsync miss key={Key}", key);
            return default;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "CacheService.GetAsync error for key={Key}", key);
            return default;
        }
    }

    public async Task<bool> SetAsync<T>(string key, T value, TimeSpan? expiry = null)
    {
        try
        {
            var json = JsonSerializer.Serialize(value, _jsonOptions);
            if (_db != null)
            {
                Log.Debug("CacheService.SetAsync setting Redis key={Key} exp={Exp}", key, expiry);
                return await _db.StringSetAsync(key, json, expiry ?? TimeSpan.FromDays(1));
            }

            var exp = expiry.HasValue ? DateTimeOffset.UtcNow.Add(expiry.Value) : (DateTimeOffset?)null;
            _memCache[key] = (json, exp);
            Log.Debug("CacheService.SetAsync set memcache key={Key} exp={Exp}", key, exp);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "CacheService.SetAsync error for key={Key}", key);
            return false;
        }
    }

    public async Task<bool> DeleteAsync(string key)
    {
        try
        {
            if (_db != null)
            {
                Log.Debug("CacheService.DeleteAsync deleting Redis key={Key}", key);
                return await _db.KeyDeleteAsync(key);
            }
            Log.Debug("CacheService.DeleteAsync deleting memcache key={Key}", key);
            return _memCache.TryRemove(key, out _);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "CacheService.DeleteAsync error for key={Key}", key);
            return false;
        }
    }

    public async Task<long> DeleteByPatternAsync(string pattern)
    {
        try
        {
            var count = 0L;
            if (_redis != null)
            {
                foreach (var endpoint in _redis.GetEndPoints())
                {
                    var server = _redis.GetServer(endpoint);
                    var keys = server.Keys(pattern: pattern);
                    foreach (var key in keys)
                    {
                        if (await _db!.KeyDeleteAsync(key))
                            count++;
                    }
                }
            }

            // also handle in-memory keys (simple wildcard: '*' -> contains; prefix matching)
            if (pattern.Contains('*'))
            {
                var pat = pattern.Replace("*", "");
                var toRemove = _memCache.Keys.Where(k => k.Contains(pat)).ToList();
                foreach (var k in toRemove)
                {
                    if (_memCache.TryRemove(k, out _)) count++;
                }
            }
            else
            {
                if (_memCache.TryRemove(pattern, out _)) count++;
            }

            return count;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Redis] 批次刪除快取時發生錯誤：{ex.Message}");
            return 0;
        }
    }

    public void Dispose()
    {
        try
        {
            if (_redis != null)
            {
                _redis.Close();
                _redis.Dispose();
            }
        }
        catch { }
    }

    // Return simple diagnostics about the cache state for health checks / debugging
    public Task<object> GetStatsAsync()
    {
        try
        {
            var redisConnected = _redis != null && _redis.IsConnected;
            var endpointCount = 0;
            if (_redis != null)
            {
                try { endpointCount = _redis.GetEndPoints().Length; } catch { endpointCount = 0; }
            }

            var memCount = _memCache.Count;
            var memKeys = _memCache.Keys.Take(10).ToArray();

            Log.Debug("CacheService.GetStatsAsync RedisConnected={RedisConnected} MemCount={MemCount}", redisConnected, memCount);
            return Task.FromResult<object>(new
            {
                RedisConnected = redisConnected,
                RedisEndpoints = endpointCount,
                MemCacheCount = memCount,
                MemKeysSample = memKeys
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Redis] 取得快取狀態失敗：{ex.Message}");
            return Task.FromResult<object>(new { Error = ex.Message });
        }
    }
}
