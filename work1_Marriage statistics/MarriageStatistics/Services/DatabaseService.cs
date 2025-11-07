using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Serilog;
using MarriageStatistics.Models;

namespace MarriageStatistics.Services
{
    /// <summary>
    /// Lightweight SQLite-backed data access used as the default persistence layer.
    /// Provides async-safe methods and improved error handling / logging.
    /// </summary>
    public class DatabaseService
    {
        private readonly string _dbPath;
        private readonly string _connectionString;

        public DatabaseService(string dbPath)
        {
            _dbPath = dbPath ?? throw new ArgumentNullException(nameof(dbPath));
            _connectionString = $"Data Source={_dbPath}";
            var dir = Path.GetDirectoryName(_dbPath) ?? ".";
            Directory.CreateDirectory(dir);
        }

        public void EnsureDatabase()
        {
            try
            {
                Log.Debug("EnsureDatabase 開始，dbPath={DbPath}", _dbPath);
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS ApiData (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        SourceUrl TEXT NOT NULL,
                        RawJson TEXT NOT NULL,
                        RetrievedAt TEXT NOT NULL
                    );";
                cmd.ExecuteNonQuery();

                using var idxCmd = conn.CreateCommand();
                idxCmd.CommandText = "CREATE INDEX IF NOT EXISTS idx_ApiData_RetrievedAt ON ApiData(RetrievedAt);";
                idxCmd.ExecuteNonQuery();

                Log.Debug("EnsureDatabase 完成");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "EnsureDatabase 失敗: {DbPath}", _dbPath);
                throw;
            }
        }

