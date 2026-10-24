namespace FileWorkbench.Localization;

public static partial class AppText
{
  // i18n:en
  private static readonly Dictionary<string, string> AnalysisEnglish = new(StringComparer.Ordinal)
  {
    ["analysis.settings.title"] = "File analysis",
    ["analysis.settings.choice"] = "Engine for new scans",
    ["analysis.settings.hint"] = "Changes apply to new jobs. Queued and resumed jobs keep their original engine and settings.",
    ["analysis.settings.saveFailed"] = "Couldn't save the analysis selection: {error}",
    ["analysis.settings.unavailableSaved"] = "The saved engine version is unavailable. Choose an available engine for new jobs.",
    ["analysis.compare.button"] = "Compare C# and Czkawka",
    ["analysis.compare.open"] = "Show report folder",
    ["analysis.compare.hint"] = "Runs separately over online sources when scans are idle; never creates a file-operation plan. Leaving this page stops comparison.",
    ["analysis.compare.progress"] = "Run {pass}/2 · {engine} · {files} discovered, {confirmed} confirmation checks",
    ["analysis.compare.normalizing"] = "Comparing source observations and groups…",
    ["analysis.compare.saving"] = "Saving the comparison report…",
    ["analysis.compare.agree"] = "The runs agree on {groups} groups. Agreement alone is not a correctness test; see the report's scope and measurements.",
    ["analysis.compare.differ"] = "The runs differ; {groups} groups match. Review the separate results in the report.",
    ["analysis.compare.incomplete"] = "Comparison incomplete or source observations changed. Results cannot be treated as an accuracy comparison.",
    ["analysis.compare.canceled"] = "Comparison canceled; saved run indexes are retained.",
    ["analysis.compare.failed"] = "Comparison failed: {error}",
    ["analysis.capability.ExactDuplicates"] = "Exact content",
    ["analysis.capability.SimilarImages"] = "Similar images",
    ["analysis.capability.SimilarVideos"] = "Similar videos",
    ["analysis.maturity.Experimental"] = "Experimental",
    ["analysis.maturity.Preview"] = "Preview",
    ["analysis.maturity.Validated"] = "Validated",
    ["analysis.availability.Available"] = "Available",
    ["analysis.availability.Unsupported"] = "Unsupported",
    ["analysis.availability.MissingDependency"] = "Optional engine is not installed",
    ["analysis.availability.Incompatible"] = "Installed engine is incompatible",
    ["analysis.job.legacy"] = "Legacy job; no engine snapshot",
    ["analysis.job.demo"] = "Sample job",
    ["analysis.job.scope"] = "Fixed sources: {sources}. Parameters: {parameters}",
    ["analysis.job.partial"] = "Analysis incomplete; new evidence is not active",
    ["analysis.job.canceled"] = "Canceled",
    ["analysis.job.partialHint"] = "The previous complete result is retained. Check the skipped or failed files before scanning again.",
  };
  // i18n:end

  // i18n:zh-TW
  private static readonly Dictionary<string, string> AnalysisChinese = new(StringComparer.Ordinal)
  {
    ["analysis.settings.title"] = "檔案分析",
    ["analysis.settings.choice"] = "新掃描使用的引擎",
    ["analysis.settings.hint"] = "變更只影響新任務；已排隊及恢復的任務保留原引擎與設定。",
    ["analysis.settings.saveFailed"] = "無法儲存分析設定：{error}",
    ["analysis.settings.unavailableSaved"] = "原設定的引擎版本無法使用，請為新任務選擇可用引擎。",
    ["analysis.compare.button"] = "比較 C# 與 Czkawka",
    ["analysis.compare.open"] = "開啟報告資料夾",
    ["analysis.compare.hint"] = "掃描閒置時分別分析線上來源，不建立檔案操作計畫。離開此頁會停止比較。",
    ["analysis.compare.progress"] = "第 {pass}/2 輪 · {engine} · 發現 {files} 個、執行 {confirmed} 次確認",
    ["analysis.compare.normalizing"] = "正在比較來源觀察與群組…",
    ["analysis.compare.saving"] = "正在儲存比較報告…",
    ["analysis.compare.agree"] = "兩輪有 {groups} 組一致。結果一致不等於正確率，請查看報告的範圍與量測。",
    ["analysis.compare.differ"] = "兩輪結果不同，有 {groups} 組一致；請檢查報告中分開保存的結果。",
    ["analysis.compare.incomplete"] = "比較未完成或來源觀察已變動，不能把結果當成正確率比較。",
    ["analysis.compare.canceled"] = "已取消比較；保留已保存的分析索引。",
    ["analysis.compare.failed"] = "比較失敗：{error}",
    ["analysis.capability.ExactDuplicates"] = "精確內容比對",
    ["analysis.capability.SimilarImages"] = "相似圖片",
    ["analysis.capability.SimilarVideos"] = "相似影片",
    ["analysis.maturity.Experimental"] = "實驗性",
    ["analysis.maturity.Preview"] = "預覽",
    ["analysis.maturity.Validated"] = "已驗證",
    ["analysis.availability.Available"] = "可用",
    ["analysis.availability.Unsupported"] = "不支援",
    ["analysis.availability.MissingDependency"] = "尚未安裝選配引擎",
    ["analysis.availability.Incompatible"] = "已安裝的引擎不相容",
    ["analysis.job.legacy"] = "舊版任務，沒有引擎快照",
    ["analysis.job.demo"] = "模擬任務",
    ["analysis.job.scope"] = "固定來源：{sources}。參數：{parameters}",
    ["analysis.job.partial"] = "分析未完整，新證據尚未啟用",
    ["analysis.job.canceled"] = "已取消",
    ["analysis.job.partialHint"] = "保留上一份完整結果；請檢查略過或失敗的檔案後再掃描。",
  };
  // i18n:end
}
