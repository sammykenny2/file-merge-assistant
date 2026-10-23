namespace FileMergeAssistant.Localization;

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
