using System.Text.Json;
using Serilog;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace MarriageStatistics.Services;

public class WebApiService
{
    private readonly DatabaseService _db;
    private readonly CacheService _cache;
    private readonly string _appDataDir;
    private readonly IJsonProcessor? _jsonProcessor;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public WebApiService(DatabaseService db, CacheService cache, string appDataDir, IJsonProcessor? jsonProcessor = null)
    {
        _db = db;
        _cache = cache;
        _appDataDir = appDataDir;
        _jsonProcessor = jsonProcessor;
    }

    public void ConfigureApi(WebApplication app)
    {
        // 回傳儀表板統計資料
        app.MapGet("/api/stats", async (HttpContext context) =>
        {
            var cacheKey = "dashboard:stats";
            var stats = await _cache.GetAsync<DashboardStats>(cacheKey);
            if (stats == null)
            {
                stats = await _db.GetDashboardStatsAsync();
                await _cache.SetAsync(cacheKey, stats, TimeSpan.FromMinutes(5));
            }
            Log.Debug("/api/stats returned lastUpdate={LastUpdate} totalMarriages={TotalMarriages}", stats.LastUpdate, stats.TotalMarriages);
            return Results.Json(stats, _jsonOptions);
        });

        // 回傳所有資料紀錄
        app.MapGet("/api/entries", async (HttpContext context) =>
        {
            var cacheKey = "entries:list";
            var entries = await _cache.GetAsync<List<ApiEntry>>(cacheKey);
            if (entries == null)
            {
                entries = await _db.GetEntriesAsync();
                await _cache.SetAsync(cacheKey, entries, TimeSpan.FromMinutes(1));
            }
            Log.Debug("/api/entries returned {Count} entries", entries?.Count ?? 0);
            return Results.Json(entries, _jsonOptions);
        });

        // 回傳特定紀錄的詳細資料
        app.MapGet("/api/entry/{id}", async (int id, HttpContext context) =>
        {
            var cacheKey = $"entry:{id}";
            var entry = await _cache.GetAsync<ApiEntryDetail>(cacheKey);
            if (entry == null)
            {
                entry = await _db.GetEntryDetailAsync(id);
                if (entry == null) return Results.NotFound();
                await _cache.SetAsync(cacheKey, entry, TimeSpan.FromMinutes(5));
            }
            Log.Debug("/api/entry/{Id} returned {Source}", id, entry.Source);
            return Results.Json(entry, _jsonOptions);
        });

        // 手動抓取介面已移除，改由 BackgroundFetcherService 週期性取得資料

        // 提供檔案下載
        app.MapGet("/api/download/{id}", async (int id, HttpContext context) =>
        {
            var entry = await _db.GetEntryDetailAsync(id);
            if (entry == null) return Results.NotFound();

            var filePath = Path.Combine(_appDataDir, entry.FileName);
            if (!File.Exists(filePath)) return Results.NotFound();

            return Results.File(filePath, "application/json", Path.GetFileName(filePath));
        });

            // 健康檢查 / 診斷資訊
            app.MapGet("/api/health", async (HttpContext context) =>
            {
                try
                {
                    Log.Debug("Health endpoint called");
                    var stats = await _db.GetDashboardStatsAsync();
                    var cacheInfo = await _cache.GetStatsAsync();
                    return Results.Json(new
                    {
                        status = "ok",
                        db = new
                        {
                            lastUpdate = stats.LastUpdate,
                            totalMarriages = stats.TotalMarriages,
                            monthlyChange = stats.MonthlyChange
                        },
                        cache = cacheInfo,
                        time = DateTime.UtcNow
                    }, _jsonOptions);
                }
                catch (Exception ex)
                {
                    return Results.Json(new { status = "error", message = ex.Message });
                }
            });

            app.MapGet("/api/cache/status", async () => Results.Json(await _cache.GetStatsAsync(), _jsonOptions));
    }
}

public class DashboardStats
{
    public DateTime LastUpdate { get; set; }
    public int TotalMarriages { get; set; }
    public decimal MonthlyChange { get; set; }
}

public class ApiEntry
{
    public int Id { get; set; }
    public string Source { get; set; } = "";
    public DateTime Timestamp { get; set; }
}

public class ApiEntryDetail
{
    public int Id { get; set; }
    public string Source { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public string FileName { get; set; } = "";
    public string Summary { get; set; } = "";
}

public class FetchRequest
{
    public string Url { get; set; } = "";
}