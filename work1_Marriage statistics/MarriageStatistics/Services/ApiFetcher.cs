using System.Net.Http;
using System.Text.Json;
using MarriageStatistics.Models;
using Serilog;

namespace MarriageStatistics.Services;

public class ApiFetcher
{
    private readonly DatabaseService _db;
    private readonly string _outputDir;
    private readonly IJsonProcessor? _jsonProcessor;
    private static readonly HttpClient _http = new HttpClient() { Timeout = TimeSpan.FromSeconds(30) };

    public ApiFetcher(DatabaseService db, string outputDir, IJsonProcessor? jsonProcessor = null)
    {
        _db = db;
        _outputDir = outputDir;
        _jsonProcessor = jsonProcessor;
        Directory.CreateDirectory(_outputDir);
    }

    /// <summary>
    /// Fetches from the given URL with retry and stores to DB and disk. Throws on critical failures.
    /// </summary>
    /// <param name="outputFileName">如果提供，會將原始 JSON 寫入此固定檔名（在 _outputDir 下），否則使用時間戳記檔名。</param>
    /// <param name="upsertDb">若為 true，會使用 UpsertApiDataAsync 更新或插入資料庫（避免重複插入）。</param>
    public async Task FetchAndStoreAsync(string url, TextWriter? output = null, string? outputFileName = null, bool upsertDb = false)
    {
    output ??= Console.Out;
    output.WriteLine($"開始從 API 取得資料: {url}");
    Log.Debug("ApiFetcher.FetchAndStoreAsync 開始: {Url}", url);

        string? json = null;
        var attempts = 0;
        var maxAttempts = 3;
        var delay = 1000;

        while (attempts < maxAttempts)
        {
            attempts++;
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                using var res = await _http.SendAsync(req);
                res.EnsureSuccessStatusCode();
                json = await res.Content.ReadAsStringAsync();
                Log.Debug("ApiFetcher fetched {Bytes} bytes from {Url}", json?.Length ?? 0, url);
                break;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "第 {Attempt} 次嘗試從 {Url} 抓取失敗", attempts, url);
                output.WriteLine($"第 {attempts} 次抓取失敗: {ex.Message}");
                if (attempts >= maxAttempts)
                {
                    output.WriteLine("已達最大重試次數，放棄抓取。");
                    return;
                }
                await Task.Delay(delay);
                delay *= 2;
            }
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            output.WriteLine("抓取到的內容為空，結束處理。");
            return;
        }

        // 簡單驗證 JSON
        try
        {
            using var doc = JsonDocument.Parse(json);
        }
        catch (Exception ex)
        {
            output.WriteLine($"取得的內容不是合法 JSON: {ex.Message}");
            return;
        }

        // 儲存至資料庫（async）
        try
        {
            if (upsertDb)
            {
                Log.Debug("ApiFetcher calling UpsertApiDataAsync for {Url}", url);
                await _db.UpsertApiDataAsync(url, json);
                Log.Debug("ApiFetcher UpsertApiDataAsync completed for {Url}", url);
            }
            else
            {
                Log.Debug("ApiFetcher calling InsertApiDataAsync for {Url}", url);
                await _db! .InsertApiDataAsync(url, json);
                Log.Debug("ApiFetcher InsertApiDataAsync completed for {Url}", url);
            }
            output.WriteLine("已儲存至本機資料庫。");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "儲存至資料庫失敗");
            output.WriteLine($"儲存至資料庫失敗: {ex.Message}");
        }

        // 也將原始 JSON 寫入 App_Data 以便現有 JSON 處理器使用
        try
        {
            var outFile = outputFileName != null
                ? Path.Combine(_outputDir, outputFileName)
                : Path.Combine(_outputDir, $"api_fetch_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");
            await File.WriteAllTextAsync(outFile, json);
            output.WriteLine($"已另存原始 JSON 至: {outFile}");
            Log.Debug("ApiFetcher wrote JSON to {OutFile}", outFile);

            // 使用注入的 JSON 處理器（如果提供），否則保留向後相容的靜態處理
            if (_jsonProcessor != null)
            {
                try
                {
                    Log.Debug("ApiFetcher invoking IJsonProcessor for {OutFile}", outFile);
                    await _jsonProcessor.ProcessAsync(outFile, output);
                    Log.Debug("ApiFetcher IJsonProcessor completed for {OutFile}", outFile);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "IJsonProcessor 處理失敗，但不影響主流程");
                }
            }
            else if (Type.GetType("MarriageStatistics.Services.JsonProcessor2, MarriageStatistics") != null)
            {
                try
                {
                    Log.Debug("ApiFetcher invoking JsonProcessor2 for {OutFile}", outFile);
                    await JsonProcessor2.ProcessJsonAsync(outFile, output);
                    Log.Debug("ApiFetcher JsonProcessor2 completed for {OutFile}", outFile);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "JsonProcessor2 處理失敗，但不影響主流程");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "寫入 JSON 檔或後續處理失敗，已繼續。{}");
            output.WriteLine($"寫入 JSON 檔或後續處理失敗: {ex.Message}");
        }
    }
}
