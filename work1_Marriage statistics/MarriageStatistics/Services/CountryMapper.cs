using System.Text.Json;

namespace MarriageStatistics.Services;

/// <summary>
/// Simple country mapping utility. Attempts to load a JSON mapping from disk
/// (AppContext.BaseDirectory/Services/country-mapping.json). If not found,
/// falls back to a built-in dictionary.
/// </summary>
public class CountryMapper
{
    private readonly Dictionary<string, string> _map = new(StringComparer.OrdinalIgnoreCase);

    public CountryMapper()
    {
        // attempt to load json mapping
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Services", "country-mapping.json");
            if (File.Exists(path))
            {
                var txt = File.ReadAllText(path);
                using var doc = JsonDocument.Parse(txt);
                if (doc.RootElement.TryGetProperty("mappings", out var maps))
                {
                    foreach (var prop in maps.EnumerateObject())
                    {
                        var canonical = prop.Name.Trim();
                        foreach (var v in prop.Value.EnumerateArray())
                        {
                            var key = Normalize(v.GetString() ?? string.Empty);
                            if (!_map.ContainsKey(key)) _map[key] = canonical;
                        }
                    }
                }
            }
        }
        catch
        {
            // ignore and fall back to defaults
        }

        // fallback defaults (ensure some common variants exist)
        if (!_map.Any())
        {
            AddFallback("臺灣", new[] { "台灣", "臺灣", "中華民國", "taiwan", "roc" });
            AddFallback("中國（大陸）", new[] { "中國", "大陸", "china", "prc" });
            AddFallback("香港", new[] { "香港", "港", "hong kong", "hk" });
            AddFallback("澳門", new[] { "澳門", "澳", "macau", "macao" });
            AddFallback("菲律賓", new[] { "菲律賓", "philippines", "philippine", "ph" });
            AddFallback("越南", new[] { "越南", "vietnam", "vn" });
            AddFallback("緬甸", new[] { "緬甸", "burma", "myanmar" });
            AddFallback("泰國", new[] { "泰國", "thailand", "th" });
            AddFallback("日本", new[] { "日本", "japan", "jp" });
            AddFallback("韓國", new[] { "韓國", "south korea", "korea", "kr" });
            AddFallback("美國", new[] { "美國", "usa", "us", "united states" });
            AddFallback("加拿大", new[] { "加拿大", "canada", "ca" });
            AddFallback("英國", new[] { "英國", "uk", "united kingdom", "gb" });
            AddFallback("澳大利亞", new[] { "澳大利亞", "australia", "au" });
            AddFallback("紐西蘭", new[] { "紐西蘭", "new zealand", "nz" });
        }
    }

    private void AddFallback(string canonical, IEnumerable<string> variants)
    {
        foreach (var v in variants)
        {
            var key = Normalize(v);
            if (!_map.ContainsKey(key)) _map[key] = canonical;
        }
    }

    private static string Normalize(string s)
    {
        var t = s?.Trim().ToLowerInvariant() ?? string.Empty;
        // remove punctuation
        var chars = t.Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c)).ToArray();
        return new string(chars).Trim();
    }

    public string Map(string raw)
    {
        var key = Normalize(raw ?? string.Empty);
        if (string.IsNullOrEmpty(key)) return raw ?? string.Empty;
        if (_map.TryGetValue(key, out var can)) return can;
        return raw.Trim();
    }

    public bool TryMap(string raw, out string canonical)
    {
        canonical = string.Empty;
        var key = Normalize(raw ?? string.Empty);
        if (string.IsNullOrEmpty(key)) return false;
        if (_map.TryGetValue(key, out var can))
        {
            canonical = can;
            return true;
        }
        return false;
    }
}
