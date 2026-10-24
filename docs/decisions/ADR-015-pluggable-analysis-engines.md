# ADR-015：可切換的 C# 與 Czkawka 分析引擎（D-02）

## 狀態

Accepted（2026-10-24：產品負責人要求將雙引擎策略整理為 ADR、SPEC、PLAN，後續授權依計畫分片實作）。接受的是架構方向；目前完成範圍以 PLAN-002 的檢查表與驗證紀錄為準。

詳細契約見 [SPEC-002](../specs/SPEC-002-pluggable-analysis-engines.md)，實作順序與驗證閘門見 [PLAN-002](../plans/PLAN-002-pluggable-analysis-engines.md)。SPEC 已核准分階段實作，PLAN 進行中；實作提交先不 push。

## 背景（決策前查閱基準）

- 目前 File Workbench GUI 與 CLI 使用 C# Core。工作台的 `SourceScanner` 同時負責探索、候選確認及索引更新；內容指紋使用帶有 `xxh128:` 標記的 XxHash128。CLI 仍有另一條服務式掃描路徑，兩個入口尚未統一為可切換引擎。
- File Workbench 的來源識別、完整索引、佇列、疊與審閱決定、操作計畫、回收及還原，並不是單一重複檔掃描工具可以取代的功能。保留這些產品流程於 C# Core。
- `czkawka_core` 提供重複檔案、相似圖片與影片等分析工具。查閱版本為 12.0.2、commit `eb8b91dbb4d25dff416202674ee0de84ee4b5e5c`；它提供 Rust API，當時尚無供 File Workbench 呼叫的 C ABI。這是決策前的評估基準；後續固定相依與自有 C ABI 實作見 PLAN-002。
- 自有 C# 實作仍有價值，但功能與大量資料驗證尚不完整，不要求它立刻與上游全部工具功能相同。
- 使用者的主要資料與實際大量驗證環境在 Windows；macOS／Linux 是後續希望提供的使用平台，但可用的真實資料與驗證資源較少。

## 決策

### 1. 將分析引擎放在共用抽象之後

在 File Workbench Core 定義 `IFileAnalysisEngine` 與中立資料契約，提供兩個獨立實作：

- **C# 引擎**：逐步從既有分析程式抽出，先支援精確內容去重，標示各能力的實際完成與驗證程度。
- **Czkawka 引擎**：由 File Workbench 擁有的薄 Rust 包裝依賴固定版本的 `czkawka_core`，建置為 `cdylib`，C# 透過 P/Invoke 呼叫。先驗證 Windows x64；其他平台使用對應的原生程式庫。

以下為決策採用的責任邊界；逐段實作與驗收以 PLAN-002 為準。
Phase 6 已將中立契約與兩個適配抽成 `src/analysis/`、`net10.0` leaf，由
Windows Core 單向引用；Core 仍擁有來源／索引／操作責任。這沒有移植整套
Core 或 WPF，平台缺口見 [支援矩陣](../platform-support.md)。

```text
[File Workbench GUI] --+
           +--> [File Workbench Core / C#]
[File Workbench CLI] --+           |
                       v
             [IFileAnalysisEngine]
                  /         \
                 v           v
         [C# engine]    [Rust FFI library]
                             |
                             v
                      [czkawka_core] <-- [Czkawka CLI]
                             ^
                             |
                       [Krokiet GUI]
```

Czkawka CLI 與 Krokiet 只用來說明上游的平行入口，均不成為 File Workbench 執行時的必要元件。Rust 包裝放在 File Workbench repo，建置不可依賴開發者電腦上的兄弟 checkout。

### 2. File Workbench Core 保有資料與檔案變更的責任

- File Workbench 管理完整檔案清單、來源身份、排除／保護規則、候選集合及 SQLite 的唯一寫入流程，維持 [ADR-004](./ADR-004-candidate-first-scanning.md) 與 [ADR-008](./ADR-008-sqlite-local-index.md)。
- 引擎只分析核准的範圍並回傳證據、候選、進度及錯誤。不得改動掃描來源、直接寫入 File Workbench 資料庫，或呼叫上游的刪除、連結替換、EXIF 清除及轉碼動作。
- 引擎可使用 File Workbench 分配的工作／快取目錄；這些可重建資料不是持久索引，也不是回收區。
- 結果仍須經過 File Workbench 的審閱、計畫確認與執行前驗證。相似候選不提升為內容相同的證據；保護規則、回收與還原沿用既有 ADR。

### 3. 能力可不對等，選擇必須可追溯

