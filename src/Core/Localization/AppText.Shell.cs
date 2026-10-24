namespace FileWorkbench.Localization;

// The tray, the dialogs the app draws itself and the shell header. One entry per line: the key, then its text.
public static partial class AppText
{
  // i18n:en
  private static readonly Dictionary<string, string> TrayEnglish = new(StringComparer.Ordinal)
  {
    ["tray.tip"] = "File Workbench · Consolidate copies stack by stack",
    ["tray.tipDownloading"] = "File Workbench · Downloading update {percent}%",
    ["tray.open"] = "Open File Workbench",
    ["tray.hideMain"] = "Hide main window",
    ["tray.checkUpdates"] = "Check for updates…",
    ["tray.exit"] = "Exit File Workbench",
  };

  private static readonly Dictionary<string, string> DialogEnglish = new(StringComparer.Ordinal)
  {
    ["dialog.alreadyOpen"] = "File Workbench is already open. Switch to the open window.",
    ["dialog.indexFailed"] = "Couldn't open the index: {message}\n\nIndex location: {path}",
    ["dialog.unexpected"] = "Something unexpected went wrong, and this action didn't finish:\n{message}\n\nDetails are in the log folder: {logs}",
    ["dialog.updateTitle"] = "File Workbench update",
    ["dialog.updateAvailable"] = "Version {version} is available (you have {current}).\n\nUpdate and restart File Workbench now?",
    ["dialog.updateAvailableSummary"] = "Version {version} is available (you have {current}).\n\n{summary}\n\nUpdate and restart File Workbench now?",
    ["dialog.upToDate"] = "File Workbench {version} is up to date.",
    ["dialog.installFailed"] = "The update didn't finish. Saved data is not affected.\n\n{detail}",
    ["dialog.busy"] = "Files are still being moved by a run, retry, restore or emptying. Exit after it finishes.",
  };
  // i18n:end

  // i18n:zh-TW
  private static readonly Dictionary<string, string> TrayChinese = new(StringComparer.Ordinal)
  {
    ["tray.tip"] = "貓檔家 · 一疊一疊整理副本",
    ["tray.tipDownloading"] = "貓檔家 · 下載更新 {percent}%",
    ["tray.open"] = "開啟 貓檔家",
    ["tray.hideMain"] = "收起主畫面",
    ["tray.checkUpdates"] = "檢查更新…",
    ["tray.exit"] = "結束 貓檔家",
  };

  private static readonly Dictionary<string, string> DialogChinese = new(StringComparer.Ordinal)
  {
    ["dialog.alreadyOpen"] = "貓檔家 已經開著了。請切換到已開啟的視窗。",
    ["dialog.indexFailed"] = "無法開啟索引：{message}\n\n索引位置：{path}",
    ["dialog.unexpected"] = "發生了沒有預期的錯誤，這個動作沒有完成：\n{message}\n\n詳細內容寫在記錄檔：{logs}",
    ["dialog.updateTitle"] = "貓檔家 更新",
    ["dialog.updateAvailable"] = "有新版本 {version}（目前 {current}）。\n\n要現在更新並重新啟動 貓檔家 嗎？",
    ["dialog.updateAvailableSummary"] = "有新版本 {version}（目前 {current}）。\n\n{summary}\n\n要現在更新並重新啟動 貓檔家 嗎？",
    ["dialog.upToDate"] = "貓檔家 {version} 已是最新版本。",
    ["dialog.installFailed"] = "更新沒有完成；已保存的資料不受影響。\n\n{detail}",
    ["dialog.busy"] = "執行、重試、還原或清空還在移動檔案，請等它完成後再結束。",
  };
  // i18n:end
}
