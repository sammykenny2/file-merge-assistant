# PLAN-002：雙分析引擎與 Rust 核心介接

## Metadata

| 欄位 | 值 |
|------|----|
| Status | In progress；2026-10-24 依產品負責人指示分片實作並逐段 commit，先不 push |
| Owner | 待指定 |
| Assigned To | 待指定 |
| Reviewer | 產品負責人；程式碼與原生邊界由非作者獨立審閱 |
| 建立與更新日期 | 2026-10-24 |

## Related Spec

2026-10-24 後續指示：產品負責人已要求 push、公開 repo、恢復 CI 並發佈
0.3.0 Preview；另明確同意先不使用 ImageSharp，重點驗證 DLL。原先先不 push／
CI 停用的限制已由這次指示取代。依 [ADR-016](../decisions/ADR-016-public-native-preview.md)
省略 legacy 媒體算法並補足可追溯的授權條款；這不代表 G5／G6 實機驗收通過。

- [SPEC-002：可切換的檔案分析引擎](../specs/SPEC-002-pluggable-analysis-engines.md)（Approved for staged implementation）
- [ADR-015：雙引擎決策](../decisions/ADR-015-pluggable-analysis-engines.md)（Accepted，依本計畫實作中）
- [SPEC-001](../specs/SPEC-001-persistent-scan-workbench.md)、[PLAN-001](./PLAN-001-batch-consolidation-workbench.md)

## Overview

先確認契約能適配上游及既有索引，再抽出有限的 C# 引擎與 Rust 包裝，最後接入 GUI／CLI、驗收及部署。各階段保留現有可用路徑，不在一次變更中同時更換 UI、算法、索引與執行器。文件提交不包含程式實作、預設值切換、平台支援宣告或 CI 啟用。

## Implementation Phases

### Phase 0：基準與適配驗證

- [x] Task 0.1：記錄實作開始時的 File Workbench／上游 commit、.NET／Rust 版本與測試結果；依 testing.md 跑現有 Core 與 GUI 測試，不沿用本文件日期的測試數作為新結果。
- [x] Task 0.2：建立小型、可重建且有已知答案的資料，包含唯一檔、同大小不同內容、空檔、改名及跨來源副本。隔離索引及引擎快取，不用個人檔案當測試樣本。
- [x] Task 0.3：以調查基準 `eb8b91d` 的完整 commit 為首輪候選，確認可建置的依賴來源與 lockfile。正式建置不得引用本機 `../../fork/czkawka-fork/`。
- [x] Task 0.4：驗證上游的路徑／檔案輸入、排除條件、硬連結及最小檔案預設值；確認能回傳所有候選的逐檔證據與終態、完整範圍、進度及取消，量測是否重複讀取。
- [x] Task 0.5：盤點 `files.hash`、`StackDetector`、疊摘要、待執行計畫及 `PlanExecutor.CopyVerified` 的算法相依；提出可保存 legacy 紀錄與獨立執行驗證的遷移方式。
- [x] Task 0.6：把實際支援矩陣、缺口、建置功能、授權與量測寫入 research。需要上游／fork API 擴充時先列出最小變更，不在外部 repo 順便修改。

**Deliverables**：可重現的適配報告、已知答案資料、候選 API 契約及索引遷移設計。

**Gate G0**：逐檔覆蓋、完整索引、候選優先與舊資料相容有可行證據後，固定 SPEC 的介面再進 Phase 1。只有重複群組、沒有契約所需證據時不得直接取代現有掃描；調整 API／包裝方案，不以省略資料需求通過閘門。

