using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Encodings.Web;
using MarriageStatistics.Models;
using MarriageStatistics.Services;
using MarriageStatistics;
using Serilog;

// 設定 Serilog
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console(
        outputTemplate: "{Timestamp:HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

Console.WriteLine("期中作業 - 婚姻統計資料處理與分析");
// CLI args: --web to start web UI; --fetch or env AUTO_FETCH=1 to auto-fetch API without prompt
var cmdArgs = Environment.GetCommandLineArgs();
if (cmdArgs.Contains("--web"))
{
    // Start the minimal web UI and exit console flow
    await WebHostRunner.StartAsync();
    return;
}
var autoFetch = cmdArgs.Contains("--fetch") || Environment.GetEnvironmentVariable("AUTO_FETCH") == "1";
// (API 抓取選項會在確定 projectDir 後提示，以確保路徑正確)

// 搜尋 CSV 檔案：從多個候選目錄尋找以避免在不同執行情境找不到檔案
var cwd = Directory.GetCurrentDirectory();
var baseDir = AppContext.BaseDirectory;
// 嘗試從 bin/... 往上推到專案目錄（最多 4 層），若失敗則 fallback 到目前工作目錄
string? projectDir = null;
{
	var dir = new DirectoryInfo(baseDir);
	for (int i = 0; i < 6 && dir != null; i++)
	{
		// 檢查專案檔案名稱，prioritize the current project filename
		if (File.Exists(Path.Combine(dir.FullName, "MarriageStatistics.csproj")))
		{
			projectDir = dir.FullName;
			break;
		}
		dir = dir.Parent;
	}
	if (projectDir == null) projectDir = cwd;
}

// 在確定 projectDir 後，提示是否從 API 抓取並匯入資料庫
var appDataDir = Path.Combine(projectDir, "App_Data");
Directory.CreateDirectory(appDataDir);

if (autoFetch)
{
	try
	{
		var dbPath = Path.Combine(appDataDir, "marriage.db");
		var db = new DatabaseService(dbPath);
		db.EnsureDatabase();
		var fetcher = new ApiFetcher(db, appDataDir);
		// 目標 API
		var apiUrl = "https://ws.hsinchu.gov.tw/001/Upload/1/opendata/8774/341/b95a118f-e411-4cb3-a990-99c67407fa87.json";
		await fetcher.FetchAndStoreAsync(apiUrl, Console.Out);
	}
	catch (Exception ex)
	{
		Console.WriteLine("執行 API 抓取或儲存時發生錯誤: " + ex.Message);
	}
}
else
{
	Console.Write("是否要從政府 API 抓取最新資料並匯入資料庫？(y/N): ");
	var key2 = Console.ReadKey(intercept: true);
	Console.WriteLine();
	if (key2.Key == ConsoleKey.Y)
	{
		try
		{
			var dbPath = Path.Combine(appDataDir, "marriage.db");
			var db = new DatabaseService(dbPath);
			db.EnsureDatabase();
			Log.Information("數據庫已初始化: {path}", dbPath);
			
			var fetcher = new ApiFetcher(db, appDataDir);
			Log.Information("初始化 API 抓取器，資料目錄: {dir}", appDataDir);
			
			// 目標 API
			var apiUrl = "https://ws.hsinchu.gov.tw/001/Upload/1/opendata/8774/341/b95a118f-e411-4cb3-a990-99c67407fa87.json";
			Log.Information("開始抓取 API 資料: {url}", apiUrl);
			await fetcher.FetchAndStoreAsync(apiUrl, Console.Out);
		}
		catch (Exception ex)
		{
			Console.WriteLine("執行 API 抓取或儲存時發生錯誤: " + ex.Message);
		}
	}
}

var searchDirs = new List<string> {
	cwd,
	baseDir,
	projectDir,
	Path.Combine(projectDir, "App_Data"),
	Path.Combine(cwd, "App_Data")
}.Distinct().ToList();

var candidates = new List<string>();
foreach (var d in searchDirs)
{
	if (!Directory.Exists(d)) continue;
	try
	{
		candidates.AddRange(Directory.EnumerateFiles(d, "*.csv", SearchOption.TopDirectoryOnly));
	}
	catch { }
}

// fallback: recursive search one level deep under projectDir
if (!candidates.Any())
{
	try
	{
		candidates.AddRange(Directory.EnumerateFiles(projectDir, "*.csv", SearchOption.AllDirectories).Take(50));
	}
	catch { }
}

if (!candidates.Any())
{
	Console.WriteLine("找不到任何 CSV 檔案。搜尋的目錄：");
	foreach (var d in searchDirs) Console.WriteLine("  - " + d);
	Console.WriteLine("請把 CSV 放在專案資料夾或 App_Data/ 中，或指定路徑後再執行。");
	return;
}

foreach (var csv in candidates)
{
	Console.WriteLine($"處理 CSV: {Path.GetFileName(csv)}");
	try
	{
		var rows = CsvProcessor.ReadCsv(csv);
		if (!rows.Any())
		{
			Console.WriteLine("  解析後沒有資料（可能欄位格式不同或檔案空白）");
			continue;
		}

		// 列印前幾筆
		Console.WriteLine($"  讀到 {rows.Count} 筆記錄；示例列印：");
		foreach (var r in rows.Take(5)) Console.WriteLine("    " + r);

		// 分析並列印摘要
		DataAnalyzer.PrintSummary(rows, Console.Out);

		// 寫出分析結果為 JSON（統一輸出到 summaries 子資料夾以避免被再次處理）
		var summaryJson = JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true });
		var summariesDir = Path.Combine(Path.GetDirectoryName(csv) ?? projectDir, "summaries");
		Directory.CreateDirectory(summariesDir);
		var outPath = Path.Combine(summariesDir, Path.GetFileNameWithoutExtension(csv) + ".summary.json");
		await File.WriteAllTextAsync(outPath, summaryJson);
		Console.WriteLine($"  已輸出解析後 JSON: {outPath}");
	}
	catch (Exception ex)
	{
		Console.WriteLine($"  解析/分析失敗: {ex.Message}");
	}
}

// 處理 JSON 檔案（如果有）
var jsonFiles = new List<string>();
foreach (var d in searchDirs)
{
	if (!Directory.Exists(d)) continue;
	try { jsonFiles.AddRange(Directory.EnumerateFiles(d, "*.json", SearchOption.TopDirectoryOnly)); } catch { }
}
if (!jsonFiles.Any())
{
	// try recursive, but limit
	try { jsonFiles.AddRange(Directory.EnumerateFiles(projectDir, "*.json", SearchOption.AllDirectories).Take(50)); } catch { }
}

foreach (var jf in jsonFiles.Distinct())
{
	var name = Path.GetFileName(jf);
	// 跳過 dotnet metadata 與已產生的 summary 檔，或 summaries 子資料夾內的檔案
	if (name.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase) ||
		name.EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase) ||
		name.IndexOf(".summary", StringComparison.OrdinalIgnoreCase) >= 0 ||
		jf.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(p => string.Equals(p, "summaries", StringComparison.OrdinalIgnoreCase)))
	{
		Console.WriteLine($"  跳過檔案: {name}");
		continue;
	}
	await JsonProcessor2.ProcessJsonAsync(jf, Console.Out);
}

Console.WriteLine("處理完成。");
