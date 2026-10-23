# 相似圖片逐檔適配（2026-10-24）

PLAN-002 Slice 4d 新增原生 `SimilarImages` 逐檔證據與 Core 持久工作，成熟度
Experimental。GUI／CLI 的候選審閱與完整工作流程尚待後續 slice；
不能宣告 G4 或相似圖片產品驗收完成。

## 適配範圍

固定 Czkawka 12.0.2，commit `eb8b91dbb4d25dff416202674ee0de84ee4b5e5c`。
本機查閱來源 `../../fork/czkawka-fork/czkawka_core/`，沒有修改外部 fork。
公開 `common::image::get_dynamic_image_from_path` 可以逐檔載入；
`SimilarImages::collect_image_file_entry`／`compute_hashes_for_image` 與分組狀態為私有。
只取重複群組無法證明每個候選都處理過，因此採最小的 FMA 適配：

- 使用公開上游 decoder，不走上游資料夾探索、cache 或檔案修改工具。
- 在 FMA 包裝中適配其 transform hashing：Mean 8×8、Lanczos3，
  原圖、水平／垂直鏡射、90／180／270 度旋轉及鏡射旋轉。
  每檔保存 1–8 個排序、去重的 64-bit hash；全零／全一的無資訊 hash 排除。
- `image 0.25.10`、`image_hasher 3.1.1` 與上游使用同一 lockfile 版本，
  沒有升級其他相依。MIT 衍生程式碼的完整聲明見
  [THIRD-PARTY-NOTICES](../../native/fma-czkawka-ffi/THIRD-PARTY-NOTICES.md)。
- FMA 自行定義候選配對與距離，不宣稱等同上游的 parent/group 選擇。

版本化算法為 `image-mean8-d8-v1`，距離為兩份 transform hash 集合的
最小 Hamming distance（0–64 bits），越低越近。這不是百分比，
距離 0 也不是精確內容相同。Core 驗證編碼及配對距離；精確內容
`ContentFingerprints` 拒收這個算法。
`SourceScanner` 的精確索引入口也明確拒收這個能力，避免經由一般
validator 接受後把感知 hash 發布到 `files.hash`。

第一版只啟用 PNG／JPEG；內容格式也必須吻合，其他格式明確失敗。
PNG 動畫僅取 default image，不保證動畫相似性。預先檢查 encoded file
不超過 32 MiB、尺寸不超過 24 megapixels；每次只保留一份轉換 bitmap。
這些是 preflight 限制，沒有程序隔離或原子檔案快照，不能視為硬性的
decoder 記憶體上限。取消在載入與各 transform 之間檢查，decoder 內部
可能延遲停止確認。空白／損壞／超限檔有逐檔錯誤，不能當作成功零結果。

## 標註 fixture 與結果

`tests/Fixtures/similar-images/labels.json` 保存 family、variant、SHA256、
閾值與預期配對統計。7 個檔案均為可重建合成圖，沒有私人照片或
上游測試素材；Rust 測試中的整數圖案生成器為來源。A 家族包含原圖、
縮小、JPEG quality 45 重壓縮、90 度旋轉、裁切；B／C 為不同圖案。
同 family 為正例，21 個配對中 10 正例、11 負例。

| 預設候選閾值 | TP | FP | FN | TN | Precision | Recall |
|--------------|---:|---:|---:|---:|----------:|-------:|
| ≤6 bits | 6 | 0 | 4 | 11 | 100% | 60% |

原圖對縮小／重壓縮／旋轉距離均為 0；原圖對裁切為 12，
裁切與另外 3 個 A 變體也為 12，因此 4 個漏判均涉及裁切。
不同家族的最小距離為 16。裁切不是這個閾值下已支援的能力，
不能為了此 fixture 的滿分而無量測地放寬預設。
這個微型合成 corpus 不代表個人照片的真實正確率，實際使用仍需人工審閱。

Rust 測試先驗證逐檔結果與來源 bytes 未改動；.NET 實際 DLL 測試
獨立核對 SHA256、全部檔案覆蓋、Core validator 及上述配對統計。
另測試壞圖的部分終態、無資訊圖、大小限制、取消與格式錯誤。

## 重現與版本相容

從 repo 根目錄執行 `scripts/windows/Build-NativeAnalysis.ps1 -Test`。
聚焦測試類別為 `PerceptualAnalysisTests`；Rust 測試為 `images::tests`。
生成器只有設定 `FMA_WRITE_IMAGE_FIXTURE_DIR` 才額外輸出合成圖，
逐檔 `create_new`，不覆寫現有資料。

Adapter 升至 0.2.0、C ABI 仍為 1；原 0.1.0 快照不能以新版默默續跑，
需原版或新建工作。既有精確內容算法、預設與歷史結果不變。
ImageSharp Release 授權仍待取得；這項 Rust Debug 驗證不能取代發佈驗收。

## Core 持久任務與全域候選

Slice 4d 第二部分提供 `SimilarImageAnalysis`。每輪建立獨立 analysis index，
保留完整來源 inventory、固定引擎／來源快照、逐檔觀察、錯誤、世代及證據。
沿用 schema 5 的 `file_fingerprints` 與 `analysis_results`，以算法、參數、
引擎版本及任務快照識別種類，不新增或重解讀既有 exact hash。

精確 `SourceScanner` 入口仍拒絕圖片設定；`ImageSourceScanner` 重用 inventory
後，按每批 100 個圖片呼叫引擎。Cache lookup、送出前與 checkpoint 前都重新
驗證身份、大小／時間、祖先連結、來源規則及 cloud-only flags。
變成雲端檔時只更新隔離清單並回報部分完成，不送入 decoder／下載。
發布前與最後報告整理也重查雲端狀態；先前 checkpoint 的圖片若變成
cloud-only，不作為本輪完整候選，報告不帶可誤用的感知 fingerprint。
圖片發布必須使用 `PublishPerceptual`；這個入口只標記完整圖片世代，
不啟用 `files.hash`。既有有效 exact hash 可保留；觀察到實際檔案改變時
原證據仍會失效。沒有新增任何決定、操作計畫或執行歷史。

全域比較按已驗證的 per-file hashes 產生跨來源配對；每對有兩個 File ID
與 bit distance，threshold 另外保存於報告，與可重用的 hash 設定分開。
可選閾值 0–16，預設 6；實驗版每來源最多 2000 個圖片送入解碼，
全域配對最多比較 2000 個圖片、產生最多 10000 個候選配對。
全域數量在各來源完成分析後、配對前檢查，因此多來源的總解碼量可能
超過 2000；這不是跨來源的解碼總量上限。超限明確回報，保留索引，
不把截斷結果當成功。
部分完成、來源改變、未說明的本機圖片缺證據及取消都不回報完整候選。
取消等待 backend 停止，已保存 inventory／checkpoints 保留。

回歸覆蓋實際 DLL 跨來源配對、損壞圖片的部分終態、既有 exact hash
不被感知 hash 覆寫、2000／10000 上限與停止確認；後續批次檔案在第一批
處理時被換成同大小／同時間的新身份或設成 Offline，也不會被送入 backend。
Offline 測試只設可重建檔案的 attribute，沒有連接雲端服務或下載個人檔案。
另外測試已 checkpoint 的早期檔在下一批被設 Offline，以及工作完成後、
報告整理前的雲端變動或缺證據；結果都不能宣告完整，所有非 Completed
報告皆無可用候選配對。這些檢查仍不是跨來源原子快照，預覽須再次驗證。
