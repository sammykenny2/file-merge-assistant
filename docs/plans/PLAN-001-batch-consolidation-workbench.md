# PLAN-001：分批收斂工作台第一階段

## Metadata

| 欄位 | 值 |
|------|----|
| Status | In progress：新介面實作中（見 Progress Log 2026-10-23）；Phase 0 只剩 Task 0.5（ImageSharp 授權金鑰） |
| Owner | 待指定（建議在 Phase 1 前指派 Conductor，負責任務分派與 README 的 Current Status） |
| Assigned To | 待指定 |
| Reviewer | 產品負責人審閱計畫；各階段程式碼由非作者的 agent 或人審閱 |
| 建立日期 | 2026-10-23 |

## Related Spec

- [SPEC-001：多來源掃描與持續索引工作流程](../specs/SPEC-001-persistent-scan-workbench.md)（Draft）
- [ADR-001 至 ADR-013](../decisions/README.md)（001、004、005、006、008、009、010、013 已接受，012 部分接受，其餘 Proposed）
- [產品需求](../requirements.md)與 [GUI 流程 mock](../gui-flow-mock.md)

[plans 目錄的說明](./README.md)寫明：計畫應在規格核准後建立。目前 SPEC-001 是 Draft，12 份 ADR 都是 Proposed，所以這份計畫是草稿。每個階段開始前，都要先通過下面的決策閘門。

## Overview

目標是交付 FR-001 至 FR-010 的第一個完整流程，並依 ADR 草稿以「疊」為審閱單位。

- 每個階段做出可以驗證的成果，並一併做出它所支撐的 GUI 畫面；不把 GUI 留到最後一次做。
- 會修改使用者檔案的動作（複製、回收、還原）集中在 Phase 5。在那之前，程式只讀取與記錄。
- 決策理由寫在 ADR，這份計畫只引用、不重述。

## 決策閘門

每個階段開始前，下列 ADR 要先被接受（Accepted）。表中同時列出被否決時，計畫要怎麼調整。

- **Phase 0**：Task 0.4 需要 [ADR-012](../decisions/ADR-012-release-packaging.md) 中升級 .NET 10 的部分。否決時維持 .NET 8，並在風險中記錄「已結束支援」。修復測試基準不需要任何閘門。
- **Phase 1**：需要 [ADR-011](../decisions/ADR-011-acceptance-dataset-and-measurement.md)。若尚未決定，先用 ADR-011 提議的三級數量作為暫定值。
- **Phase 2**：
  - 需要 [ADR-008](../decisions/ADR-008-sqlite-local-index.md)、[ADR-010](../decisions/ADR-010-source-identity.md)、[ADR-012](../decisions/ADR-012-release-packaging.md) 的資料位置部分，以及依 Phase 1 驗證結果決定的 [ADR-009](../decisions/ADR-009-ui-technology.md)。
  - ADR-008 被否決：只替換儲存層，索引介面不變。
  - ADR-009 改用其他 UI 技術：Phase 2 起的 GUI 任務改用該技術，並加入移植現有 GUI 的任務。
  - ADR-010 被否決：退回以路徑辨認並請使用者確認。這不符合 SPEC-001，要同步修改規格。
- **Phase 3**：
  - 需要 [ADR-004](../decisions/ADR-004-candidate-first-scanning.md) 與 [ADR-005](../decisions/ADR-005-code-project-awareness.md)。
  - ADR-004 被否決：對全部檔案計算指紋，第一疊出現的時間會變長。
  - ADR-005 被否決：只保留排除規則。git 狀態可以單獨延後到 P1。
- **Phase 4**：
  - 需要 [ADR-001](../decisions/ADR-001-review-unit-stacks.md)、[ADR-006](../decisions/ADR-006-stack-reopen-and-membership.md)，而且 SPEC-001 已改寫為以「疊」為單位。改寫規格屬於文件工作，可以提前完成。
  - ADR-001 被否決：改做 SPEC-001 現行的檔案群組審閱，工作台與審閱畫面改為群組清單。
