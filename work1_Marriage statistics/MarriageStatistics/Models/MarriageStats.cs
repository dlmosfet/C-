using System.Text.Json;
using System.Text.Json.Serialization;

namespace MarriageStatistics.Models;

public class MarriageStats
{
    public string Source { get; set; } = "";
    public int Total { get; set; }
    public int TotalDifferentGender { get; set; }
    public int TotalSameGender { get; set; }
    public List<AreaStats> ByArea { get; set; } = new();
}

public class AreaStats
{
    public string Area { get; set; } = "";
    public int Total { get; set; }
    public int SameGender { get; set; }
    public int DifferentGender { get; set; }
}