# ADR-012：發佈格式與資料位置（D-06 之二）

## 狀態

Proposed（草稿，待產品負責人決定）

## 背景

- 目前沒有任何發佈設定：[deployment.md](../deployment.md) 仍是範本，專案中沒有發佈設定檔，CI 也暫停中。
- FR-010 要求：Windows 使用者可以直接啟動 GUI，資料位置不隨啟動時的工作目錄改變。現在多處使用相對路徑 `fma-results`：
  - [`ScanCacheService`](../../src/Core/Services/ScanCacheService.cs) 的快取。
  - [`RecycleBinService`](../../src/Core/Services/RecycleBinService.cs) 的回收區。
  - `LoggingSetup` 的記錄檔。
  - `ScanOptions` 的預設輸出目錄。
  - GUI 的 `ScanViewModel`。
- [產品需求](../requirements.md) 的 D-06 列出三種候選：免安裝、NSIS 安裝檔、MSIX。
- 所有專案都以 .NET 8 為目標。依 [.NET Blog 的公告](https://devblogs.microsoft.com/dotnet/dotnet-8-9-end-of-support/)，.NET 8 在 2026-11-10 結束支援，之後不再有安全性更新；.NET 10 是 LTS 版本，支援到 2028 年 11 月。

## 決策（提議）

- 第一批版本以免安裝的 zip 發佈：
  - 內含 .NET 執行環境的 win-x64 版本，使用者不必另外安裝 .NET。
  - 不需要系統管理員權限。
  - 是否打包成單一執行檔，實作時再驗證。
- 所有程式資料統一放在 `%LOCALAPPDATA%\FileMergeAssistant\`，包括索引、記錄檔與操作歷史。回收內容依 [ADR-007](./ADR-007-recycle-area-policy.md) 放在各磁碟區。程式資料不放在執行檔旁邊，換版本時資料也會保留。
- 正式發佈前，先升級到 .NET 10 LTS。.NET 8 在 2026-11-10 之後不再有安全性更新，發佈時不應仍停留在已結束支援的版本。
- 公開發佈前，決定是否進行程式碼簽章。未簽章的執行檔會被 SmartScreen 警告。
- 需要開始功能表捷徑、解除安裝或自動更新時，再評估 NSIS 或 MSIX。

## 影響

### 正面

- 發佈最簡單，不需要安裝程式，也不需要系統管理員權限。
- 資料位置固定，修正目前依工作目錄存放資料的問題。
- 發佈時仍在 .NET 的支援期內。

### 負面

- 沒有開始功能表捷徑與解除安裝，更新要手動下載。
- 內含執行環境，下載檔較大。
- 升級 .NET 10 需要額外的相容性測試，包括現有測試的既有問題（見 [testing.md](../testing.md)）。

### 中性

- CLI 是否放在同一個發佈包內，可以另外決定。

## 考慮過的替代方案

### 替代方案一：一開始就提供 NSIS 安裝檔

- **優點**：有開始功能表捷徑與解除安裝。
- **缺點**：要維護安裝腳本；預設安裝到 Program Files 時需要系統管理員權限。
- **不採用的原因**：第一批版本還不需要，之後再加。

### 替代方案二：一開始就用 MSIX

- **優點**：安裝與更新由系統管理，解除安裝很乾淨。
- **缺點**：一定要簽章；封裝後寫入 AppData 的行為，以及解除安裝時資料是否保留，都需要驗證。
- **不採用的原因**：驗證成本較高，延後評估。

### 替代方案三：不含執行環境的版本

- **優點**：下載檔小。
- **缺點**：使用者要自己安裝正確版本的 .NET 桌面執行環境。
- **不採用的原因**：不符合「直接啟動」。

## 待決定事項

- 發佈前升級到 .NET 10 的時程。
- 是否進行程式碼簽章，以及簽章方式。
- CLI 是否與 GUI 一起發佈。
- 什麼時候加入安裝檔或自動更新。

## 參考

- [產品需求：D-06、FR-010](../requirements.md)
- [deployment.md](../deployment.md)
- [testing.md](../testing.md)
- [RecycleBinService.cs](../../src/Core/Services/RecycleBinService.cs)
- [ScanCacheService.cs](../../src/Core/Services/ScanCacheService.cs)

---

*提出日期：2026-10-23*
*決策日期：待定*
*決策者：產品負責人*
