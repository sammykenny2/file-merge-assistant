# ADR-008：本機索引採用 SQLite（D-01）

## 狀態

Accepted（2026-10-23：產品負責人要求依 GUI 流程 mock 實作新介面，採用這份提議）

## 背景

- 現行 [`JsonScanResultStore`](../../src/Core/Services/JsonScanResultStore.cs) 把整份掃描結果寫成一個 JSON 檔。`File.Create` 會先清空舊檔再寫入，寫到一半中斷時，舊結果與新結果都可能遺失。
- 現行 [`ScanCacheService`](../../src/Core/Services/ScanCacheService.cs) 依「掃描路徑加演算法」各存一個 JSON 檔，位於相對於工作目錄的 `fma-results/.cache`。大小與修改時間相同時，沿用快取的 hash。
- [產品需求](../requirements.md) 與 [SPEC-001](../specs/SPEC-001-persistent-scan-workbench.md) 要求的資料責任，JSON 整檔讀寫難以支撐：
  - 保存所有已掃描檔案，不只重複檔（FR-003）。
  - 分批保存進度，中斷後可以接著做（FR-004）。
  - 跨來源查詢與比較（FR-005）。
  - 逐項的操作歷史與還原（FR-009）。
- 依 [ADR-004](./ADR-004-candidate-first-scanning.md) 與 [ADR-006](./ADR-006-stack-reopen-and-membership.md)，索引還要保存掃描階段的檢查點、疊的決定歷史與排除關係。
- 目標規模是數萬到數十萬筆檔案紀錄，並且掃描寫入時，介面也要能同時查詢。

## 決策（提議）

- 本機索引使用 SQLite，透過 Microsoft.Data.Sqlite 存取，直接寫 SQL，不使用 ORM（PLAN-001 Task 2.2，2026-10-23）：資料表少、查詢以批次寫入與彙總為主，直接寫 SQL 比較容易控制交易與效能。
- 啟用 WAL 模式，讓掃描寫入時，介面仍可以讀取。依 SPEC-001，一次只有一個掃描工作寫入。
- 每處理一批檔案就以一個交易提交；交易即為檢查點，中斷時只會重做尚未提交的那一批。
- 資料庫記錄自己的結構版本，升級時依序遷移；遷移前先備份索引檔。
- 索引檔放在使用者的應用程式資料位置（例如 `%LOCALAPPDATA%\FileMergeAssistant\`），不隨工作目錄改變，符合 FR-010（另見 [ADR-012](./ADR-012-release-packaging.md)）。
- 需要保存的資料包括來源、掃描工作、檔案、指紋（含演算法與版本）、疊與決定、待執行計畫、操作紀錄與回收項目。這是資料責任的清單，不是最終的資料表設計。

## 影響

### 正面

- 有交易保護，中斷不會破壞已提交的索引。
- 每次只寫入變動的部分，不必整檔重寫。
- 可以用查詢做候選分組、跨來源比對與歷史檢索。

### 負面

- 多了一個原生相依：Microsoft.Data.Sqlite 透過 SQLitePCLRaw 帶入 SQLite 原生程式庫，發佈時要一併包含。
- 需要維護結構版本與遷移。
- 現有的 JSON 結果與快取要決定是否匯入，或直接捨棄。

### 中性

- 同一時間只有一個寫入者的限制，與 SPEC-001「一次執行一個掃描工作」相符。

## 考慮過的替代方案

### 替代方案一：繼續使用 JSON 檔（現況）

- **優點**：不需要新的相依，檔案容易直接查看。
- **缺點**：整檔讀寫，沒有交易；中斷時容易損壞；查詢要先把整份資料載入記憶體。
- **不採用的原因**：無法支撐持久索引、分批保存與跨來源查詢。

### 替代方案二：LiteDB 等純 .NET 的嵌入式文件資料庫

- **優點**：不需要原生程式庫，直接存取物件。
- **缺點**：工具與社群資源較少；依大小分組、跨表比對之類的查詢較不直覺。
- **不採用的原因**：候選分組與歷史查詢以關聯式模型表達較自然。

### 替代方案三：需要另外安裝的資料庫伺服器

- **優點**：功能完整。
- **缺點**：使用者要另外安裝與維護，與「直接啟動的本機工具」相違背。
- **不採用的原因**：部署與維護成本過高。

## 待決定事項

- 是否採用 SQLite。
- 現有的 JSON 掃描結果與快取要匯入，還是捨棄。
- 索引大小與查詢時間的量測目標（依 [ADR-011](./ADR-011-acceptance-dataset-and-measurement.md)）。

## 參考

- [產品需求：D-01、FR-003、FR-004、FR-005、FR-009、FR-010](../requirements.md)
- [SPEC-001 工作流程規格](../specs/SPEC-001-persistent-scan-workbench.md)
- [JsonScanResultStore.cs](../../src/Core/Services/JsonScanResultStore.cs)
- [ScanCacheService.cs](../../src/Core/Services/ScanCacheService.cs)

---

*提出日期：2026-10-23*
*決策日期：2026-10-23*
*決策者：產品負責人*
