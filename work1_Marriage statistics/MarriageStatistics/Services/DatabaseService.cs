using Microsoft.Data.Sqlite;
using Serilog;
using System.Text.Json;
using MarriageStatistics.Models;

namespace MarriageStatistics.Services;

/// <summary>
/// Lightweight SQLite-backed data access used as the default persistence layer.
/// Provides async-safe methods and improved error handling / logging.
/// </summary>
public class DatabaseService
{
    private readonly string _dbPath;
    private readonly string _connString;

    public DatabaseService(string dbPath)
    {
        _dbPath = dbPath;
        _connString = $"Data Source={_dbPath}";
        var dir = Path.GetDirectoryName(_dbPath) ?? ".";
        Directory.CreateDirectory(dir);
    }

    public void EnsureDatabase()
    {
        try
        {
            Log.Debug("EnsureDatabase 開始，dbPath={DbPath}", _dbPath);
            using var conn = new SqliteConnection(_connString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS ApiData (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    SourceUrl TEXT NOT NULL,
    RawJson TEXT NOT NULL,
    RetrievedAt TEXT NOT NULL
);
";
            cmd.ExecuteNonQuery();
            Log.Debug("EnsureDatabase: ApiData table ensured");
            // 建立索引以加速依 RetrievedAt 的查詢
            using var idxCmd = conn.CreateCommand();
            idxCmd.CommandText = "CREATE INDEX IF NOT EXISTS idx_ApiData_RetrievedAt ON ApiData(RetrievedAt);";
            idxCmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "EnsureDatabase 失敗: {DbPath}", _dbPath);
            throw;
        }
    }