        public async Task InsertApiDataAsync(string url, string rawJson)
        {
            if (url == null) throw new ArgumentNullException(nameof(url));
            if (rawJson == null) throw new ArgumentNullException(nameof(rawJson));

            try
            {
                Log.Debug("InsertApiDataAsync 開始: {Url}", url);
                await using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync();

                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO ApiData (SourceUrl, RawJson, RetrievedAt) VALUES ($url, $raw, $ts);";
                cmd.Parameters.AddWithValue("$url", url);
                cmd.Parameters.AddWithValue("$raw", rawJson);
                cmd.Parameters.AddWithValue("$ts", DateTime.UtcNow.ToString("o"));

                await cmd.ExecuteNonQueryAsync();
                Log.Information("已新增 API 資料: {Url}", url);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "InsertApiDataAsync 失敗: {Url}", url);
                throw;
            }
        }

        public async Task UpsertApiDataAsync(string url, string rawJson)
        {
            if (url == null) throw new ArgumentNullException(nameof(url));
            if (rawJson == null) throw new ArgumentNullException(nameof(rawJson));

            try
            {
                Log.Debug("UpsertApiDataAsync 開始: {Url}", url);
                await using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync();

                await using var tx = conn.BeginTransaction();

                await using var findCmd = conn.CreateCommand();
                findCmd.Transaction = tx;
                findCmd.CommandText = "SELECT Id FROM ApiData WHERE SourceUrl = $url ORDER BY RetrievedAt DESC LIMIT 1;";
                findCmd.Parameters.AddWithValue("$url", url);
                var existing = await findCmd.ExecuteScalarAsync();

                var ts = DateTime.UtcNow.ToString("o");
                if (existing != null && existing != DBNull.Value)
                {
                    var id = Convert.ToInt64(existing);
                    await using var upd = conn.CreateCommand();
                    upd.Transaction = tx;
                    upd.CommandText = "UPDATE ApiData SET RawJson = $raw, RetrievedAt = $ts WHERE Id = $id;";
                    upd.Parameters.AddWithValue("$raw", rawJson);
                    upd.Parameters.AddWithValue("$ts", ts);
                    upd.Parameters.AddWithValue("$id", id);
                    await upd.ExecuteNonQueryAsync();
                }
                else
                {
                    await using var ins = conn.CreateCommand();
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
                await using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync();

                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT Id, SourceUrl, RetrievedAt FROM ApiData ORDER BY RetrievedAt DESC;";

                await using var reader = await cmd.ExecuteReaderAsync();
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
                await using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync();

                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT Id, SourceUrl, RawJson, RetrievedAt FROM ApiData WHERE Id = $id;";
                cmd.Parameters.AddWithValue("$id", id);

                await using var reader = await cmd.ExecuteReaderAsync();
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

        /// <summary>
        /// Build a ChartData object by reading pre-generated summaries in App_Data/summaries.
        /// If no summaries exist, returns an empty ChartData with default collections.
        /// </summary>
        public async Task<ChartData> GetChartDataAsync()
        {
            var result = new ChartData
            {
                TrendData = new List<TrendPoint>(),
                AreaData = new List<AreaData>(),
                GenderData = new GenderData(),
                AgeData = new Dictionary<string, int>(),
                NationalityData = new Dictionary<string, int>(),
                NationalityBreakdown = new Dictionary<string, NationalityCount>()
            };

            try
            {
                var summariesDir = Path.Combine(Path.GetDirectoryName(_dbPath) ?? ".", "summaries");
                if (!Directory.Exists(summariesDir))
                {
                    Log.Warning("找不到摘要目錄: {Dir}", summariesDir);
                    return result;
                }

                var files = new DirectoryInfo(summariesDir)
                    .GetFiles("*.summary.json")
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .Take(12)
                    .ToArray();

                if (files.Length == 0)
                {
                    Log.Warning("無摘要檔案可用");
                    return result;
                }

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    AllowTrailingCommas = true,
                    ReadCommentHandling = JsonCommentHandling.Skip
                };

                var processed = 0;
                var mapper = new CountryMapper();

                foreach (var file in files)
                {
                    try
                    {
                        var txt = await File.ReadAllTextAsync(file.FullName);
                        MarriageStats? stats = null;

                        try
                        {
                            stats = JsonSerializer.Deserialize<MarriageStats>(txt, options);
                        }
                        catch { /* ignore parse error and try fallback */ }

                        if (stats == null)
                        {
                            Log.Warning("檔案解析失敗: {File}", file.Name);
                            continue;
                        }

                        // trend
                        result.TrendData.Add(new TrendPoint { Date = file.LastWriteTime, Total = stats.Total });
                        processed++;

                        // latest file -> populate area & gender & nationality
                        if (file == files.First())
                        {
                            result.AreaData = stats.ByArea?.Select(a => new AreaData { Name = a.Area, Value = a.Total }).ToList() ?? new List<AreaData>();
                            result.GenderData = new GenderData { SameGender = stats.TotalSameGender, DifferentGender = stats.TotalDifferentGender };

                            // try to extract nationality breakdown from summary if present
                            try
                            {
                                using var doc = JsonDocument.Parse(txt);
                                if (doc.RootElement.TryGetProperty("NationalityBreakdown", out var nb))
                                {
                                    var natTotals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                                    var natBreak = new Dictionary<string, NationalityCount>(StringComparer.OrdinalIgnoreCase);

                                    foreach (var prop in nb.EnumerateObject())
                                    {
                                        var name = prop.Name ?? string.Empty;
                                        try
                                        {
                                            int s = 0, d = 0;
                                            if (prop.Value.ValueKind == JsonValueKind.Object)
                                            {
                                                if (prop.Value.TryGetProperty("Same", out var sv))
                                                {
                                                    if (sv.ValueKind == JsonValueKind.Number && sv.TryGetInt32(out var si)) s = si;
                                                    else if (sv.ValueKind == JsonValueKind.String && int.TryParse(sv.GetString(), out var ssi)) s = ssi;
                                                }
                                                if (prop.Value.TryGetProperty("Different", out var dv))
                                                {
                                                    if (dv.ValueKind == JsonValueKind.Number && dv.TryGetInt32(out var di)) d = di;
                                                    else if (dv.ValueKind == JsonValueKind.String && int.TryParse(dv.GetString(), out var ddi)) d = ddi;
                                                }
                                            }

                                            var total = s + d;
                                            if (total > 0)
                                            {
                                                var mapped = mapper.GetMappedCountry(name);
                                                if (string.IsNullOrEmpty(mapped)) mapped = name;
                                                natTotals[mapped] = total;
                                                natBreak[mapped] = new NationalityCount { Same = s, Different = d };
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            Log.Warning(ex, "解析 NationalityBreakdown 中的 {Country} 失敗", prop.Name);
                                        }
                                    }

                                    if (natTotals.Any())
                                    {
                                        result.NationalityData = natTotals.OrderByDescending(kv => kv.Value).ToDictionary(kv => kv.Key, kv => kv.Value);
                                        result.NationalityBreakdown = natBreak.OrderByDescending(kv => kv.Value.Same + kv.Value.Different).ToDictionary(kv => kv.Key, kv => kv.Value);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Log.Debug(ex, "從 summary 解析 NationalityBreakdown 失敗: {File}", file.Name);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "處理摘要檔案失敗: {File}", file.Name);
                    }
                }

                Log.Information("GetChartDataAsync: 讀取 {Count} 個摘要檔案", processed);
                return result;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "GetChartDataAsync 發生錯誤");
                return result;
            }
        }

        public async Task<DashboardStats> GetDashboardStatsAsync()
        {
            try
            {
                await using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync();

                await using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
SELECT 
    MAX(RetrievedAt) as last_update
FROM ApiData;";

                await using var reader = await cmd.ExecuteReaderAsync();
                DateTime lastUpdate = DateTime.MinValue;
                if (await reader.ReadAsync())
                {
                    if (!reader.IsDBNull(0))
                    {
                        lastUpdate = DateTime.TryParse(reader.GetString(0), out var lu) ? lu : DateTime.MinValue;
                    }
                }

                int totalMarriages = 0;
                decimal monthlyChange = 0;
                try
                {
                    var summariesDir = Path.Combine(Path.GetDirectoryName(_dbPath) ?? ".", "summaries");
                    if (Directory.Exists(summariesDir))
                    {
                        var files = new DirectoryInfo(summariesDir).GetFiles("*.summary.json").OrderByDescending(f => f.LastWriteTimeUtc).ToArray();
                        if (files.Length > 0)
                        {
                            var latest = files[0];
                            var txt = await File.ReadAllTextAsync(latest.FullName);
                            var ms = JsonSerializer.Deserialize<MarriageStats?>(txt);
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
                catch { }

                return new DashboardStats { LastUpdate = lastUpdate, TotalMarriages = totalMarriages, MonthlyChange = monthlyChange };
            }
            catch (Exception ex)
            {
                Log.Error(ex, "GetDashboardStatsAsync 失敗");
                return new DashboardStats();
            }
        }

        public IEnumerable<(int Id, string SourceUrl, string RawJson, string RetrievedAt)> QueryAll()
        {
            var list = new List<(int, string, string, string)>();
            try
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT Id, SourceUrl, RawJson, RetrievedAt FROM ApiData ORDER BY Id DESC;";
                using var rdr = cmd.ExecuteReader();
                while (rdr.Read())
                {
                    list.Add((rdr.GetInt32(0), rdr.GetString(1), rdr.GetString(2), rdr.GetString(3)));
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "QueryAll 失敗");
            }

            return list;
        }
    }
}
