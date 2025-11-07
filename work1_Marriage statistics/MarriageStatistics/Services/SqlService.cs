using System.Data;
using Microsoft.Data.SqlClient;
using Serilog;

namespace MarriageStatistics.Services;

/// <summary>
/// Provides optional SQL Server persistence while keeping SQLite as a reliable fallback.
/// The service prefers SQL Server when SQL_CONN is configured; otherwise operations target SQLite.
/// </summary>
public class SqlService
{
    private readonly string? _sqlServerConn;
    private readonly DatabaseService _sqlite;

    public SqlService(string appDataDir)
    {
        _sqlServerConn = Environment.GetEnvironmentVariable("SQL_CONN");
        // fallback to sqlite in App_Data
        var sqlitePath = Path.Combine(appDataDir, "marriage.db");
        _sqlite = new DatabaseService(sqlitePath);
        _sqlite.EnsureDatabase();
    }

    public async Task EnsureSqlServerTableAsync()
    {
        if (string.IsNullOrWhiteSpace(_sqlServerConn)) return;
        try
        {
            using var conn = new SqlConnection(_sqlServerConn);
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='ApiData' AND xtype='U')
BEGIN
    CREATE TABLE ApiData (
        Id BIGINT IDENTITY(1,1) PRIMARY KEY,
        SourceUrl NVARCHAR(1024) NOT NULL,
        RawJson NVARCHAR(MAX) NOT NULL,
        RetrievedAt DATETIME2 NOT NULL
    );
END
";
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "EnsureSqlServerTableAsync 失敗，繼續使用 SQLite 為後備。" );
        }
    }

    public async Task InsertApiDataAsync(string url, string rawJson)
    {
        if (!string.IsNullOrWhiteSpace(_sqlServerConn))
        {
            try
            {
                using var conn = new SqlConnection(_sqlServerConn);
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO ApiData (SourceUrl, RawJson, RetrievedAt) VALUES (@url, @raw, @ts);";
                cmd.Parameters.AddWithValue("@url", url);
                cmd.Parameters.AddWithValue("@raw", rawJson);
                cmd.Parameters.AddWithValue("@ts", DateTime.UtcNow);
                await cmd.ExecuteNonQueryAsync();
                return;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "InsertApiDataAsync 到 SQL Server 失敗，會降級到 SQLite。" );
            }
        }

        // fallback
        await _sqlite.InsertApiDataAsync(url, rawJson);
    }

    public IEnumerable<(long Id, string SourceUrl, string RawJson, string RetrievedAt)> QueryAll(string prefer = "sqlite")
    {
        // Keep simple: prefer SQLite for quick reads; SQL Server listing can be added if needed.
        return _sqlite.QueryAll();
    }
}
