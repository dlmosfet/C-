using System.Text.Json;
using System.Text.Json.Serialization;
using MarriageStatistics.Models;
using Serilog;

namespace MarriageStatistics.Services;

public static class JsonProcessor2
{
    private static readonly string[] SeaCountries = { "印尼", "馬來西亞", "新加坡", "菲律賓", 
        "泰國", "緬甸", "越南", "柬埔寨", "寮國" };
    
    private static readonly string[] OtherCountries = { "日本", "韓國", "美國", "加拿大", 
        "澳大利亞", "紐西蘭", "英國", "法國", "德國", "史瓦帝尼", "南非", "賴索托", "模里西斯" };

    public static async Task ProcessJsonAsync(string path, TextWriter output)
    {
        var name = Path.GetFileName(path);
        Log.Debug("[JSON處理] 開始處理檔案: {File}", name);

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            Log.Warning("[JSON處理] 檔案不存在或路徑無效: {Path}", path);
            output.WriteLine("檔案不存在，跳過。");
            return;
        }

        // 跳過 dotnet metadata 與已產生的 summary
        if (name.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase) ||
            name.IndexOf(".summary", StringComparison.OrdinalIgnoreCase) >= 0 ||
            path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(p => string.Equals(p, "summaries", StringComparison.OrdinalIgnoreCase)))
        {
            return; // 靜默跳過
        }

        try
        {
            using var fs = File.OpenRead(path);
            using var doc = await JsonDocument.ParseAsync(fs);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Array)
            {
                var items = root.EnumerateArray().ToList();
                // 針對婚姻統計資料的特殊處理
                if (items.Count > 0 && items[0].TryGetProperty("區域別", out _))
                {
                    // 只統計每個區域最新一筆（以區域別分組，取最後一筆）
                    var latestByArea = items
                        .Where(x => x.TryGetProperty("區域別", out _))
                        .GroupBy(x => x.GetProperty("區域別").GetString() ?? "未知")
                        .ToDictionary(g => g.Key, g => g.Last());

                    var stats = new Dictionary<string, (int total, int same, int different)>();
                    var nationality = new Dictionary<string, NationalityCount>();

                    output.WriteLine($"\n處理 {latestByArea.Count} 個區域（每區僅取最新一筆）...");
                    foreach (var kv in latestByArea)
                    {
                        var area = kv.Key;
                        var item = kv.Value;
                        Log.Debug("[JSON處理] 處理區域: {Area}", area);

                        // 處理基本統計
                        var total = GetInt(item, "總計_總計");
                        var same = GetInt(item, "相同性別_總計");
                        var different = GetInt(item, "不同性別_總計");

                        stats[area] = (total, same, different);

                        // 處理國籍資料
                        AddNationalityCount(nationality, "本國籍",
                            GetInt(item, "相同性別_本國籍_合計"),
                            GetInt(item, "不同性別_本國籍_合計"));

                        AddNationalityCount(nationality, "大陸",
                            GetInt(item, "相同性別_大陸地區_合計"),
                            GetInt(item, "不同性別_大陸地區_合計"));

                        AddNationalityCount(nationality, "港澳",
                            GetInt(item, "相同性別_港澳地區_合計"),
                            GetInt(item, "不同性別_港澳地區_合計"));

                        // 東南亞國家
                        foreach (var country in SeaCountries)
                        {
                            AddNationalityCount(nationality, country,
                                GetInt(item, $"相同性別_外國籍_東南亞地區_{country}"),
                                GetInt(item, $"不同性別_外國籍_東南亞地區_{country}"));
                        }

                        // 其他國籍
                        foreach (var country in OtherCountries)
                        {
                            AddNationalityCount(nationality, country,
                                GetInt(item, $"相同性別_外國籍_其他國籍_{country}"),
                                GetInt(item, $"不同性別_外國籍_其他國籍_{country}"));
                        }

                        // 其他未分類
                        AddNationalityCount(nationality, "其他",
                            GetInt(item, "相同性別_外國籍_其他國籍_其他"),
                            GetInt(item, "不同性別_外國籍_其他國籍_其他"));

                        Log.Information("[JSON處理] 區域 {Area}: 總計={Total}, 相同性別={Same}, 不同性別={Diff}", 
                            area, total, same, different);
                        output.WriteLine($"區域: {area,-10} 總計: {total,6} 相同性別: {same,6} 不同性別: {different,6}");
                    }

                    if (stats.Any())
                    {
                        output.WriteLine("\n婚姻統計摘要：");
                        output.WriteLine("區域　　　　　總計　不同性別　相同性別");
                        output.WriteLine("========================================");
                        
                        foreach (var kvp in stats.OrderByDescending(x => x.Value.total))
                        {
                            output.WriteLine($"{kvp.Key,-12} {kvp.Value.total,6} {kvp.Value.different,8} {kvp.Value.same,8}");
                        }

                        // 計算全區總計
                        var grandTotal = stats.Values.Sum(x => x.total);
                        var totalSame = stats.Values.Sum(x => x.same);
                        var totalDiff = stats.Values.Sum(x => x.different);
                        output.WriteLine("----------------------------------------");
                        output.WriteLine($"總計　　　　 {grandTotal,6} {totalDiff,8} {totalSame,8}");
                        
                        Log.Information("[JSON處理] 統計結果: 總計={Total}, 相同性別={Same}, 不同性別={Different}", 
                            grandTotal, totalSame, totalDiff);
                            
                        // 輸出國籍統計
                        if (nationality.Any())
                        {
                            output.WriteLine("\n國籍統計：");
                            output.WriteLine("國籍　　　　　總計　不同性別　相同性別");
                            output.WriteLine("========================================");
                            foreach (var kvp in nationality.OrderByDescending(x => x.Value.Total))
                            {
                                output.WriteLine($"{kvp.Key,-12} {kvp.Value.Total,6} {kvp.Value.Different,8} {kvp.Value.Same,8}");
                                Log.Debug("[JSON處理] 國籍 {Nation}: 總計={Total}, 不同性別={Diff}, 相同性別={Same}",
                                    kvp.Key, kvp.Value.Total, kvp.Value.Different, kvp.Value.Same);
                            }
                        }

                        // 寫出 summary JSON（更簡潔的格式）
                        var summary = new MarriageStats
                        {
                            Source = Path.GetFileName(path),
                            Total = grandTotal,
                            TotalDifferentGender = totalDiff,
                            TotalSameGender = totalSame,
                            ByArea = stats.Select(kvp => new AreaStats
                            {
                                Area = kvp.Key,
                                Total = kvp.Value.total,
                                SameGender = kvp.Value.same,
                                DifferentGender = kvp.Value.different
                            }).OrderByDescending(x => x.Total).ToList(),
                            NationalityBreakdown = nationality
                        };

                        var summariesDir = Path.Combine(Path.GetDirectoryName(path) ?? ".", "summaries");
                        Directory.CreateDirectory(summariesDir);
                        var outPath = Path.Combine(summariesDir, 
                            Path.GetFileNameWithoutExtension(path) + ".summary.json");
                        
                        var options = new JsonSerializerOptions 
                        { 
                            WriteIndented = true,
                            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                        };
                        
                        await File.WriteAllTextAsync(outPath, 
                            JsonSerializer.Serialize(summary, options));
                        
                        output.WriteLine($"\n已輸出統計摘要: {outPath}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            output.WriteLine($"  解析失敗: {ex.Message}");
            Log.Error(ex, "[JSON處理] 處理檔案時發生錯誤: {Message}", ex.Message);
        }
    }

    private static void AddNationalityCount(Dictionary<string, NationalityCount> dict, 
        string nation, int same, int different)
    {
        if (same == 0 && different == 0) return;
        
        if (!dict.TryGetValue(nation, out var count))
        {
            count = new NationalityCount();
            dict[nation] = count;
        }
        
        count.Same += same;
        count.Different += different;
    }

    private static int GetInt(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var value))
        {
            return ParseJsonNumber(value);
        }
        return 0;
    }

    private static int ParseJsonNumber(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String &&
            int.TryParse(element.GetString(), out var n))
            return n;
        
        if (element.ValueKind == JsonValueKind.Number &&
            element.TryGetInt32(out var m))
            return m;
        
        return 0;
    }
}