- **Phase 5**：
  - 需要 [ADR-002](../decisions/ADR-002-merge-into-keeper.md)、[ADR-003](../decisions/ADR-003-protected-source-writes.md)、[ADR-007](../decisions/ADR-007-recycle-area-policy.md)。
  - ADR-002 被否決：只提供「只保留正本」與「都保留」。
  - ADR-003 改成唯讀：受保護的正本不能用合併。
  - ADR-007 被否決：採用 ADR-007 列出的集中回收區替代方案。
- **Phase 7**：需要 ADR-012 的發佈格式與程式碼簽章決定。

## Implementation Phases

### Phase 0：測試基準與 .NET 10 升級

測試全部通過之前，其他階段都不開始。

- [x] Task 0.1：修正 [testing.md](../testing.md) 記錄的 CA1716（`IWatchService.Stop()`）與 CA1859（`PreviewStatisticsService.BuildDirectoryBreakdown()`），讓 `TreatWarningsAsErrors` 下也能建置。實作時另外發現並修正：GUI 的 CA1305，以及 ImageSharp 2.1.13 的 NuGet 稽核錯誤（升級到 4.1.3）。
- [x] Task 0.2：找出並修正 `EmptyDirectoryServiceTests.Cleanup_DryRun_Does_Not_Delete` 卡住的原因：dry run 時目錄不會被刪掉，迴圈永遠不會結束。這是產品程式的錯誤，不只是測試的問題。獨立審閱另外發現清理會跟著 junction 走到 root 之外刪除空目錄（改動前就存在），一併修正。
- [x] Task 0.3：在 testing.md 記錄全部通過的基準：SDK 版本、測試數與耗時。
- [x] Task 0.4：把所有專案的 `TargetFramework` 升級到 `net10.0-windows`，更新套件，重新跑完整測試。一併處理 testing.md 列出的間接相依漏洞（System.Text.Json、System.Net.Http、System.Text.RegularExpressions）：System.Text.Json 隨 Microsoft.Extensions.Hosting 升到 10.0.13 而移除；另外兩個是 .NET 10 SDK 略過框架已提供的套件，所以不再被引用。
- [ ] Task 0.5：向 Six Labors 申請 ImageSharp 授權金鑰，放在 repo 之外（環境變數或 `SixLaborsLicenseFile`）。沒有金鑰時 Release 建置會失敗；這是 Phase 7 的前置條件。

**Deliverables**：在 .NET 10 上全部通過的測試套件，並記錄在 testing.md。

### Phase 1：測試資料集、量測工具與 UI 技術驗證

- [ ] Task 1.1：建立 `scripts/windows/New-TestDataset.ps1`，依 [SCRIPT-STANDARDS](../SCRIPT-STANDARDS.md) 撰寫 comment-based help：
  - 用亂數種子產生小、中、大三級資料集。
  - 內容包括程式碼專案與 node_modules、改名的照片、「(1)」與「- 複製」複本、部分重疊的資料夾，以及模擬外接硬碟的來源。
- [ ] Task 1.2：產生器同時輸出 ground-truth 清單，記錄哪些資料夾與檔案彼此是複本、哪些只是部分重疊。Phase 4 用它計算疊偵測的 precision 與 recall。
- [ ] Task 1.3：經產品負責人同意後，建立只收集統計數字的 `scripts/windows/Get-FolderStatistics.ps1`，只收集數量、大小分布、深度與副檔名比例，用來校正產生器。
- [ ] Task 1.4：建立量測工具 `tests/Core.Benchmarks/`。它要記錄 ADR-011 列出的項目：
  - 各掃描階段的耗時與第一疊出現的時間。
  - 記憶體尖峰與索引檔大小。
  - 1% 檔案變動後的重新掃描時間。
  - 參考硬體的規格。
- [ ] Task 1.5：WPF 技術驗證。用中型資料集大小的模擬資料，做出「工作台」清單與「一疊」對照表，量測捲動、篩選與背景工作進行時的輸入回應。結果寫回 ADR-009，供產品負責人決定。
- [ ] Task 1.6：在 docs/research/ 記錄現有掃描流程的基準量測。

**Deliverables**：可重現的資料集與 ground truth、量測工具與基準數字，以及 ADR-009 的決策依據。

### Phase 2：資料位置、持久索引與來源身份

