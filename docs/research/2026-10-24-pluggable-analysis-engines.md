# 雙分析引擎與 Rust 互通調查

Date: 2026-10-24

Topic: C# 工作流程搭配可切換的自有分析與 `czkawka_core`。

## 本機查閱基準

- FMA：`531ab2e45aac4d7d0f7c7aabbdd57b2a2ffee724`，產品版本 0.2.1，Windows 目標。
- 使用者提供的 `../../fork/czkawka-fork/`：`eb8b91dbb4d25dff416202674ee0de84ee4b5e5c`，crate 版本 12.0.2。只讀查閱，沒有修改。這個相對位置是調查來源，不是 FMA 建置相依。
- 這次沒有建置 Rust、量測效能或驗證跨平台支援；上游已有功能不代表 FMA 已完成介接。

## 發現與影響

| 觀察 | 程式依據 | 對設計的影響 |
|------|----------|--------------|
| FMA 工作台直接建立 `SourceScanner`，內容指紋為帶 `xxh128:` 標記的 XxHash128 | `src/Core/Scanning/ScanWorker.cs`、`SourceScanner.cs` | 先分離探索／候選／提交與分析，不把整個工作台換成上游掃描 |
| 索引的 `files.hash`、疊及複製驗證與既有指紋相依 | `src/Core/Index/IndexDatabase.cs`、`Stacks/StackDetector.cs`、`Plans/PlanExecutor.cs` | 需證據版本化、舊資料遷移與操作驗證相容測試 |
| CLI 和工作台共用 Core 專案，但掃描接線不同 | `src/Cli/Commands/ScanCommand.cs`、`src/Gui/App.xaml.cs` | 兩個入口都要逐步接到同一分析契約，不能只改 GUI 就宣稱完成 |
| 上游 core 是無 UI 相依的 Rust crate，查閱範圍沒有 C ABI 匯出或 `cdylib` 設定 | 上游 `czkawka_core/Cargo.toml`、`src/lib.rs` | FMA 需自行建立薄 Rust 包裝及 C# 適配 |
| 上游 `Search` 使用 `Arc<AtomicBool>` 與 `Sender<ProgressData>`；輸入可設定 included paths | 上游 `common/traits.rs`、`common/tool_data.rs` | 不能把這些 Rust 型別直接當 C# API；需轉接取消與進度，逐工具驗證候選及結果覆蓋 |
| 上游另有修改來源的工具及操作 | 上游 `tools/`、CLI commands | 本次分析包裝只提供讀取能力，不暴露清理及修改入口 |

## 官方來源

