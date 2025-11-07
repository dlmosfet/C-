using Microsoft.AspNetCore.Mvc;
using MarriageStatistics.Services;
using MarriageStatistics.Models;

namespace MarriageStatistics.Controllers;

[ApiController]
[Route("api/chart-data")]
public class ChartDataController : ControllerBase
{
    private readonly DatabaseService _db;

    public ChartDataController(DatabaseService db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ChartData> Get()
    {
        return await _db.GetChartDataAsync();
    }
}