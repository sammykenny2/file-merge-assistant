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
