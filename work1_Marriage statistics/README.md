# MarriageStatistics

系統簡介
--
本專案為以 .NET 9.0 開發的婚姻統計資料讀取、彙整與視覺化服務。它會從本地 CSV/JSON（`App_Data/`）或透過背景抓取 API 的原始資料產出簡潔的摘要（`*.summary.json`），並透過前端互動圖表（趨勢、性別、區域、國籍分佈）呈現分析結果，方便快速檢視各區域與歷年趨勢。

功能特色
--
- 資料彙整：支援從 CSV、JSON 檔與 API 原始資料產生 summary 檔案。
- 互動圖表：歷年趨勢（折線）、性別比例（甜甜圈）、區域（餅圖）與國籍分佈（長條）等視覺化展示。
- 明細檢視：獨立的 `details.html` 顯示來源條目與原始 JSON，支援下載。
- 向後相容：伺服器端可處理部分舊格式摘要（例如以年度陣列儲存的 `MarriageCount[]`）並轉換為目前模型。

技術架構
--
- 後端：ASP.NET Core Minimal API (.NET 9.0)
	- 主要服務：`DatabaseService`、`JsonProcessor`、`ApiFetcher`、`WebApiService`。
- 前端：靜態頁面（`wwwroot/`），使用 Chart.js 與 ECharts 呈現圖表，搭配簡單 CSS/Bootstrap。
- 資料：本地 `App_Data/`（CSV/JSON）與 `summaries/`（摘要檔）。
- 日誌：Serilog，輸出於 `logs/`。

專案說明
--
此方案包含一個 Web 應用（主服務）與一個工具範例（若存在 `ConsoleApp/`）：

1. Web 應用（MarriageStatistics）
	 - 提供 REST API：
		 - GET /api/chart-data — 取得供前端圖表使用的 `ChartData`。
		 - GET /api/entries — 列出已儲存的原始條目（ApiData）。
		 - GET /api/entry/{id} — 取得指定條目原始 JSON。
		 - GET /api/download/{id} — 下載原始 JSON。
	 - 主要程式位於 `Services/`、`Models/`、`wwwroot/`。

2. ConsoleApp（可選）
	 - 用於快速解析或測試資料處理流程（若專案包含）。

資料來源
--
- 本地範例檔案：`App_Data/`（包含 CSV 與 JSON 範例）。
- 可透過 `ApiFetcher` 下載公開 API 資料並寫入資料庫與摘要檔。

開發環境需求
--
- .NET 9.0 SDK
- Windows PowerShell（範例指令以 PowerShell 撰寫）
- 建議使用 Visual Studio 或 VS Code + C# 擴充

建置與執行（PowerShell 範例）
--
在專案根目錄（含 `MarriageStatistics.csproj`）執行：

建置（Debug）：
```powershell
dotnet build "MarriageStatistics.csproj" -c Debug
```

啟動（開發模式）：
```powershell
dotnet run --project . -- --web
```

測試 API：
```powershell
Invoke-RestMethod http://localhost:5000/api/chart-data
```

日誌
--
- 執行期間日誌檔位置：`logs/app-YYYYMMDD.log`。

已知限制與建議改進
--
- 年齡層分布：若 summary 檔未包含年齡桶（age bucket），前端會顯示「無年齡資料」。若需年齡分布，應在 `JsonProcessor` 中將原始欄位聚合為年齡桶並寫入 `MarriageStats`。
- 國籍欄位解析目前以屬性名稱包含「外國籍/國籍」為判斷規則，並嘗試擷取欄位名稱的最後段作為國別；若欄位命名不一致，可提供 mapping 或擴充解析規則以提升正確率。
- 建議新增單元測試（`xUnit`）覆蓋 `JsonProcessor`、`DatabaseService` 的轉換與聚合邏輯，並加入 CI 流程。

如何貢獻
--
- 歡迎透過 Git 分支與 Pull Request 提交修正或新功能。
- 若發現資料解析問題，請附上對應 `App_Data/summaries/*.summary.json` 範例與 `logs/app-*.log` 的錯誤片段，以便重現問題。

聯絡與授權
--
- 本專案為個人練習與教學用途。請在 PR 或 Issue 中留下聯絡資訊或回報。


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
