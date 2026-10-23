namespace FileMergeAssistant.Localization;

// The tray, the dialogs the app draws itself and the shell header. One entry per line: the key, then its text.
public static partial class AppText
{
  // i18n:en
  private static readonly Dictionary<string, string> TrayEnglish = new(StringComparer.Ordinal)
  {
    ["tray.tip"] = "File Merge Assistant · Consolidate copies stack by stack",
    ["tray.tipDownloading"] = "File Merge Assistant · Downloading update {percent}%",
    ["tray.open"] = "Open File Merge Assistant",
    ["tray.hideMain"] = "Hide main window",
    ["tray.checkUpdates"] = "Check for updates…",
    ["tray.exit"] = "Exit File Merge Assistant",
  };

  private static readonly Dictionary<string, string> DialogEnglish = new(StringComparer.Ordinal)
  {
    ["dialog.alreadyOpen"] = "File Merge Assistant is already open. Switch to the open window.",
    ["dialog.indexFailed"] = "Couldn't open the index: {message}\n\nIndex location: {path}",
    ["dialog.unexpected"] = "Something unexpected went wrong, and this action didn't finish:\n{message}\n\nDetails are in the log folder: {logs}",
    ["dialog.updateTitle"] = "File Merge Assistant update",
    ["dialog.updateAvailable"] = "Version {version} is available (you have {current}).\n\nUpdate and restart File Merge Assistant now?",
    ["dialog.updateAvailableSummary"] = "Version {version} is available (you have {current}).\n\n{summary}\n\nUpdate and restart File Merge Assistant now?",
    ["dialog.upToDate"] = "File Merge Assistant {version} is up to date.",
    ["dialog.installFailed"] = "The update didn't finish. Saved data is not affected.\n\n{detail}",
  };
  // i18n:end

  // i18n:zh-TW
  private static readonly Dictionary<string, string> TrayChinese = new(StringComparer.Ordinal)
  {
    ["tray.tip"] = "File Merge Assistant · 一疊一疊整理副本",
    ["tray.tipDownloading"] = "File Merge Assistant · 下載更新 {percent}%",
    ["tray.open"] = "開啟 File Merge Assistant",
    ["tray.hideMain"] = "收起主畫面",
    ["tray.checkUpdates"] = "檢查更新…",
    ["tray.exit"] = "結束 File Merge Assistant",
  };

  private static readonly Dictionary<string, string> DialogChinese = new(StringComparer.Ordinal)
  {
    ["dialog.alreadyOpen"] = "File Merge Assistant 已經開著了。請切換到已開啟的視窗。",
    ["dialog.indexFailed"] = "無法開啟索引：{message}\n\n索引位置：{path}",
    ["dialog.unexpected"] = "發生了沒有預期的錯誤，這個動作沒有完成：\n{message}\n\n詳細內容寫在記錄檔：{logs}",
    ["dialog.updateTitle"] = "File Merge Assistant 更新",
    ["dialog.updateAvailable"] = "有新版本 {version}（目前 {current}）。\n\n要現在更新並重新啟動 File Merge Assistant 嗎？",
    ["dialog.updateAvailableSummary"] = "有新版本 {version}（目前 {current}）。\n\n{summary}\n\n要現在更新並重新啟動 File Merge Assistant 嗎？",
    ["dialog.upToDate"] = "File Merge Assistant {version} 已是最新版本。",
    ["dialog.installFailed"] = "更新沒有完成；已保存的資料不受影響。\n\n{detail}",
  };
  // i18n:end
}
