using System.Threading;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace MarriageStatistics.Services;

/// <summary>
/// Background service that periodically fetches a configured API URL and writes to a fixed file name.
/// It will upsert the DB record for the same SourceUrl to avoid creating duplicate rows.
/// Configuration via environment variables:
/// - FETCH_URL (defaults to built-in Hsinchu URL)
/// - FETCH_FILENAME (defaults to api_fetch_20251107_081759.json)
/// - FETCH_INTERVAL_MINUTES (defaults to 10)
/// - ENABLE_PERIODIC_FETCH (set to "1" to enable; default enabled when running web)
/// </summary>
public class BackgroundFetcherService : BackgroundService
{
    private readonly DatabaseService _db;
    private readonly CacheService _cache;
    private readonly string _appDataDir;
    private readonly IJsonProcessor? _jsonProcessor;

    public BackgroundFetcherService(DatabaseService db, CacheService cache, string appDataDir, IJsonProcessor? jsonProcessor = null)
    {
        _db = db;
        _cache = cache;
        _appDataDir = appDataDir;
        _jsonProcessor = jsonProcessor;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Read configuration from environment
        var url = Environment.GetEnvironmentVariable("FETCH_URL") ?? "https://ws.hsinchu.gov.tw/001/Upload/1/opendata/8774/341/b95a118f-e411-4cb3-a990-99c67407fa87.json";
        var filename = Environment.GetEnvironmentVariable("FETCH_FILENAME") ?? "api_fetch_20251107_081759.json";
    var intervalStr = Environment.GetEnvironmentVariable("FETCH_INTERVAL_MINUTES") ?? "10";
    if (!int.TryParse(intervalStr, out var minutes)) minutes = 10;
    // Enforce a minimum of 10 minutes to avoid aggressive polling
    if (minutes < 10) minutes = 10;
    var interval = TimeSpan.FromMinutes(minutes);

    Log.Information("BackgroundFetcherService 啟動 - 將每 {Minutes} 分鐘抓取 {Url} 並儲存為 {File}", minutes, url, filename);
    Log.Debug("BackgroundFetcherService config: FETCH_URL={Url}, FETCH_FILENAME={File}, FETCH_INTERVAL_MINUTES={Minutes}", url, filename, minutes);

        var fetcher = new ApiFetcher(_db, _appDataDir, _jsonProcessor);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                Log.Debug("BackgroundFetcherService 開始執行 Fetch: {Url}", url);
                using var sw = new StringWriter();
                await fetcher.FetchAndStoreAsync(url, sw, outputFileName: filename, upsertDb: true);

                // 清除快取以便前端能夠立刻看見更新
                await _cache.DeleteAsync("dashboard:stats");
                await _cache.DeleteAsync("entries:list");

                Log.Information("BackgroundFetcherService 已完成一次抓取。檔案={File}", filename);
                Log.Debug("BackgroundFetcherService Fetch result: {Message}", sw.ToString());
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "BackgroundFetcherService 抓取時發生錯誤");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (TaskCanceledException) { break; }
        }

        Log.Information("BackgroundFetcherService 停止");
    }
}
