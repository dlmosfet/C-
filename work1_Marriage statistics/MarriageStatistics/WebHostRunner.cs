using MarriageStatistics.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using Serilog;
using Serilog.Events;

namespace MarriageStatistics;

public static class WebHostRunner
{
    public static async Task StartAsync()
    {
        try
        {
            // 設定 Serilog
            var isDebug = (Environment.GetEnvironmentVariable("DEBUG") ?? "0") == "1" ||
                          string.Equals(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), "Development", StringComparison.OrdinalIgnoreCase);

            var loggerCfg = new LoggerConfiguration()
                .MinimumLevel.Is(isDebug ? LogEventLevel.Debug : LogEventLevel.Information)
                .MinimumLevel.Override("Microsoft", isDebug ? LogEventLevel.Debug : LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .WriteTo.Console();

            // 只有在非除錯模式下才啟用每日檔案輪替（除錯時也保留檔案方便追蹤）
            loggerCfg = loggerCfg.WriteTo.File(
                    Path.Combine("logs", "app-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7);

            Log.Logger = loggerCfg.CreateLogger();

            var builder = WebApplication.CreateBuilder();
            
            // 加入 Serilog
            builder.Host.UseSerilog();
            
            // 設定控制器
            builder.Services.AddControllers();
            
            // 優先尋找專案目錄中的 App_Data（與 CLI 模式一致），若找不到則使用發行/執行目錄下的 App_Data
            string? projectDir = null;
            var dirInfo = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 6 && dirInfo != null; i++)
            {
                if (File.Exists(Path.Combine(dirInfo.FullName, "MarriageStatistics.csproj")))
                {
                    projectDir = dirInfo.FullName;
                    break;
                }
                dirInfo = dirInfo.Parent;
            }

            var appDataDir = projectDir != null
                ? Path.Combine(projectDir, "App_Data")
                : Path.Combine(AppContext.BaseDirectory, "App_Data");

            Directory.CreateDirectory(appDataDir);
            Directory.CreateDirectory("logs");

            builder.Services.AddSingleton(sp => new DatabaseService(Path.Combine(appDataDir, "marriage.db")));
            builder.Services.AddSingleton(sp => new SqlService(appDataDir));
            builder.Services.AddSingleton(sp => new CacheService());
            // Json processor for DI
            builder.Services.AddSingleton<IJsonProcessor, JsonProcessorAdapter>();
            builder.Services.AddSingleton(sp => new WebApiService(
                sp.GetRequiredService<DatabaseService>(),
                sp.GetRequiredService<CacheService>(),
                appDataDir,
                sp.GetService<IJsonProcessor>()
            ));

            // 註冊背景週期抓取服務（每 N 分鐘抓一次，預設 10 分鐘）
            builder.Services.AddSingleton<IHostedService>(sp => new BackgroundFetcherService(
                sp.GetRequiredService<DatabaseService>(),
                sp.GetRequiredService<CacheService>(),
                appDataDir,
                sp.GetService<IJsonProcessor>()
            ));

            // 加入 Swagger
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo
                {
                    Title = "婚姻統計資料 API",
                    Version = "v1",
                    Description = "提供婚姻統計資料的查詢與分析功能"
                });
            });

            var app = builder.Build();

            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI(c =>
                {
                    c.SwaggerEndpoint("/swagger/v1/swagger.json", "婚姻統計資料 API v1");
                });
            }

            // 設定靜態檔案與路由
            app.UseDefaultFiles();
            app.UseStaticFiles();
            
            // 啟用路由與控制器
            app.UseRouting();
            app.MapControllers();

            // 全域錯誤處理
            app.UseExceptionHandler(errorApp =>
            {
                errorApp.Run(async context =>
                {
                    var exceptionHandlerPathFeature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerPathFeature>();
                    var exception = exceptionHandlerPathFeature?.Error;

                    Log.Error(exception, "處理請求時發生未處理的錯誤");

                    context.Response.StatusCode = 500;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsJsonAsync(new
                    {
                        error = "內部伺服器錯誤",
                        message = app.Environment.IsDevelopment() ? exception?.Message : "請稍後再試"
                    });
                });
            });

            // 確保資料庫與表存在
            var db = app.Services.GetRequiredService<DatabaseService>();
            try
            {
                db.EnsureDatabase();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "建立 SQLite 資料表失敗");
            }

            // 嘗試建立 SQL Server 表（若有設定 SQL_CONN）
            var sqlService = app.Services.GetService<SqlService>();
            if (sqlService != null)
            {
                try
                {
                    await sqlService.EnsureSqlServerTableAsync();
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "嘗試建立 SQL Server 表時發生錯誤，已繼續使用 SQLite。");
                }
            }

            // 設定 API 路由
            var apiService = app.Services.GetRequiredService<WebApiService>();
            apiService.ConfigureApi(app);

            // 健康檢查 API
            app.MapGet("/api/ping", () => Results.Ok(new { 
                status = "ok", 
                time = DateTime.UtcNow,
                version = typeof(WebHostRunner).Assembly.GetName().Version?.ToString() ?? "0.0.0"
            }));

            var port = Environment.GetEnvironmentVariable("PORT") ?? "5000";
            app.Urls.Add($"http://localhost:{port}");

            Log.Information($"啟動 Web 服務，監聽於 http://localhost:{port}");
            await app.RunAsync();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Web 服務啟動失敗");
            throw;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
