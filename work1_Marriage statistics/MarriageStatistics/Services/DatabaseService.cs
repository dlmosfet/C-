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