    public async Task InsertApiDataAsync(string url, string rawJson)
    {
        try
        {
            Log.Debug("InsertApiDataAsync 開始: {Url}", url);
            using var conn = new SqliteConnection(_connString);
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO ApiData (SourceUrl, RawJson, RetrievedAt) VALUES ($url, $raw, $ts);";
            cmd.Parameters.AddWithValue("$url", url);
            cmd.Parameters.AddWithValue("$raw", rawJson);
            cmd.Parameters.AddWithValue("$ts", DateTime.UtcNow.ToString("o"));
            await cmd.ExecuteNonQueryAsync();
            Log.Debug("InsertApiDataAsync completed for {Url}", url);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "InsertApiDataAsync 失敗: {Url}", url);
            throw;
        }
    }

    /// <summary>
    /// Insert or update the latest ApiData row for the given SourceUrl.
    /// If an existing record for the URL exists, update its RawJson and RetrievedAt; otherwise insert a new row.
    /// </summary>
    public async Task UpsertApiDataAsync(string url, string rawJson)
    {
        try
        {
            Log.Debug("UpsertApiDataAsync 開始: {Url}", url);
            using var conn = new SqliteConnection(_connString);
            await conn.OpenAsync();
            using var tx = conn.BeginTransaction();

            // Find latest id for this source
            using var findCmd = conn.CreateCommand();
            findCmd.Transaction = tx;
            findCmd.CommandText = "SELECT Id FROM ApiData WHERE SourceUrl = $url ORDER BY RetrievedAt DESC LIMIT 1;";
            findCmd.Parameters.AddWithValue("$url", url);
            var existing = await findCmd.ExecuteScalarAsync();

            var ts = DateTime.UtcNow.ToString("o");
            if (existing != null && existing != DBNull.Value)
            {
                var id = Convert.ToInt64(existing);
                using var upd = conn.CreateCommand();
                upd.Transaction = tx;
                upd.CommandText = "UPDATE ApiData SET RawJson = $raw, RetrievedAt = $ts WHERE Id = $id;";
                upd.Parameters.AddWithValue("$raw", rawJson);
                upd.Parameters.AddWithValue("$ts", ts);
                upd.Parameters.AddWithValue("$id", id);
                await upd.ExecuteNonQueryAsync();
            }
            else
            {
                using var ins = conn.CreateCommand();
                ins.Transaction = tx;
                ins.CommandText = "INSERT INTO ApiData (SourceUrl, RawJson, RetrievedAt) VALUES ($url, $raw, $ts);";
                ins.Parameters.AddWithValue("$url", url);
                ins.Parameters.AddWithValue("$raw", rawJson);
                ins.Parameters.AddWithValue("$ts", ts);
                await ins.ExecuteNonQueryAsync();
            }

            tx.Commit();
            Log.Debug("UpsertApiDataAsync 完成: {Url}", url);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "UpsertApiDataAsync 失敗: {Url}", url);
            throw;
        }
    }

    public async Task<List<ApiEntry>> GetEntriesAsync()
    {
        var entries = new List<ApiEntry>();
        try
        {
            using var conn = new SqliteConnection(_connString);
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, SourceUrl, RetrievedAt FROM ApiData ORDER BY RetrievedAt DESC;";

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                entries.Add(new ApiEntry
                {
                    Id = reader.GetInt32(0),
                    Source = reader.GetString(1),
                    Timestamp = DateTime.TryParse(reader.GetString(2), out var ts) ? ts : DateTime.MinValue
                });
            }
            Log.Debug("GetEntriesAsync returned {Count} entries", entries.Count);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "GetEntriesAsync 失敗");
        }

        return entries;
    }

    public async Task<ApiEntryDetail?> GetEntryDetailAsync(int id)
    {
        try
        {
            using var conn = new SqliteConnection(_connString);
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, SourceUrl, RawJson, RetrievedAt FROM ApiData WHERE Id = $id;";
            cmd.Parameters.AddWithValue("$id", id);

            using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return null;

            var raw = reader.GetString(2);
            return new ApiEntryDetail
            {
                Id = reader.GetInt32(0),
                Source = reader.GetString(1),
                FileName = $"data_{id}.json",
                Summary = raw.Length > 120 ? raw[..120] + "..." : raw,
                Timestamp = DateTime.TryParse(reader.GetString(3), out var tts) ? tts : DateTime.MinValue
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "GetEntryDetailAsync 失敗: {Id}", id);
            return null;
        }
        finally
        {
            Log.Debug("GetEntryDetailAsync finished for id={Id}", id);
        }
    }

    public async Task<ChartData> GetChartDataAsync()
    {
        try
        {
            var summariesDir = Path.Combine(Path.GetDirectoryName(_dbPath) ?? ".", "summaries");
            var result = new ChartData
            {
                TrendData = new List<TrendPoint>(),
                AreaData = new List<AreaData>(),
                GenderData = new GenderData(),
                AgeData = new Dictionary<string, int>()
            };

            if (Directory.Exists(summariesDir))
            {
                var files = new DirectoryInfo(summariesDir)
                    .GetFiles("*.summary.json")
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .ToArray();

                Log.Information("找到 {Count} 個摘要檔案用於圖表資料", files.Length);

                if (files.Length > 0)
                {
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        AllowTrailingCommas = true,
                        ReadCommentHandling = JsonCommentHandling.Skip
                    };

                    bool nationalityFound = false;
                    foreach (var file in files.Take(12)) // 取最近12個檔案作為趨勢資料
                    {
                        try
                        {
                            var txt = await File.ReadAllTextAsync(file.FullName);
                            Log.Debug("讀取檔案 {File} 內容: {Content}", file.Name, txt);

                            // 嘗試在最近的檔案中找出包含國籍欄位的檔案（只取第一個有欄位的檔案）
                            if (!nationalityFound)
                            {
                                try
                                {
                                    using var docCheck = JsonDocument.Parse(txt);
                                    var rootCheck = docCheck.RootElement;
                                    if (rootCheck.ValueKind == JsonValueKind.Object)
                                    {
                                        var mapper = new CountryMapper();
                                        // 若有任何屬性名稱包含 國籍 或 外國籍，或欄位名稱中含有可被 mapping 的 token，採用此檔案為來源
                                        var anyNat = rootCheck.EnumerateObject().Any(p => p.Name.Contains("外國籍") || p.Name.Contains("國籍") ||
                                            p.Name.Split(new[] { '_', ' ', '-' }, StringSplitOptions.RemoveEmptyEntries).Any(tok => mapper.TryMap(tok, out _)));

                                        if (anyNat)
                                        {
                                            try
                                            {
                                                var natTotals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                                                var natBreak = new Dictionary<string, NationalityCount>(StringComparer.OrdinalIgnoreCase);

                                                foreach (var prop in rootCheck.EnumerateObject())
                                                {
                                                    var name = prop.Name ?? string.Empty;
                                                    // 若欄位並非含國籍相關字眼且也不包含 mapping token，略過
                                                    var tokens = name.Split(new[] { '_', ' ', '-' }, StringSplitOptions.RemoveEmptyEntries);
                                                    if (!name.Contains("外國籍") && !name.Contains("國籍") && !tokens.Any(t => mapper.TryMap(t, out _)))
                                                        continue;

                                                    // 嘗試以最後一段為國別，若無 mapping 再尋找 tokens 中第一個可 map 的
                                                    var parts = tokens;
                                                    var rawCountry = parts.Length > 0 ? parts[^1] : name;
                                                    if (rawCountry.Contains("計") || rawCountry.Contains("小計") || rawCountry.Contains("已設籍") || rawCountry.Contains("未設籍"))
                                                        continue;

                                                    int value = 0;
                                                    try
                                                    {
                                                        if (prop.Value.ValueKind == JsonValueKind.Number)
                                                            value = prop.Value.GetInt32();
                                                        else if (prop.Value.ValueKind == JsonValueKind.String)
                                                        {
                                                            var sval = prop.Value.GetString() ?? "0";
                                                            int.TryParse(sval, out value);
                                                        }
                                                    }
                                                    catch { }
                                                    if (value == 0) continue;

                                                    var isSame = name.Contains("相同性別") || (name.Contains("同性別") && !name.Contains("不同")) || name.Contains("相同");
                                                    var isDifferent = name.Contains("不同性別") || name.Contains("異性別") || name.Contains("不同");

                                                    string country;
                                                    if (!mapper.TryMap(rawCountry, out country))
                                                    {
                                                        // try other tokens
                                                        country = parts.Select(p => p.Trim()).FirstOrDefault(p => mapper.TryMap(p, out _)) ?? rawCountry;
                                                        // if mapped, convert
                                                        if (mapper.TryMap(country, out var c2)) country = c2;
                                                    }

                                                    if (natTotals.ContainsKey(country)) natTotals[country] += value;
                                                    else natTotals[country] = value;

                                                    if (!natBreak.TryGetValue(country, out var nc))
                                                    {
                                                        nc = new NationalityCount { Same = 0, Different = 0 };
                                                        natBreak[country] = nc;
                                                    }

                                                    if (isSame) nc.Same += value;
                                                    else if (isDifferent) nc.Different += value;
                                                    else nc.Different += value;
                                                }

                                                result.NationalityData = natTotals.OrderByDescending(kv => kv.Value).ToDictionary(kv => kv.Key, kv => kv.Value);
                                                result.NationalityBreakdown = natBreak.OrderByDescending(kv => (kv.Value.Same + kv.Value.Different)).ToDictionary(kv => kv.Key, kv => kv.Value);
                                                nationalityFound = true;
                                            }
                                            catch (Exception ex)
                                            {
                                                Log.Warning(ex, "解析 summary 的國籍欄位失敗 (初始檢查): {File}", file.Name);
                                            }
                                        }
                                    }
                                }
                                catch { }
                            }

                            MarriageStats? stats = null;
                            try
                            {
                                stats = JsonSerializer.Deserialize<MarriageStats>(txt, options);
                            }
                            catch { stats = null; }

                            // 若為舊版陣列格式，嘗試解析為 List<MarriageCount> 並轉換
                            if (stats == null)
                            {
                                try
                                {
                                    var arr = JsonSerializer.Deserialize<List<MarriageCount>>(txt, options);
                                    if (arr != null && arr.Count > 0)
                                    {
                                        var total = arr.Sum(a => a.Total);
                                        stats = new MarriageStats
                                        {
                                            Source = file.Name,
                                            Total = total,
                                            TotalDifferentGender = total,
                                            TotalSameGender = 0,
                                            ByArea = new List<AreaStats>()
                                        };
                                        Log.Debug("已將舊版陣列 {File} 轉換為 MarriageStats，總計={Total}", file.Name, total);
                                    }
                                }
                                catch { /* ignore */ }
                            }

                            if (stats == null)
                            {
                                Log.Warning("檔案 {File} 解析為空或不支援的格式，略過", file.Name);
                                continue;
                            }

                            // 添加趨勢資料點
                            result.TrendData.Add(new TrendPoint 
                            { 
                                Date = file.LastWriteTime,
                                Total = stats.Total
                            });

                            // 對於最新的檔案，更新其他圖表資料
                            if (file == files[0])
                            {
                                // 更新區域分布資料
                                result.AreaData = stats.ByArea
                                    .Select(a => new AreaData { Name = a.Area, Value = a.Total })
                                    .ToList();

                                // 更新性別比例資料
                                result.GenderData = new GenderData 
                                {
                                    SameGender = stats.TotalSameGender,
                                    DifferentGender = stats.TotalDifferentGender
                                };

                                // 嘗試從原始 summary JSON 中擷取與「外國籍」或「國籍」相關的欄位
                                try
                                {
                                    using var doc = JsonDocument.Parse(txt);
                                    var root = doc.RootElement;

                                    var mapper = new CountryMapper();
                                    var natTotals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                                    var natBreak = new Dictionary<string, NationalityCount>(StringComparer.OrdinalIgnoreCase);

                                    foreach (var prop in root.EnumerateObject())
                                    {
                                        var name = prop.Name ?? string.Empty;
                                        if (!name.Contains("外國籍") && !name.Contains("國籍"))
                                            continue;

                                        // 從欄位名稱取出可能的國家片段（最後一段）
                                        var parts = name.Split('_', StringSplitOptions.RemoveEmptyEntries);
                                        var rawCountry = parts.Length > 0 ? parts[^1] : name;

                                        // 忽略彙整字眼
                                        if (rawCountry.Contains("計") || rawCountry.Contains("小計") || rawCountry.Contains("已設籍") || rawCountry.Contains("未設籍"))
                                            continue;

                                        int value = 0;
                                        try
                                        {
                                            if (prop.Value.ValueKind == JsonValueKind.Number)
                                            {
                                                value = prop.Value.GetInt32();
                                            }
                                            else if (prop.Value.ValueKind == JsonValueKind.String)
                                            {
                                                var sval = prop.Value.GetString() ?? "0";
                                                int.TryParse(sval, out value);
                                            }
                                        }
                                        catch { }

                                        if (value == 0) continue;

                                        // 判定同/異性別
                                        var isSame = name.Contains("相同性別") || (name.Contains("同性別") && !name.Contains("不同")) || name.Contains("相同");
                                        var isDifferent = name.Contains("不同性別") || name.Contains("異性別") || name.Contains("不同");

                                        var country = mapper.Map(rawCountry);

                                        // 總計
                                        if (natTotals.ContainsKey(country)) natTotals[country] += value;
                                        else natTotals[country] = value;

                                        // breakdown
                                        if (!natBreak.TryGetValue(country, out var nc))
                                        {
                                            nc = new NationalityCount { Same = 0, Different = 0 };
                                            natBreak[country] = nc;
                                        }

                                        if (isSame) nc.Same += value;
                                        else if (isDifferent) nc.Different += value;
                                        else
                                        {
                                            // 若欄位未明確指示，保守地視為不同性別
                                            nc.Different += value;
                                        }
                                    }

                                    // 指派回結果（排序）
                                    result.NationalityData = natTotals.OrderByDescending(kv => kv.Value).ToDictionary(kv => kv.Key, kv => kv.Value);
                                    result.NationalityBreakdown = natBreak.OrderByDescending(kv => (kv.Value.Same + kv.Value.Different)).ToDictionary(kv => kv.Key, kv => kv.Value);
                                }
                                catch (Exception ex)
                                {
                                    Log.Warning(ex, "解析 summary 的國籍欄位失敗: {File}", file.Name);
                                }

                                Log.Information("已更新最新的圖表資料: 區域數={Areas}, 同性={Same}, 異性={Different}", 
                                    result.AreaData.Count, result.GenderData.SameGender, result.GenderData.DifferentGender);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, "處理檔案 {File} 時發生錯誤", file.Name);
                        }
                    }

                    Log.Information("圖表資料已更新: 趨勢資料點={TrendPoints}, 區域={Areas}, 同性={Same}, 異性={Different}", 
                        result.TrendData.Count, result.AreaData.Count, 
                        result.GenderData.SameGender, result.GenderData.DifferentGender);
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "GetChartDataAsync 失敗");
            return new ChartData();
        }
    }

    public async Task<DashboardStats> GetDashboardStatsAsync()
    {
        try
        {
            using var conn = new SqliteConnection(_connString);
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT 
    MAX(RetrievedAt) as last_update
FROM ApiData;";

            using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return new DashboardStats();

            DateTime lastUpdate;
            if (reader.IsDBNull(0)) lastUpdate = DateTime.MinValue;
            else
            {
                var s = reader.GetString(0);
                lastUpdate = DateTime.TryParse(s, out var luv) ? luv : DateTime.MinValue;
            }

            // 嘗試從 summaries 資料夾讀取最新的 summary.json 以取得 TotalMarriages 與 MonthlyChange
            var summariesDir = Path.Combine(Path.GetDirectoryName(_dbPath) ?? ".", "summaries");
            int totalMarriages = 0;
            decimal monthlyChange = 0;

            try
            {
                Log.Information("嘗試從 {dir} 讀取統計摘要", summariesDir);
                if (Directory.Exists(summariesDir))
                {
                    var files = new DirectoryInfo(summariesDir).GetFiles("*.summary.json").OrderByDescending(f => f.LastWriteTimeUtc).ToArray();
                    Log.Information("找到 {count} 個摘要檔案", files.Length);
                    if (files.Length > 0)
                    {
                        var latest = files[0];
                        Log.Information("讀取最新摘要檔案: {file}", latest.FullName);
                        var txt = await File.ReadAllTextAsync(latest.FullName);
                        Log.Debug("摘要檔案內容: {content}", txt);
                        var ms = JsonSerializer.Deserialize<MarriageStats?>(txt);
                        if (ms != null)
                        {
                            Log.Information("已讀取摘要資料: 總計={total}, 相同性別={same}, 不同性別={diff}", 
                                ms.Total, ms.TotalSameGender, ms.TotalDifferentGender);
                        }
                        else
                        {
                            Log.Warning("摘要資料解析失敗，檔案內容可能有問題");
                        }
                        
                        if (ms != null) totalMarriages = ms.Total;

                        if (files.Length > 1)
                        {
                            var prev = files[1];
                            var ptxt = await File.ReadAllTextAsync(prev.FullName);
                            var pms = JsonSerializer.Deserialize<MarriageStats?>(ptxt);
                            if (pms != null && pms.Total != 0)
                            {
                                monthlyChange = Math.Round((decimal)(totalMarriages - pms.Total) / pms.Total * 100M, 2);
                            }
                        }
                    }
                }
            }
            catch { /* 忽略 summary 讀取錯誤，仍回傳基本資訊 */ }

            return new DashboardStats
            {
                LastUpdate = lastUpdate,
                TotalMarriages = totalMarriages,
                MonthlyChange = monthlyChange
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "GetDashboardStatsAsync 失敗");
            return new DashboardStats();
        }
    }

    public IEnumerable<(long Id, string SourceUrl, string RawJson, string RetrievedAt)> QueryAll()
    {
        var list = new List<(long, string, string, string)>();
        try
        {
            using var conn = new SqliteConnection(_connString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, SourceUrl, RawJson, RetrievedAt FROM ApiData ORDER BY Id DESC;";
            using var rdr = cmd.ExecuteReader();
            while (rdr.Read())
            {
                list.Add((rdr.GetInt64(0), rdr.GetString(1), rdr.GetString(2), rdr.GetString(3)));
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "QueryAll 失敗");
        }

        return list;
    }
}