- [x] Task 2.1（2026-10-23 隨安裝檔提前完成）：新增 `AppDataPaths`，所有程式資料統一放到 `%LOCALAPPDATA%\FileMergeAssistant\`，取代快取、回收區、記錄檔與 GUI 中的相對路徑 `fma-results`（FR-010）。CLI 明確指定的 `--output` 維持原行為。
- [x] Task 2.2：決定直接寫 SQL 或使用 ORM，記錄在 ADR-008 與本計畫的 Technical Decisions。決定：直接寫 SQL（Microsoft.Data.Sqlite）。
- [x] Task 2.3（`src/Core/Index/IndexDatabase.cs`）：建立 SQLite 索引與第一版結構，包括來源、磁碟區、掃描工作、檔案與指紋（含演算法與版本）。啟用 WAL。
- [ ] Task 2.4（結構版本與遷移前備份已完成；舊 JSON 結果與快取不匯入）：**索引遷移**。資料庫記錄自己的結構版本，遷移依序執行，遷移前備份索引檔。依 ADR-008 的決定，匯入或捨棄舊的 JSON 結果與快取。
- [ ] Task 2.5（已讀取磁碟區序號並用來判斷離線；GUID、檔案 ID 與重新連接的比對規則尚未做）：**來源身份**。以 `IVolumeIdentityProvider` 包裝 Win32 呼叫，讀取磁碟區序號、磁碟區 GUID 與 NTFS 檔案 ID，並實作 ADR-010 的比對規則（自動對應、請使用者確認、不沿用）。
- [ ] Task 2.6：用檔案 ID 補上 Windows 上的 hardlink 偵測，改寫 `HardlinkDetector`（AC-06）。
- [ ] Task 2.7（畫面已接上真實索引；「指定新位置」尚未提供）：GUI。依 ADR-009 的決定建立新的視窗外殼：導覽列、常駐的索引狀態列，以及「來源」畫面（新增、設定、保護、離線狀態、指定新位置）。新外殼不沿用現有的 Scan、Results、Reports 分頁。

**Deliverables**：關閉程式後索引與來源仍保留；磁碟代號改變時能正確辨認來源。

### Phase 3：分段掃描與工作佇列

- [x] Task 3.1（`src/Core/Scanning/SourceScanner.cs`）：把掃描拆成探索、找候選、確認內容三段（ADR-004），取代 `FileScanner` 逐檔計算 hash 的流程。
- [x] Task 3.2：**檢查點**。每處理一批檔案就以一個交易提交，中斷時只重做尚未提交的那一批，不產生重複紀錄。
- [ ] Task 3.3（大小與時間不變時沿用指紋，指紋帶演算法前綴；恢復掃描後的重新驗證尚未做）：**指紋有效性**。大小與修改時間都沒變時沿用指紋；演算法或版本不同的指紋不混用；恢復掃描後重新驗證已保存紀錄的有效性。
- [ ] Task 3.4（`ScanWorker`：一次一個、排序、暫停、繼續、取消；設定快照尚未做）：工作佇列。同一時間只執行一個工作，其餘可以排序、暫停、繼續、取消與重試。依 SPEC-001 實作工作狀態，每個工作保存開始時的設定快照。
- [x] Task 3.5（預設清單可依來源調整的介面尚未做）：可重建資料夾（ADR-005）：內建預設清單，可以依來源調整，只記錄大小與檔案數。
- [ ] Task 3.6：如果 ADR-005 把 git 狀態列入第一階段，以唯讀方式讀取 HEAD、分支與未提交的變更。
- [x] Task 3.7：成功、略過與錯誤分開計數。只有完成相應範圍的完整檢查後，才能判定檔案已缺失。
- [x] Task 3.8（接上真實掃描工作）：GUI。首次開啟畫面，以及「掃描工作」區塊：顯示階段、進度、暫停中、中斷待處理與等待來源等狀態。

**Deliverables**：可中斷、可恢復的分段掃描，並量測第一疊出現的時間（AC-02 至 AC-05）。

### Phase 4：疊的偵測、對照與決定

這個階段只記錄決定，不改動任何檔案。

- [ ] Task 4.1（`StackDetector` 已實作並以合成資料驗證；用 ADR-011 資料集量測 precision 與 recall 尚未做）：定義資料夾疊的候選簽章與相似門檻，例如相對路徑加大小的集合重疊比例。用 ground truth 計算 precision 與 recall，結果寫回 ADR-001。
- [x] Task 4.2（程式碼專案裡的相同檔不會成為單檔疊）：單檔疊。內容指紋相同，而且不屬於任何資料夾疊的檔案。
- [x] Task 4.3（`StackReview`）：疊內對照模型：以正本為基準，依相對路徑分成相同、只在部分複本、內容不同三類。
- [ ] Task 4.4：正本建議規則，例如受保護、較完整、最近修改、git 狀態。每條建議都要附上可以顯示的理由。
- [ ] Task 4.5（決定已保存並在重新開啟後保留；重新開啟、提醒與「不屬於這疊」的長期記憶尚未做）：疊的決定與生命週期（ADR-006）：
  - 保存決定。
  - 出現新複本時重新開啟。
  - 「都保留」的疊有變動時提醒。
  - 記住「不屬於這疊」。
- [ ] Task 4.6：比較範圍（FR-005），以及 hardlink 不重複計算空間（AC-06）。相似照片與同名候選只保留位置，不併入流程（AC-11）。
- [x] Task 4.7（接上真實索引）：GUI。工作台（下一疊、待整理清單、位置組合篩選、依建議批次加入）與「一疊」畫面（逐份對照、設為正本、也保留、不屬於這疊、選擇做法）。

**Deliverables**：使用者可以逐疊審閱並記錄決定；疊偵測的 precision 與 recall 已量測（AC-01、AC-06、AC-11）。

### Phase 5：待執行計畫、執行與還原

這是唯一會修改使用者檔案的階段。這個階段的每個 PR 都必須由非作者審閱。

- [ ] Task 5.1（`PlanBuilder` 已產生動作與相依；固定疊版本與重新確認尚未做）：計畫模型。動作分為複製到正本、另存為、移入回收區，並記錄動作之間的相依（先複製並驗證，再回收）。每個計畫固定審閱時的疊版本；新加入的項目要重新確認。
- [ ] Task 5.2：**操作競態**，採「檢查後立即執行」：
  - 每個操作執行前，立即重新檢查磁碟區身份、檔案是否存在、大小、修改時間，以及判定依據所需的指紋。
  - 回收整個資料夾時，比對審閱時的檔案清單快照。
  - 不符就略過該項，記錄原因，並把該疊標為待更新。
  - 複製與驗證期間，以不允許其他程式寫入的方式開啟檔案，縮小檢查與執行之間的空窗。
- [ ] Task 5.3：保護檢查（ADR-003）擴大到所有會移除、移動或覆寫的動作，取代 `ResolutionService` 只檢查 Delete 的做法。
- [ ] Task 5.4：在 `IFileOperationService` 新增「複製並驗證」與「另存為」（ADR-002），包括命名規則與失敗處理。
- [ ] Task 5.5：**回收與還原衝突**。依 ADR-007 使用各磁碟區的回收區，集中保存紀錄：
  - 還原時原位置已有檔案，絕不覆寫，讓使用者選擇改名還原、還原到其他位置，或先不還原。
  - 還原合併時，複製進正本的檔移入回收區。
- [ ] Task 5.6：逐項記錄結果；只重試失敗的項目；可以取消；不宣稱整批已回滾。
- [ ] Task 5.7：分開計算四種空間數字：複本佔用、計畫處理量、回收區佔用、實際已釋放。
- [ ] Task 5.8（畫面已完成，目前接示範資料）：GUI。待執行畫面（重新檢查、有變動的項目、相依提醒、確認執行），以及執行結果與歷史畫面（重試、單項與整批還原、還原衝突）。

**Deliverables**：可以確認、追蹤與還原的整理流程（AC-07 至 AC-10）。

### Phase 6：長期使用、無障礙與整體驗收

- [ ] Task 6.1（畫面已完成，目前接示範資料）：GUI。「一週後回來」的變化摘要：硬碟重新接上、新複本、「都保留」的疊有變動、只剩一份的疊。
- [ ] Task 6.2：無障礙：鍵盤操作、焦點順序、螢幕閱讀器標籤；狀態不能只用顏色區分。
- [ ] Task 6.3：用大型資料集量測大量疊、長路徑與缺少預覽同時存在的情況（AC-12）。依基準與 ADR-011 訂出達標標準。
- [ ] Task 6.4：用中型資料集與一顆真實的 USB 硬碟，手動走完設計交接的五個任務腳本。

**Deliverables**：所有驗收情境通過，並有量測紀錄。

### Phase 7：發佈

- [x] Task 7.1（依 ADR-012 的 2026-10-23 決定改為 NSIS）：`scripts/windows/Build-Installer.ps1` 打包個人安裝的 NSIS 安裝檔，GUI 與 CLI 都內含 .NET 執行環境，CLI 是單一執行檔。0.1.0 目前只建置了 Debug 版。
- [ ] Task 7.2：依 ADR-012 的決定處理程式碼簽章。
- [ ] Task 7.3：在乾淨的 Windows 環境（例如 Windows Sandbox）做冒煙測試，確認安裝、啟動、資料寫入固定位置與解除安裝。
- [x] Task 7.4：撰寫 [deployment.md](../deployment.md)。README 的 Current Status 仍由 Conductor 或產品負責人更新。
- [x] Task 7.5：手動觸發的 release workflow（`.github/workflows/release.yml`），尚未執行；第一次執行前需要授權金鑰 secret，而且更新來源 repository 必須公開。
- [x] Task 7.6：程式內「檢查更新」（[ADR-013](../decisions/ADR-013-in-app-updates.md)）。

**Deliverables**：可以下載、直接啟動的第一個版本。

## Files to Create/Modify

以下檔名都是暫定，實作時可以調整。

| 檔案 | 動作 | 說明 |
|------|------|------|
| `Directory.Build.props`、`global.json`、各 `*.csproj` | Modify | 升級到 `net10.0-windows` 並更新套件（Phase 0 已完成） |
| `src/Core/Services/IWatchService.cs`、`WatchService.cs`、`PreviewStatisticsService.cs`、`src/Gui/ViewModels/ScannedFileViewModel.cs` | Modify | 修正 CA1716、CA1859、CA1305（Phase 0 已完成） |
| `src/Core/Services/EmptyDirectoryService.cs`、`tests/Core.Tests/Services/EmptyDirectoryServiceTests.cs` | Modify | 修正 dry run 無窮迴圈，並且不再跟隨 junction（Phase 0 已完成） |
| `src/Core/Core.csproj`、`.gitignore` | Modify | ImageSharp 升級到 4.1.3；Git 忽略 `sixlabors.lic`（Phase 0 已完成） |
| `scripts/windows/New-TestDataset.ps1` | Create | 資料集與 ground truth 產生器 |
| `scripts/windows/Get-FolderStatistics.ps1` | Create | 只收集統計數字的腳本（需同意） |
| `tests/Core.Benchmarks/` | Create | 量測工具 |
| `src/Core/Infrastructure/AppDataPaths.cs` | Create | 固定的程式資料位置 |
| `src/Core/Index/` | Create | SQLite 索引、結構版本與遷移 |
| `src/Core/Sources/` | Create | 來源模型、磁碟區身份與比對規則 |
| `src/Core/Services/HardlinkDetector.cs` | Modify | 以檔案 ID 偵測 Windows hardlink |
| `src/Core/Scanning/` | Create | 分段掃描、工作佇列與檢查點 |
| `src/Core/Services/FileScanner.cs` | Modify | 改為分段流程，或由新的掃描元件取代 |
| `src/Core/Stacks/` | Create | 疊的偵測、對照、正本建議與生命週期 |
| `src/Core/Plans/` | Create | 計畫模型、執行前檢查與執行器 |
| `src/Core/Services/IFileOperationService.cs`、`FileOperationService.cs` | Modify | 新增複製並驗證、另存為 |
| `src/Core/Services/RecycleBinService.cs` | Modify | 各磁碟區的回收區與集中紀錄 |
| `src/Core/Resolution/ResolutionService.cs` | Modify | 保護檢查涵蓋所有破壞性動作 |
| `src/Gui/` | Modify | 新外殼與各畫面；移除舊分頁 |
| `tests/Core.Tests/` | Create/Modify | 各階段的單元與整合測試 |
| `tests/Gui.Tests/` | Create | ViewModel 測試（目前沒有 GUI 測試專案） |
| `docs/testing.md`、`docs/deployment.md`、SPEC-001、相關 ADR | Modify | 記錄基準、量測結果與決定 |

## Technical Decisions

- 決策理由見各 ADR，這裡只列本計畫自己的安排。
- **索引存取**：直接寫 SQL（Microsoft.Data.Sqlite），不使用 ORM（Task 2.2）。疊不存進資料庫，每次由索引與「決定」資料表推導；疊的 id 由各份複本的身份算出，所以「不屬於這疊」的記憶存在決定裡。
- **CLI 範圍**：CLI 維持可建置、既有測試通過，但不新增功能；來源、疊與待執行等新流程只在 GUI 提供。這需要產品負責人確認。
- **不在第一階段流程內的既有程式**：同名分組（`SmartMergeService`）、`WatchService`、報告產生器與 Reports 畫面、感知雜湊與影片指紋（FR-011、FR-012、AC-11）。它們保持可建置，但不接入新的 GUI 流程。
- **可測試性**：Win32 呼叫、檔案系統與時間都放在介面後面，讓競態、離線與身份比對可以用測試替身重現。ViewModel 盡量不直接引用 WPF 型別。
- **工作流程**：程式碼階段依 AGENTS.md 走開分支、PR、獨立審閱；Phase 5 的審閱者不能是作者。純文件變更直接推到 main。
- **CI**：依 testing.md 維持停用，測試在本機執行；重新啟用要另外指示。

## Testing Strategy

- **Unit Tests**：
  - 疊內對照分類與正本建議。
  - 來源身份比對規則。
  - 計畫相依與「新項目需要重新確認」。
  - 保護檢查。
  - 空間計算。
  - 另存為命名。
- **Integration Tests**：
  - 用暫存資料庫測試遷移與交易。
  - 在暫存資料夾測試掃描中斷與恢復，以模擬故障中斷一批提交。
  - 複製、驗證、回收與還原。
  - 在檢查與執行之間插入檔案變動，確認該項被略過，而且沒有資料遺失。
  - 還原時原位置已有新檔，確認不會覆寫。
- **量測**：依 ADR-011，以三級資料集記錄：
  - 掃描各階段的耗時。
  - 記憶體尖峰。
  - 索引大小。
  - 第一疊出現的時間。
  - 重新掃描的時間。
  - 介面回應：技術驗證中的計時記錄，加上手動腳本。
  - 每次量測都記錄硬體規格。
- **Manual Testing**：用中型資料集與真實 USB 硬碟走完五個任務腳本，包括拔除硬碟、換磁碟代號、檔案正在被使用、還原衝突。

## Risks & Mitigations

| 風險 | 影響 | 緩解方式 |
|------|------|----------|
| 合併或回收造成資料遺失 | High | 先複製驗證再回收；執行前立即重新檢查；Phase 5 前不碰使用者檔案；強制非作者審閱 |
| 資料夾疊判錯 | High | 用 ground truth 量測；提供「不屬於這疊」；門檻可以調整 |
| ADR 被否決或大幅修改 | High | 決策閘門與各自的調整方式；以介面隔離儲存與 UI |
| WPF 處理大量清單效能不足 | Medium | Phase 1 先做技術驗證，再決定 ADR-009 |
| .NET 10 升級帶來相容性問題 | Medium | 放在 Phase 0，以全部通過的測試作為基準 |
| Win32 身份資訊的特殊情況（複製的硬碟、網路磁碟） | Medium | 以介面隔離並用測試替身覆蓋；用真實 USB 手動測試 |
| 範圍擴大（相似照片、背景監控、git 進階功能） | Medium | 明列為第一階段範圍外；git 狀態可以單獨延到 P1 |

## 需求與驗收對照

- FR-001 來源管理：Phase 2、3
- FR-002 工作佇列：Phase 3
- FR-003 持久索引：Phase 2
- FR-004 增量與恢復：Phase 3
- FR-005 比較範圍：Phase 4
- FR-006 檢視疊：Phase 4
- FR-007 保留建議：Phase 4
- FR-008 計畫與重新驗證：Phase 5
- FR-009 紀錄與還原：Phase 5
- FR-010 直接啟動與資料位置：Phase 2、7
- AC-01：Phase 4（跨來源的疊需要索引與疊偵測都完成）
- AC-02、AC-03、AC-05：Phase 3
- AC-04：Phase 2、3
- AC-06：Phase 2、4
- AC-07、AC-08、AC-09、AC-10：Phase 5
- AC-11：Phase 4
- AC-12：Phase 6

## 目前的已知限制（2026-10-23，真實索引第一版）

- 單檔疊沒有份數上限：200 個相同的散落檔會變成 200 欄的對照表。
- 大小相同但計算指紋失敗的檔，會一直沒有指紋，在對照表裡顯示為「內容不同」。
- 疊的 id 由各份複本算出；掃描找到新的複本後 id 會變，原本的決定留在資料庫裡但不再套用（ADR-006 的重新開啟尚未做）。
- 判斷來源是否在線時會在介面執行緒呼叫 `Directory.Exists`；網路磁碟斷線時可能讓介面停頓。
- 「指定新位置」、磁碟區 GUID 與重新連接的比對、git 狀態、ADR-011 的 precision 與 recall 量測都還沒做。

## Progress Log

| 日期 | 更新 |
|------|------|
| 2026-10-23 | 建立草稿；等待 ADR 決定後再開始 Phase 0 以外的工作 |
| 2026-10-23 | 完成 Task 0.1 至 0.3：solution 建置通過，測試全部通過；新增 Task 0.5（ImageSharp 授權金鑰）。依獨立審閱修正 junction 問題後，測試增為 149 個 |
| 2026-10-23 | 產品負責人指示升級 .NET 10；完成 Task 0.4：`net10.0-windows` 上建置與 149 個測試通過，沒有已知漏洞的套件 |
| 2026-10-23 | 產品負責人要求 NSIS 安裝檔、release workflow 與檢查更新：提前完成 Task 2.1，完成 Task 7.1、7.4、7.5、7.6；0.1.0 先以 Debug 設定打包 |
| 2026-10-23 | 真實索引（只讀）：SQLite 索引、分段掃描與工作佇列、疊偵測、`IndexedWorkbenchStore`；預設接真實索引，`--demo` 接示範資料；待執行可重新檢查，但「執行」顯示「執行功能在下一版加入」。移除舊的 Scan、Results、Reports 畫面（CLI 保留）。測試共 307 個 |
| 2026-10-23 | ADR-008、ADR-010 改為 Accepted；Task 2.2 決定直接寫 SQL。下一批：真實的索引、分段掃描與疊偵測（只讀），執行與還原仍待 ADR-002、003、007 |
| 2026-10-23 | 新介面第一批：新視窗外殼與 7 個畫面（加入來源、來源與掃描工作、工作台、一疊、待執行、執行結果與還原、自上次以來）接上 `IWorkbenchStore` 與 `DemoWorkbenchStore`。Core 新增 `Sources/`、`Stacks/`（`StackReview`、`StackTriage`）、`Plans/`（`PlanBuilder`）與 `Workbench/`。新增 `tests/Gui.Tests`，逐一繪製每個畫面並檢查沒有繫結錯誤。測試共 272 個。舊的 Scan、Results、Reports 暫放在「舊版掃描」，等真實掃描接上後移除 |
| 2026-10-23 | 產品負責人要求依 GUI 流程 mock 實作新介面。ADR-001、004、005、006、009 改為 Accepted；ADR-002、003、007、008、010 在會用到它們的 PR 前再確認。順序改為先做畫面：新外殼與 7 個畫面先接上 `IWorkbenchStore` 的示範資料，接著以真實的分段掃描、索引與疊偵測取代示範資料，最後才是會改動檔案的執行與還原 |

---

*建立日期：2026-10-23*
*負責人：待指定*