- [固定 commit 的 core manifest](https://github.com/qarmin/czkawka/blob/eb8b91dbb4d25dff416202674ee0de84ee4b5e5c/czkawka_core/Cargo.toml)：core 宣告 MIT；實際發佈仍需盤點所有建置相依。
- [固定 commit 的 Search 等 traits](https://github.com/qarmin/czkawka/blob/eb8b91dbb4d25dff416202674ee0de84ee4b5e5c/czkawka_core/src/common/traits.rs)：Rust 端的分析與輸出介面。
- [固定 commit 的 core 說明](https://github.com/qarmin/czkawka/blob/eb8b91dbb4d25dff416202674ee0de84ee4b5e5c/czkawka_core/README.md)：共用核心、原生選配相依與影片功能需求。
- [Rust Reference：Linkage](https://doc.rust-lang.org/reference/linkage.html)：`cdylib` 是供其他語言載入的系統動態程式庫；僅改輸出種類不會自動產生適用 C# 的穩定契約。
- [Microsoft：P/Invoke](https://learn.microsoft.com/en-us/dotnet/standard/native-interop/pinvoke)：.NET 可呼叫原生函式與 callback；資料及生命週期仍需明確定義。

Relevance: 支撐 [ADR-015](../decisions/ADR-015-pluggable-analysis-engines.md) 的直接核心介接及版本化雙引擎邊界。

Action Items: 依 [PLAN-002 Phase 0](../plans/PLAN-002-pluggable-analysis-engines.md) 驗證逐檔證據、候選範圍、取消、索引相容與建置；本調查沒有替這些未完成項目作成功宣告。

## Phase 0 適配結果（2026-10-24）

FMA 基準為文件提交 `a01ba97`，.NET SDK 10.0.402；完整基準測試為 Core 404、GUI 16，全部通過。新加的 C# 已知答案測試單獨執行通過。Rust 工具鏈為 1.99.1、host `x86_64-pc-windows-msvc`。

在 FMA 內建立 `native/fma-czkawka-ffi/`，直接從 Git 取得固定上游 commit，產生 Cargo.lock，未更動外部 fork。原生三項測試全數通過；首次含相依編譯耗時約 2 分 51 秒，這不是掃描效能數字。

### 可用 API 與範圍決定

上游 `tools::duplicate::hash_calculation` 是公開的逐檔完整內容分析 API，接收檔案、buffer、HashType、讀取 byte counter 及 stop flag。不必呼叫 `DuplicateFinder.search`：

- FMA 已有完整探索與候選集合；逐檔 API 不再次走訪資料夾，不套用上游的最小檔案、extension 或 hardlink 隱藏預設值。來源範圍及連結政策留在 Core。
- 所有被提交的候選都能得到 hash、取消或錯誤，不會因為檔案不重複而從回傳中消失。空檔也能得到內容證據。
- 包裝檢查呼叫前取消，補上空檔在上游沒有 read chunk 時不檢查 stop flag 的行為；呼叫後再檢查取消及 size／mtime／byte counter，避免接受觀察到變動的結果。
- byte counter 必須等於該檔案大小才接受證據；這證明 API 的邏輯完整讀取範圍，沒有量測實體磁碟 I/O 或承諾取消延遲。
- 進度可使用該 counter 與逐檔終態，FFI 預計將它們轉成有界的輪詢事件；尚未驗證原生 job 協定。

### 已知答案與證據

共用 fixture 有 7 檔，包含跨資料夾改名副本、相同大小不同內容、唯一檔、Unicode 路徑及兩個空檔。預期群組為 `[101,102,107]`、`[105,106]`，由 fixture 指定，不以另一個引擎的輸出當答案。

Rust 驗證兩個預期群組、`abc` 的 BLAKE3 固定值、200 KB 檔案中段變動、空檔預先取消、missing／directory 錯誤與來源內容不變；C# 既有完整內容指紋符合相同群組且 size／mtime／內容不變。原生證據命名為 `blake3-v1:`，沒有冒用既有 `xxh128:`。

### 索引與執行相容設計

保留 `files.hash` 作目前疊偵測所使用的有型別指紋；新增帶引擎／算法／參數及檔案觀察版本的證據記錄。切換算法後，只在整批結果通過 Core 的範圍與觀察檢查時更新 active hash；不得只改前綴或混比算法。離線、取消及部分失敗不清除其他完整結果。

任務新增不可變的引擎快照。舊 `xxh128:` 可辨識為 legacy C# 證據，舊任務不推定為 Rust；原版本不可用時保留任務並明示重新建立。由 hash 改變而受影響的疊摘要與計畫需重新驗證。`PlanExecutor.CopyVerified` 的 XxHash128 複製驗證獨立保留，歷史及還原不得依賴分析引擎是否存在。

Slice 3b 已實作上述遷移，schema 5 分開保存 `file_fingerprints`（算法／參數／版本專屬證據）、`analysis_results`（Job／世代的核准路徑及原始觀察）與 `files.hash`（最後啟用結果）。整代成功並重新觀察後才啟用；失敗不把未回報檔案當成消失。快取命中保留原計算任務與時間，新世代仍保存本次核對的觀察。`CopyDigest.Of` 保留磁碟 metadata 比對用途，另以 `EvidenceOf` 記錄內容證據並使受影響的待執行計畫重開。

最後觀察採 500 筆分頁，不保留所有檔案 handle，也不是跨來源檔案系統的原子快照；外部程式仍可在之後修改檔案。既有操作前重新驗證與回收／還原流程仍適用。Windows 已驗證；Pi arm64／labwc Wayland 與 Apple Silicon macOS 的驗證將由使用者執行準備好的指令，尚無平台通過報告。ImageSharp 授權尚未取得，Release 應用程式驗收仍待完成。

### G0 結論與後續限制

公開 API 能支撐候選優先、逐檔覆蓋及完整內容證據；不需為這個能力修改上游。G0 適配驗證通過，可開始共用契約與 C# 抽離。Windows x64 以外、相似媒體、FFI 記憶體生命週期、持久任務及舊資料遷移仍需後續階段驗證。ImageSharp 的既有 Debug 授權警告仍在，不能由 Rust 適配通過推論 Release 已可發佈。

獨立審閱已確認 G0 可行性，並要求修改時間無法讀取時明確失敗；已修正，Rust test／fmt／clippy 再次通過。審閱也獨立重跑原生 3 個與 C# 基準 1 個測試。後續階段仍須測試執行中取消與檔案觀察競爭條件。