2026-10-24：G0 的逐檔 hash 適配已通過，資料與測試證據見 [research](../research/2026-10-24-pluggable-analysis-engines.md#phase-0-適配結果2026-10-24)。不呼叫上游資料夾走訪，直接使用公開 `hash_calculation`；核心契約在 Phase 1 依此固定，FFI job 與遷移仍是後續交付。

### Phase 1：共用契約與有限的 C# 引擎

- [x] Task 1.1：在 Core 建立分析介面、descriptor、能力／可用性／成熟度、request／result／progress 與 registry，保持與 UI 框架無關。
- [x] Task 1.2：將 `SourceScanner` 的探索、候選選取與索引提交責任保留在 Core，抽出內容分析為 C# 引擎；先不改既有 hash 語意或切換預設值。
- [x] Task 1.3：C# 只宣告已實作能力；不把既有影像／影片元件的存在當作工作台已支援。以已知答案做精確去重契約測試，補足排除、變動與部分失敗案例。
- [x] Task 1.4：定義單一結果驗證與提交入口，核對請求 ID、範圍、檔案 ID、觀察版本與世代；拒絕未知、變動或越界項目。

**Deliverables**：共用契約、C# 適配與保持原有行為的測試證據。

**Gate G1**：既有 Core／GUI 測試與 AC-E02、AC-E03、AC-E09 通過，索引仍含不重複檔；獨立審閱同意責任邊界。

2026-10-24：G1 通過。Core 416／GUI 16 全通過，獨立審閱確認候選委派及資料邊界。新增 malformed／unknown／duplicate／不同算法結果、取消、部分失敗及 fake Changed metadata 回歸；Core 重新觀察檔案，不採信引擎提供的新 metadata。此階段只接 C# 精確分析，原生與持久任務尚未接入。

### Phase 2：Rust C ABI 與 .NET 適配

- [x] Task 2.1：在 File Workbench 的 `native/file-workbench-czkawka-ffi/` 建立獨立包裝 crate，固定上游完整 commit、Rust 工具鏈、Cargo.lock 與最小建置功能；先產出 Windows x64 `cdylib`。
- [x] Task 2.2：實作版本／能力查詢、job handle、開始、輪詢、批次、取消及釋放協定。記錄字串編碼、結構大小／版本、長度上限與記憶體所有權。
- [x] Task 2.3：包裝只呼叫讀取／分析能力；明確關閉來源變更操作，快取及暫存只進 File Workbench 分配目錄。先驗證精確去重，不一次包完上游工具。
- [x] Task 2.4：在 Core 建立以 SafeHandle／P/Invoke 為基礎的 Czkawka 適配，按受控絕對路徑載入，先檢查版本／ABI／架構，再執行工作。
- [x] Task 2.5：加入原生邊界測試：錯誤輸入、缺 DLL、錯誤架構、ABI 不符、重複取消、完成後釋放、執行中關閉、批次背壓、反覆執行的記憶體及來源不變性。
- [x] Task 2.6：可恢復錯誤不跨 ABI 展開；程序級崩潰案例在隔離的測試程序內驗證下次啟動的恢復，不能靠 catch 宣稱有程序隔離。

**Deliverables**：固定版本的原生產物、能力資訊、C# 適配及可重現 ABI 測試。

**Gate G2**：Rust 各自通過已知答案與來源不變測試，AC-E07 通過；獨立審閱原生記憶體、生命週期與取消流程。失敗時仍保留 C# 原路徑，不提升原生預設值。

2026-10-24：G2 的 Windows x64 interop 範圍通過獨立審閱：Rust 10、Core 421、GUI 16 通過，原生整合 5 項零略過。短緩衝區保留事件、輸出長度上限、背壓、8 個實際 worker 上限、取消停止確認與上傳途中取消皆有回歸。協定見 [ABI.md](../../native/file-workbench-czkawka-ffi/ABI.md)，重現指令為 `scripts/windows/Build-NativeAnalysis.ps1 -Test`。Task 2.6 的程序中斷持久恢復留在 Phase 3 完成，不能用本次 interop 測試代替。

### Phase 3：持久任務、索引與舊紀錄遷移

- [x] Task 3.1：以具備備份及舊版 fixture 的 schema migration 加入引擎／參數快照、結果來源與世代；已執行歷史與回收紀錄完整保留。
- [x] Task 3.2：依 G0 設計處理舊 `xxh128:`、新算法與逐檔證據；驗證疊摘要、決定重開與計畫重新驗證，不把新 hash 塞進舊語意。
- [x] Task 3.3：為排隊、暫停、中斷、等待來源、取消及失敗記錄固定引擎。無可用原版本的任務保持可診斷，建立新任務才能改引擎。
- [x] Task 3.4：完整世代才替代既有結果；取消、略過或失敗的分析不刪除未回報檔案。恢復採重做未提交範圍，檢查原生快取是否可安全沿用。
- [x] Task 3.5：使用遷移前的待執行計畫與歷史，測試切換、缺少 Rust DLL、執行、失敗重試及還原；執行驗證不依賴目前預設分析引擎。

**Deliverables**：可追溯任務、版本化證據、遷移測試及舊計畫／還原相容性。

**Gate G3**：AC-E04、AC-E05、AC-E06 通過，完整索引與回收記錄無遺失；不以清空資料庫作為升級方式。

2026-10-24：G3 的 Windows x64 範圍通過獨立審閱。Core 444／GUI 16 零略過；隔離子程序先以實際 DLL 保存 200 筆原生證據，再於第二批原生上傳期間被終止。重新開啟保留 450 筆原索引、快照與檢查點；缺原引擎時等待，原版本恢復後以同 Job ID 完成第二代。另一個全新程序在原生目錄不存在、還原前後原生模組皆為 0 的條件下讀歷史並還原成功。此測試證明程序損失後恢復，不代表 catch 能攔截 native abort 或已採程序隔離架構。Task 2.6 同時完成。

### Phase 4：GUI／CLI 接線與相似圖片

- [x] Task 4.1：現有 WPF 透過共用 registry 提供引擎與能力選擇、成熟度、不可用原因及任務快照顯示，不把 P/Invoke 細節散入 ViewModel。
- [x] Task 4.2：CLI 接同一個選擇與驗證入口；明確新增選項及結果版本資訊，保留既有格式及預設操作語意，需變更時另記相容政策。
- [x] Task 4.3：相同資料分開執行兩個引擎的唯讀分析，輸出可比較的已知答案、時間、I/O、記憶體與結果差異；不自動合併兩份操作計畫。
- [x] Task 4.4：精確去重契約穩定後接 Rust 相似圖片，使用標註的縮放／重壓縮／旋轉／裁切及不同圖片資料量測誤判與漏判。各變形是否支援以結果為準。
- [x] Task 4.5：相似候選提供比較與人工審閱入口，分數有算法與閾值定義，不進入「精確內容相同」的自動處理路徑。

**Deliverables**：可操作的兩個入口、引擎比較報告與單獨驗證的相似圖片能力。

**Gate G4**：AC-E01、AC-E08、AC-E09 通過；GUI 的綁定、語言及取消測試通過；CLI 無非預期格式或操作變更。未通過相似圖片驗收不影響已驗證的精確去重。

2026-10-24：Windows x64 Debug 的 G4 整合驗證通過。Core 493／GUI 25／CLI 12 零略過；獨立審閱 Core archive 8／GUI review 5／CLI 12 通過，含實際 DLL。WPF 提供並排候選、保存判斷及缺引擎重開；CLI 新增 `similar-images`，既有介面與預設保留。縮圖兩軸限制、損壞紀錄、過期儲存及離頁取消有回歸；裁切漏判與資源限制明確記錄。此 gate 不代表大量個人照片準確率、Release／安裝套件或非 Windows 支援通過；這些仍屬 Phase 5／6。

### Phase 5：Windows 驗收、打包與預設引擎

- [ ] Task 5.1：按 ADR-011 的決策或記錄的暫定規模，在 Windows 量測冷／暖快取、多來源、新增 1% 檔案、USB／離線、連結與長路徑。記錄硬體及缺測項目，不宣稱未量測的速度。
- [ ] Task 5.2：乾淨環境驗證原生產物、NSIS 安裝／更新／卸載、架構及 ABI 檢查、無 Rust 工具鏈執行；保留可選引擎不可用的診斷。按實際相依處理授權，ImageSharp 發佈條件仍依 ADR-012。
- [ ] Task 5.3：按能力與 Windows 版本記錄驗收成熟度；產品負責人依報告決定是否把原生能力設為新任務預設。C# 未驗證能力仍為實驗性，無隱藏 fallback。
- [x] Task 5.4：更新 setup／testing／deployment／release notes，記錄已交付範圍、回退方式與仍未支援能力；CI 不因本階段自動恢復。外部驗證回傳後仍須補實際結果，不提前宣告 G5。

**Deliverables**：Windows 驗收報告、可安裝套件及明確的預設值決定。

**Gate G5**：AC-E10 通過、量測與已知限制可查，且獨立審閱完成，才可更改預設。所有歷史與既有任務保持可讀，不以回退程式版本取代資料遷移相容設計。

2026-10-24，Slice 5a：Windows x64 Debug NSIS／portable 與 native Release DLL、
固定相依的離線重建來源、實際 notice 清單及 Sandbox／VM 驗證腳本已加入。
Core 494／GUI 25／CLI 12、Rust 13 零略過通過；系統 PATH 的正式 CLI 與
PowerShell 5.1 測試通過。11 個缺漏 crate notices 從完整上游 commit 補入，
另 3 項缺原文明列 DeclaredOnly，原生匯出預設停止，僅內部 Debug 檢視包可保留
診斷繼續；Release 不放行。ImageSharp key 仍未取得。產品負責人會在 Windows
Sandbox／VM 執行安裝／更新／卸載驗證；本機既有安裝與執行中程序未改動。
獨立審閱通過此 private Debug 打包 slice；Kit 的 18 份 checksum、portable 的
SOURCE.json 對應來源 ZIP，以及最終來源包離線 Rust 13 項測試均已確認。
Task 5.2／G5 仍待外部結果與發佈條件，Task 5.1 及平台驗證仍須繼續，未切換預設。

Slice 5b：以真實 NTFS lone-surrogate／U+FFFD 同名形態 fixture 確認 SQLite／JSON
轉碼會遺失路徑身份，已加入 root／inventory／FFI／操作與 legacy persistence guards。
完整 Core 507／GUI 25／CLI 12、獨立聚焦 13 零略過通過；保留合法 supplementary
Unicode，異常項目以 escaped 碼位報錯且保持 partial，未自動修補舊資料。
詳見 [filesystem evidence](../research/2026-10-24-windows-path-encoding.md)。
這是 Task 5.1 的已知邊界修正；規模／實體 USB 量測與平台測試仍待後續 slice。

Slice 5c tooling：六來源的可重建量測 host 與有整體期限／自有 child 管理的 runner
已加入；空／重複矩陣不能形成假成功。兩引擎 5k／50k 的完整答案、warm 零後端讀取、
+1% 只分析新檔、長路徑／排除及模擬離線恢復均通過。後續兩引擎 300k 整輪
也通過，快取重用、新增 3000 檔、完整答案及離線恢復皆符合；舊二進位已凍結。
抽取分析模組後的新版 host／bounded runner 另跑兩引擎 1000 檔通過，未冒充
重新量測大資料。可查的精簡數字保存在 research/data，原始報告留於 build。
數值與未測項見 [acceptance baseline](../research/2026-10-24-analysis-acceptance.md)。
GUI 互動、實體 USB 及 G5 仍待結果，未提升成熟度或切換預設。

Slice 5d：離屏 WPF 量測發現一萬筆衝突／結果控制項會全部建立，已改為有界
viewport 與 recycling，保留完整模型、可聚焦入口及可讀名稱。最終 20 筆量測／
斷言全部完成、binding errors 0，GUI 25 零略過；首尾 radio 選擇在回收後保留。
獨立審閱確認焦點與選擇邊界。數值與早期失敗 probe 限制見
[WPF scale](../research/2026-10-24-wpf-scale.md)。實體鍵盤、focus cue、高 DPI
及真實掃描中的互動仍由 Sandbox／VM 驗證，G5 未通過。

Slice 5e：completion audit 補上 Hub 載入完整 10001 筆的缺口；各組使用同一個
可回收、有界且可聚焦的清單樣式。23 筆 probe 全部完成，含最後列存取、回收
按鈕導覽與完整載入後篩選重設，binding errors 0，GUI 25 零略過。500 次
load-more 的同步累計時間不等於單次點擊或實體輸入延遲，限制仍見 WPF research。

### Phase 6：後續平台與 UI 技術驗證

- [x] Task 6.1：盤點並抽象化 Windows 路徑／磁碟／檔案身份及系統整合，將可攜的 Core／契約測試分離出目前 `net10.0-windows` 目標限制。來源服務接線與可攜分析 leaf 已完成；真正 Unix provider 與平台執行證據仍由 6.2／6.3 驗證，不代表整套 Core 已移植。
- [ ] Task 6.2：依可用硬體選定 macOS／Linux 平台及架構，建置對應 `.dylib`／`.so`，以小型資料驗證大小寫、Unicode、連結、權限與跨磁碟行為。
- [ ] Task 6.3：記錄各平台與檔案系統的支援證據；尚未完成執行／還原驗收的平台先限制為分析與檢視，不標為完整 File Workbench 支援。
- [x] Task 6.4：另行決定 Avalonia 技術驗證與 ADR-009 的後續狀態；只有通過驗證並取得 UI 採用決定後，才建立 GUI 移植計畫。現階段保留 WPF，Avalonia 驗證延後，未建立 GUI 移植計畫。

**Deliverables**：平台支援矩陣及 UI 技術評估。相似影片另訂能力驗收，不作為前面階段的必要條件。

**Gate G6**：AC-E11 通過後才宣告對應能力的預覽支援。Windows 的正式驗收不代表 macOS／Linux 已驗證，無大量個人檔案也不免除可重建資料的功能驗證。

Slice 6a：將唯讀分析抽成 `src/analysis/`、`net10.0` leaf，Core 單向引用，
保留 Windows 檔案身份與序列化形狀。Core 507／GUI 25／CLI 12 及獨立分析
測試通過；Windows published verifier 的連結建立明確略過，非通過。
已提供 [平台矩陣與 Windows 相依盤點](../platform-support.md)、Pi arm64／
macOS arm64 的 source/vendor/self-contained [私有驗證包](../analysis-platform-kit.md)。
Unix 尚無物理檔案身份 provider，目標機 native build/run 與跨磁碟結果待產品
負責人執行回傳；6.1 至 6.3 仍未全部完成，G6 未通過。ADR-009 已記錄保留
WPF、延後 Avalonia 驗證的安排，並非已驗證或採用跨平台 GUI。

Slice 6b：來源新增／canonical／overlap、快照 Capture／IsOnline、ScanWorker
重複探測，以及比較／圖片 session／preview 均接同一個 `ISourcePathService`。
Windows 預設保留既有行為，volume／startup 的現有介面沿用；未移植操作／GUI。
七項 fake-provider 回歸涵蓋別名、未知／替換身份、探測途中變更、不發布部分結果
及同 Job ID 恢復。Core 514／GUI 25／CLI 12／分析 21 零略過；實際 Unix provider
及目標 filesystem 結果仍待驗，本 slice 不提升非 Windows 支援或 G6 狀態。
獨立審閱另確認 canonical casing 變更以同一服務唯一配對，保留原 frozen Root／
RootIdentity；大小寫回歸與修正後完整測試通過，6.1 的本機抽象範圍完成。

Slice 6c tooling：verifier 新增 optional `--second-work`，target runner 新增
`--second-work-parent`，同一 request 跨兩個新根目錄檢驗三種算法、逐檔證據、
重複群組及來源不變。只有前後穩定且不同的 OS volume GUID／stat device ID
才能標示跨卷通過；同卷、未知身份或未指定第二根明確略過，不宣稱實體磁碟。
分析 Release 27 零略過；實際單根／同卷雙根通過，跨卷欄位為 false；既有第二
根拒絕時未建立第一根且 sentinel 未變。Bash 僅本機 syntax/help 驗證，Pi／Mac
建置與真正不同卷結果仍待 owner，Task 6.2／6.3 及 G6 不提前勾選。
獨立審閱通過；最終 Windows／平台套件均從 clean `87afbd8` 重建，Windows
18／平台 26442 份內部 checksum、PowerShell 5.1 正式 CLI 及 published verifier
雙根同卷檢查通過。最新產物／雜湊／回傳方式見
[packaging evidence](../research/2026-10-24-analysis-packaging.md)。最終 completion
audit 未找到其他當前範圍內必要本機程式缺口；G5／G6 與 owner 驗收仍未完成。

## Files to Create/Modify

以下列出交付位置；完成範圍以各階段勾選與測試紀錄為準。

| 路徑 | 預計動作 | 責任 |
|------|----------|------|
| `src/analysis/` | 新增（Phase 6 從 Core 抽出） | 可攜引擎介面、契約、registry、C# 與 Czkawka 適配 |
| `native/file-workbench-czkawka-ffi/` | 新增 | C ABI 包裝、版本固定、Rust 測試與建置設定 |
| `src/Core/Scanning/SourceScanner.cs`、`ScanWorker.cs` | 調整 | 保留探索／排程／索引責任，委派分析 |
| `src/Core/Index/IndexDatabase.cs`、`src/Core/Sources/ScanJobInfo.cs` | 調整 | 任務快照、證據來源、世代與遷移 |
| `src/Core/Stacks/`、`src/Core/Plans/` | 必要時調整 | 分離新分析證據與既有操作驗證，保持還原相容 |
| `src/Gui/Workbench/`、`src/Cli/Commands/` | 調整 | 共用引擎選擇、能力與任務顯示 |
| `tests/Core.Tests/Analysis/`、`tests/Gui.Tests/` | 新增／調整 | 契約、遷移、原生整合與 UI 回歸 |
| `scripts/windows/`、部署文件與安裝設定 | 新增／調整 | 固定原生產物的本機建置及安裝驗證 |

## Testing Strategy

- **既有回歸**：依 [testing.md](../testing.md) 執行 `dotnet test messy-file-workbench.sln --verbosity minimal`；只記錄實際執行結果。新 Rust crate 的測試命令在 crate 建立後寫入 testing.md，不把目前不存在的測試寫成已通過。
- **契約與整合**：兩個引擎各自對已知答案驗證，涵蓋空集合、失敗、部分覆蓋、來源變動、未知 ID、取消與版本不符；相似媒體用標註資料，結果一致不等於正確。
- **資料相容**：用真實舊 schema 的隔離 fixture 驗證備份、遷移、混合指紋、疊／計畫重新驗證、歷史及缺少引擎時還原；不可使用個人索引作破壞性測試。
- **原生與操作**：壓力測試記憶體生命週期、批次上限與程序異常；確認唯讀分析不動來源，檔案變更測試仍只透過 Core 的既有操作入口。
- **量測**：所有比較記錄引擎／參數／功能、範圍、硬體、檔案系統與快取狀態。性能門檻先量測再固定，不新增未量測的承諾。

## 驗收對照

| SPEC-002 驗收 | 主要階段 |
|---------------|----------|
| AC-E01、AC-E08 | Phase 4 |
| AC-E02、AC-E03 | Phase 0、1、2 |
| AC-E04、AC-E05、AC-E06 | Phase 3 |
| AC-E07 | Phase 2 |
| AC-E09 | Phase 1 至 4 |
| AC-E10 | Phase 5 |
| AC-E11 | Phase 6 |

## Risks & Mitigations

| 風險 | 影響 | 處理 |
|------|------|------|
| 上游輸入／輸出不足以支撐持久索引 | 高 | G0 先證明逐檔證據與範圍；缺口先補設計，不用重複群組冒充完整索引 |
| 新 hash 改變疊與舊計畫語意 | 高 | 版本化證據、遷移 fixture、獨立操作驗證及 G3 |
| 原生崩潰／合作取消延遲 | 高 | 明訂限制、分批／輪詢、隔離異常測試、下次啟動恢復；需要程序隔離時另立決策 |
| 維護兩個引擎與多平台範圍過大 | 中 | 能力不追齊，先 Windows 精確去重，再圖片；按能力分別提升成熟度 |
| 大量資料及非 Windows 測試不足 | 高 | 已知答案與可重建資料先行；缺測公開記錄，未驗證的平台不開放檔案變更 |

## Progress Log

| 日期 | 更新 |
|------|------|
| 2026-10-24 | 建立 ADR-015、SPEC-002 與本計畫；記錄 0.2.1 及上游查閱基準。只有文件，未開始任何實作階段。 |
| 2026-10-24 | Slice 0：建立共用已知答案與固定上游的 Rust crate，確認公開逐檔 API 可用；完整基準 Core 404／GUI 16、新 C# fixture 測試 1、Rust 測試 3 通過。尚未提供 C ABI 或切換應用程式引擎。 |
| 2026-10-24 | Slice 1：建立不可變的分析契約、registry、有限 C# 引擎與共用結果驗證；工作台委派候選分析，既有 xxh128 與操作驗證保留。Core 416／GUI 16 通過，G1 獨立審閱通過。 |
| 2026-10-24 | Slice 2：Windows x64 Rust C ABI、受控載入的 .NET 適配、生命週期／取消競爭回歸與建置腳本；Rust 10、Core 421／GUI 16 通過，G2 interop 獨立審閱通過。程序中斷恢復仍待 Phase 3；未變更預設、未 push。 |
| 2026-10-24 | Slice 3a：schema 3→4 備份與 legacy 證據表、固定任務／來源快照、精確版本恢復及來源身份檢查；新增 9 項遷移／快照測試。Core 430／GUI 16 零略過通過，獨立審閱確認換碟與前置取消競爭修正。引擎變更需建立新任務，取消等待停止確認；世代提交與程序中斷恢復仍待後續 slice。 |
| 2026-10-24 | Slice 3b：schema 5 保存逐代路徑／觀察，算法與版本專屬的可重用證據、完整世代原子啟用；發布前重新觀察可攔截跨批次同大小／同時間換檔。內容證據摘要與磁碟 metadata 摘要分開，待執行計畫會要求重新審閱。Core 443／GUI 16 零略過通過；缺 DLL 的冷啟動還原及程序中斷恢復仍待 Slice 3c，G3 尚未完成。 |
| 2026-10-24 | Slice 3c：加入 test-only scan-host，驗證原生呼叫期間程序損失、檢查點／原快照恢復、缺引擎等待及原版本續跑；缺 DLL 冷啟動仍可讀歷史並還原。Core 444／GUI 16、獨立聚焦 4 項零略過通過，Windows x64 G3 通過；未 push、未切換預設。 |
| 2026-10-24 | Slice 4a：WPF 設定提供目前精確內容能力的引擎／算法、成熟度及不可用原因，保存為新任務預設；既有快照不變。工作清單顯示原版本／算法與固定範圍，部分世代不顯示成功啟用。Core 446／GUI 19 零略過，獨立 Core 10／GUI 3 通過並完成畫面檢查；CLI 及相似圖片接線仍待後續 slice，G4 尚未通過。 |
| 2026-10-24 | Slice 4b：新增唯讀 `engines`／`analyze` CLI，使用共用 registry、固定快照、掃描／驗證與獨立持久索引；JSON 格式版本 1，保留原 `scan` 預設及格式。跨來源已知答案、取消停止確認與實際 DLL 皆有測試；修正資料目錄別名可能掃入自身的邊界。Core 453／GUI 19／CLI 8；獨立 Core 9／CLI 8 零略過通過。引擎比較與相似圖片仍待後續 slice。 |
| 2026-10-24 | Slice 4c：Core、CLI `compare-engines` 與 WPF 唯讀比較；分開保存索引、觀察、群組差異與程序時間／I/O／記憶體。GUI 兩輪沿用完整來源政策；收尾取消不回報成功。小型已知答案兩引擎均 TP 4／FP 0／FN 0，不推論效能優劣。完整 Core 461／GUI 20／CLI 10 零略過；相似圖片仍待後續 slice，G4 尚未通過。 |
| 2026-10-24 | Slice 4d 第一部分：adapter 0.2.0／ABI 1 新增實驗性相似圖片逐檔證據，使用公開 Czkawka decoder 與具 MIT 聲明的 transform hashing 適配。7 檔合成標註 corpus 在 ≤6 bits 下 TP 6／FP 0／FN 4／TN 11，裁切漏判明確記錄；Rust 12、Core 474／GUI 20／CLI 10 零略過。主索引圖片工作、CLI／WPF 候選審閱仍待後續 slice，Task 4.4／4.5 與 G4 尚未完成。 |
| 2026-10-24 | Slice 4d 第二部分：Core 圖片 inventory、逐批驗證／checkpoints、獨立感知世代發布與跨來源配對，保留 exact hash／操作計畫邊界。每來源解碼 2000、全域配對 2000 圖片／10000 配對上限；雲端狀態、祖先規則與身份在送出／發布／報告時重查。完整 Core 485／GUI 20／CLI 10、獨立聚焦 44 零略過；CLI／WPF 人工審閱仍待下一 slice，G4 尚未完成。 |
| 2026-10-24 | Slice 4e：WPF 圖片審閱頁與 `similar-images` CLI；保存人工註記、凍結範圍、算法／閾值及來源版本，缺引擎可重開。背景縮圖不放大、兩軸有界；審閱 metadata 使用內容 revision 與保存鎖，拒絕過期覆寫。Core 493／GUI 25／CLI 12、獨立 Core 8／GUI 5／CLI 12 零略過；完成畫面檢查，Windows x64 Debug G4 通過。Phase 5／6 仍待後續 slices，未 push。 |

---

*建立日期：2026-10-24*
