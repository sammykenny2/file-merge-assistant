namespace FileWorkbench.Localization;

// The window header: the section tabs and the index status strip under them. The Workbench and Sources tabs use
// hub.title and sources.title, and the Settings tab settings.title.
public static partial class AppText
{
  // i18n:en
  private static readonly Dictionary<string, string> HeaderEnglish = new(StringComparer.Ordinal)
  {
    ["header.productName"] = "File Workbench",
    ["header.productTagline"] = "Your file organization expert",
    ["header.nav.pending"] = "Pending",
    ["header.nav.history"] = "History",
    ["header.demo"] = "Sample data",
    ["header.demoTip"] = "Started with --demo: sample data from the GUI flow mock; your files are not read or changed.",
    ["header.strip.name"] = "Index status",
    ["header.strip.noSources"] = "No sources yet. Folders you add are indexed in the background, and you can keep working meanwhile.",
    ["header.strip.indexed.one"] = "{count} file indexed",
    ["header.strip.indexed.other"] = "{count} files indexed",
    ["header.strip.job"] = "{title}: {progress}",
    ["header.strip.next"] = "Next: {title}",
    ["header.strip.offline"] = "{path} {name} is offline",
    ["header.strip.open.one"] = "{count} stack to consolidate",
    ["header.strip.open.other"] = "{count} stacks to consolidate",
    ["header.job.pausing"] = "Pausing",
    ["header.job.paused"] = "Paused",
    ["header.job.discovering.one"] = "Discovering, {count} file found so far",
    ["header.job.discovering.other"] = "Discovering, {count} files found so far",
    ["header.job.checking"] = "Checking contents, {count} processed",
  };
  // i18n:end

  // i18n:zh-TW
  private static readonly Dictionary<string, string> HeaderChinese = new(StringComparer.Ordinal)
  {
    ["header.productName"] = "貓檔家",
    ["header.productTagline"] = "整理檔案的專家",
    ["header.nav.pending"] = "待執行",
    ["header.nav.history"] = "歷史",
    ["header.demo"] = "模擬資料",
    ["header.demoTip"] = "以 --demo 啟動：用的是 GUI 流程 mock 的模擬資料，不會讀取或改動你的檔案。",
    ["header.strip.name"] = "索引狀態",
    ["header.strip.noSources"] = "還沒有來源。加入資料夾後，會在背景建立索引，這段時間仍可以操作。",
    ["header.strip.indexed.one"] = "已索引 {count} 個檔",
    ["header.strip.indexed.other"] = "已索引 {count} 個檔",
    ["header.strip.job"] = "{title}：{progress}",
    ["header.strip.next"] = "下一個：{title}",
    ["header.strip.offline"] = "{path} {name}離線",
    ["header.strip.open.one"] = "待整理 {count} 疊",
    ["header.strip.open.other"] = "待整理 {count} 疊",
    ["header.job.pausing"] = "正在暫停",
    ["header.job.paused"] = "已暫停",
    ["header.job.discovering.one"] = "探索中，已找到 {count} 個檔",
    ["header.job.discovering.other"] = "探索中，已找到 {count} 個檔",
    ["header.job.checking"] = "確認內容中，已處理 {count} 個",
  };
  // i18n:end
}