- 能力支援、執行時可用性、驗證成熟度分開呈現，且按平台記錄。上游有某個工具，不等於 File Workbench 包裝已支援或驗證它。
- 使用者可明確選擇可用的引擎；不支援、缺少程式庫及分析失敗，不能回報為「找到零筆」，也不默默改用另一個引擎。
- 每個分析任務保存引擎與上游版本、契約／ABI 版本、功能、演算法、參數及範圍快照。設定改動只影響新任務；恢復任務不能換引擎或換演算法。
- 引擎只負責產生候選，不自動選正本或提交檔案操作。比較兩個引擎時，建立分開的分析任務與結果，不同時重複安排檔案變更。

### 4. 先驗證，再切換預設值

- Czkawka 是主要分析引擎的候選，不因這份 ADR 就改變目前版本的行為。只有通過契約、索引相容、Windows 驗收與部署檢查的能力，才能被提升為預設。
- C# 路徑先保留既有行為；抽出後的引擎不因「自有實作」而自動標為已驗證。未完成能力標示不支援，未通過驗收的能力標示實驗性。
- 既有指紋、疊識別與待執行計畫不能被另一種 hash 解讀或覆寫。需要遷移時，先備份、增加來源版本資訊，並讓受影響的計畫重新驗證；歷史與還原能力不依賴目前選擇的分析引擎。
- 原生包裝和 C# 在同一程序內；可切換不是崩潰隔離。可恢復錯誤使用結果狀態回報，程序級失敗則沿用中斷恢復。若需要強制終止或崩潰隔離，另行決定 worker 程序架構。

### 5. GUI 與平台策略分開演進

- 本決策不重寫 GUI，不移植全部 C# Core，也不定案 Avalonia。現有 WPF 繼續使用，未來的 UI 也接同一套 Core 介面。
- [ADR-009](./ADR-009-ui-technology.md) 的「沒有跨平台需求」前提改為需要重評；Avalonia 是後續技術驗證候選，取代 WPF 仍需另行決定。
- Windows 優先驗證；macOS／Linux 先以可重建資料驗證分析功能。File Workbench 的來源識別、路徑、系統整合與檔案操作仍需分平台實作，不能沿用上游的跨平台宣告作為 File Workbench 的驗收結果。
- 原生相依固定版本、平台與架構，與程式一併打包；終端使用者不需要安裝 Rust 工具鏈。不新增網路服務或 listener，CI 維持停用。

## 影響

### 正面

- 保留 File Workbench 的工作流程及既有回歸測試，分析演算法可獨立替換。
- 可以逐項接入成熟的上游分析能力，不要求自有實作全部追齊。
- GUI、CLI 與後續跨平台 UI 可共用能力查詢、結果及任務語意。

### 負面

- 要維護兩個引擎、C ABI、原生套件及每個平台的驗證紀錄。
- 上游的路徑式掃描、快取及結果結構，未必能直接滿足 File Workbench 的候選批次與持久索引，需要先證明可適配。
- 原生版本更新、記憶體所有權與取消語意需獨立回歸；上游成熟不能取代 File Workbench 的整合驗收。

### 中性

- 相同的已知答案資料集可驗證精確去重；相似圖片需分別量測誤判與漏判，不能只比較兩個引擎是否一致。
- `czkawka_core` 宣告 MIT，不代表所有選配相依與 GUI 均採相同授權；打包前需按實際建置內容整理授權。

## 考慮過的替代方案

| 方案 | 優點 | 本次未採用的原因 |
|------|------|------------------|
| 全面改為 Rust／Slint | GUI 可直接引用 Rust 核心 | File Workbench 的索引、任務、審閱與還原也要移植，超過目前引擎整合需求 |
| 先呼叫 Czkawka CLI | 可快速試用，具程序隔離 | 本次選擇直接核心介接；CLI 參數／匯出結果不作為長期的內部契約 |
| 只維護 C# 引擎 | 工具鏈單純 | 相似媒體分析需自行補齊，無法利用既有上游能力 |
| 自動切換到另一個引擎 | 缺少相依時看似可繼續 | 能力、範圍與結果語意可能改變，無法保證任務可重現 |

## 參考

- [SPEC-002](../specs/SPEC-002-pluggable-analysis-engines.md)、[PLAN-002](../plans/PLAN-002-pluggable-analysis-engines.md)
- [調查基準與外部來源](../research/2026-10-24-pluggable-analysis-engines.md)
- [ADR-003 保護規則](./ADR-003-protected-source-writes.md)、[ADR-007 回收區](./ADR-007-recycle-area-policy.md)、[ADR-010 來源身份](./ADR-010-source-identity.md)

---

*決策日期：2026-10-24*
*決策者：產品負責人*
