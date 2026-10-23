# 雙引擎比較驗證（2026-10-24）

PLAN-002 Slice 4c 提供 Core／CLI／WPF 唯讀比較。兩輪使用獨立索引，
群組按成員路徑正規化；不混比 XXH128 與 BLAKE3 的字串，不建立操作計畫。
來源觀察不同時標示 Incomparable。這份記錄是可重建 fixture 的正確性基準，
大量資料、安裝及跨平台驗收仍需後續階段。

## 已知答案

共用 `tests/Fixtures/analysis/` fixture：7 檔、26 bytes，包含 Unicode、
改名副本、同大小不同內容與空檔。6 個大小候選共 12 bytes。
預期群組 `[101,102,107]`、`[105,106]`，由 fixture 定義。
21 個檔案配對中，4 個為正例；兩引擎各自得到 TP 4／FP 0／FN 0，
2 個群組一致。測試也核對來源 bytes 未變動。

| 配置 | 精確算法 | 引擎版本 | 上游 |
|------|----------|----------|------|
| C# | xxh128-v1 | 1.0.0 | FMA |
| Czkawka | blake3-v1 | 0.1.0 / ABI 1 | 12.0.2，eb8b91dbb4d25dff416202674ee0de84ee4b5e5c |

## 單次量測

Windows NT 10.0.26300.0、Intel64 Family 6 Model 183 Stepping 1 GenuineIntel，
.NET SDK 10.0.402、Rust 1.99.1，兩者均 Debug。
每輪新建 FMA 索引；OS cache 未控制，同一程序依 C#、Rust 順序執行，
native library 已於計時前載入。未量測完整硬體／檔案系統矩陣。

| 程序指標 | C# | Czkawka |
|----------|---:|--------:|
| Session elapsed，ms | 153 | 81 |
| Process read bytes | 196736 | 196736 |
| Process write bytes | 260092 | 260092 |
| Working set before，bytes | 87580672 | 100503552 |
| Working set after，bytes | 100454400 | 101715968 |
| Process lifetime peak，bytes | 100462592 | 101715968 |

時間包含來源登記、探索、SQLite 及結果驗證；I/O 是整個程序的計數，
不是實體磁碟量。Working set 是取樣，peak 是程序整個生命週期的最大值，
不能歸因於單一引擎。此微型 fixture 的 JIT、順序及 cache 影響很大，
上述數字不支持「Rust 較快」或改預設值。

本機原始報告 `.tmp/comparison-evidence/known-answer-comparison.json`，
SHA256 `61395FECE783374C87CD0B04D2155EBDE8796A0553A2935B43FE474C1EB088DB`。
原始報告包含暫存絕對路徑，未提交；重跑會產生新路徑／時間與不同 checksum。

## 重現

先執行 `scripts/windows/Build-NativeAnalysis.ps1 -Test` 建置及啟用原生測試。
在 repo 根目錄設定匹配的 native artifact 位置後，執行：

```powershell
$env:FMA_NATIVE_LIBRARY_DIR = Join-Path (Get-Location) 'native/fma-czkawka-ffi/target/x86_64-pc-windows-msvc/debug'
$env:FMA_COMPARISON_REPORT_DIR = Join-Path (Get-Location) '.tmp/comparison-evidence'
dotnet test tests/Core.Tests/Core.Tests.csproj --no-restore --filter FullyQualifiedName~Managed_And_Native_Known_Answers_Agree_With_Separate_Time_Io_And_Memory_Records
```

其他回歸包含來源變更、第二輪前置失敗、可變 caller path list、
完整來源排除／regenerable 規則凍結、工作完成後取消及比較收尾取消。
GUI 檢查報告保存、語言切換、binding、主索引任務／計畫／歷史未被修改。
ImageSharp 授權尚未取得，這些 Debug 檢查不代表 Release 已可發佈。
