# MarriageStatistics - 婚姻統計資料處理

# MarriageStatistics

婚姻統計資料處理與視覺化小型服務。

此專案目標：
- 解析政府或 CSV 原始資料（CSV/JSON），產生統計與 summary
- 本地儲存原始 API JSON（SQLite）並提供簡單 Web UI 與 API
- 支援快取（Redis）與可降級到 SQLite 的 SQL Server 支援

目前主要功能
- CLI：支援 `--web`（啟動 web UI）與 `--fetch`（自動抓取）
- Web UI：位於 `wwwroot/`，包含 dashboard 與圖表（Chart.js / ECharts）
- API：
	- GET `/api/ping` 健康檢查
	- GET `/api/entries` 抓取紀錄列表
	- GET `/api/entry/{id}` 單筆紀錄詳細
	- GET `/api/stats` 儀表板統計（不包含抓取次數）
	- GET `/api/download/{id}` 下載原始 JSON
- 快取：`Services/CacheService.cs`，優先使用 Redis（環境變數 `REDIS_CONNECTION`），失敗時降級
- 資料庫：預設 SQLite（`App_Data/marriage.db`），可透過 `SQL_CONN` 環境變數連到 SQL Server（會自動降級為 SQLite 若失敗）

快速開始
1. 開發環境（PowerShell）

```powershell
cd "c:\Users\jackyjuang\Desktop\C#\work1_Marriage statistics\MarriageStatistics"
dotnet restore
dotnet build
# 啟動 Web UI（預設 http://localhost:5000，可用 PORT 環境變數改變）
dotnet run -- --web
```

2. 抓取機制

- BackgroundFetcherService 會在 Web 模式下週期性抓取資料（預設每 10 分鐘），並將原始 JSON 覆寫到 `App_Data/{FETCH_FILENAME}`，同時更新 SQLite。手動 POST `/api/fetch` 介面已移除。

	你可以透過下列環境變數調整行為：
	- `FETCH_URL` = 抓取的 API URL（預設為內建 Hsinchu 範例）
	- `FETCH_FILENAME` = 寫入的固定檔名（預設 `api_fetch_20251107_081759.json`）
	- `FETCH_INTERVAL_MINUTES` = 抓取間隔（最小強制為 10 分鐘）

環境變數（常用）
- `PORT` = Web 服務監聽埠（預設 5000）
- `REDIS_CONNECTION` = Redis 連線字串（預設 localhost:6379）
- `SQL_CONN` = SQL Server 連線字串（若設定，會優先嘗試寫入 SQL Server，失敗時降級至 SQLite）

日誌與監控
- 使用 Serilog 輸出到 Console 與 `logs/app-*.log`（每日輪替，保留 7 天）

除錯/診斷（Debug）
- 若想啟用更詳細的除錯日誌，可設定環境變數 `DEBUG=1` 或將 `ASPNETCORE_ENVIRONMENT=Development`。
	在除錯模式下，Serilog 會以 Debug 等級輸出更多細節（包含外部呼叫錯誤、解析錯誤等）。

健康檢查與診斷端點
- GET `/api/health`：回傳資料庫統計（已抓取次數、最後更新時間）、快取狀態與目前時間。
- GET `/api/cache/status`：回傳快取狀態（Redis 連線、記憶體快取鍵樣本等）。

App_Data 路徑說明
- 執行行為在不同模式（CLI 或 Web）下會使用相同的 `App_Data` 預設位置：
	- 當啟動時若能在上層目錄找到 `MarriageStatistics.csproj`，程式會以該專案目錄下的 `App_Data` 為優先路徑（這讓 CLI 抓取與 Web 服務讀取一致）。
	- 若找不到專案檔，會退回到 `AppContext.BaseDirectory\App_Data`（例如發行後的執行目錄）。

開發建議與注意事項
- 若要升級為長期服務，建議：
	- 引入 EF Core（利於 schema 與 migration）
	- 將敏感設定（SQL_CONN、REDIS_CONNECTION）放入安全的機密管理（例如 Azure Key Vault）
	- 增加單元測試與 CI（xUnit / GitHub Actions）
- 專案目前保留部分範例檔案（`ConsoleApp/` 等），可在確認不需要後移除以減少干擾。

清理與下一步（提議）
- 我可以為你：
	1. 產生一份完整的專案分析報告（包含相依性、風險點與改進建議）
	2. 移除或合併不必要的範例資料夾與檔案
	3. 補上單元測試樣本與 CI 設定

若你同意，我會先產生專案分析報告，然後根據報告逐步執行清理與測試。謝謝！
