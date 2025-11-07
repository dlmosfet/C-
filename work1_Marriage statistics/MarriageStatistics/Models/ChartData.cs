using System.Text.Json.Serialization;

namespace MarriageStatistics.Models;

public class ChartData
{
    public List<TrendPoint> TrendData { get; set; } = new();
    public List<AreaData> AreaData { get; set; } = new();
    public GenderData GenderData { get; set; } = new();
    public Dictionary<string, int> AgeData { get; set; } = new();
    // 各國國籍分佈（例如: 日本: 12, 菲律賓: 4）
    public Dictionary<string, int> NationalityData { get; set; } = new();
    // 每國同/異性別拆分
    public Dictionary<string, NationalityCount> NationalityBreakdown { get; set; } = new();
}

public class NationalityCount
{
    public int Same { get; set; }
    public int Different { get; set; }
    public int Total => Same + Different;
}

public class TrendPoint
{
    public DateTime Date { get; set; }
    public int Total { get; set; }
}

public class AreaData
{
    public string Name { get; set; } = "";
    public int Value { get; set; }
}

public class GenderData
{
    public int SameGender { get; set; }
    public int DifferentGender { get; set; }
}