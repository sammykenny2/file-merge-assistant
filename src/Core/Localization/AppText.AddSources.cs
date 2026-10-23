namespace FileMergeAssistant.Localization;

// The add-sources screen: the first run, and adding more folders later. The example stack's paths are sample data;
// only the parts a Windows in that language would name differently are translated.
public static partial class AppText
{
  // i18n:en
  private static readonly Dictionary<string, string> AddSourcesEnglish = new(StringComparer.Ordinal)
  {
    ["addSources.title.first"] = "Start with the places you keep things",
    ["addSources.title.more"] = "Add folders",
    ["addSources.lead.first"] = "No need to scan everything at once. Folders you add are indexed in the background. When the same thing turns up in two places, it becomes a “stack” on the workbench, and you consolidate one stack at a time.",
    ["addSources.lead.more"] = "Folders you add go to the end of the scan queue. While scanning you can keep consolidating the stacks already found on the workbench.",
    ["addSources.found"] = "Common places found",
    ["addSources.choose"] = "Choose other folders…",
    ["addSources.pickerTitle"] = "Choose folders to add",
    ["addSources.chosen"] = "A folder you chose",
    ["addSources.options.title"] = "Applies to every source",
    ["addSources.options.skipRegenerable"] = "Skip regenerable folders",
    ["addSources.options.skipRegenerableHint"] = "node_modules, bin, obj, .vs, dist, __pycache__ and the like. Only their size is recorded; files aren't compared one by one.",
    ["addSources.options.skipCloud"] = "Don't download cloud-only files",
    ["addSources.options.skipCloudHint"] = "OneDrive placeholders only have their name and size recorded; they're never downloaded for comparison.",
    ["addSources.options.protect"] = "Make {path} protected",
    ["addSources.options.protectHint"] = "Protected files are never moved to the recycle area or overwritten.",
    ["addSources.options.later"] = "You can adjust each source later under Sources.",
    ["addSources.addAndScan.one"] = "Add {count} source and start scanning",
    ["addSources.addAndScan.other"] = "Add {count} sources and start scanning",
    ["addSources.addOnly"] = "Add only, scan later",
    ["addSources.selectFirst"] = "Select at least one location first",
    ["addSources.cancel"] = "Cancel",
    ["addSources.problems"] = "Some folders weren't added:",
    ["addSources.safe"] = "Adding sources doesn't move or change any file. Nothing is consolidated until you decide and confirm, stack by stack.",
    ["addSources.how.title"] = "What happens after you add them",
    ["addSources.how.discover"] = "Discover",
    ["addSources.how.discoverText"] = "Records each file's path, size and date. It's fast and can be paused at any time.",
    ["addSources.how.candidates"] = "Find possible copies",
    ["addSources.how.candidatesText"] = "Folders of similar structure and size, and files of the same size, are lined up for comparison first.",
    ["addSources.how.confirm"] = "Check contents",
    ["addSources.how.confirmText"] = "Content fingerprints are computed only for possible copies. The same file name doesn't count.",
    ["addSources.how.workbench"] = "Appear on the workbench",
    ["addSources.how.workbenchText"] = "Each stack can be consolidated as soon as it's confirmed; there's no need to wait for the whole scan.",
    ["addSources.stack.title"] = "What's a stack?",
    ["addSources.stack.text"] = "A few copies of the same thing scattered around. It may be a whole project folder or just one file. Here's an example of a stack:",
    ["addSources.stack.backupFolder"] = "E:\\Backup\\2023\\",
    ["addSources.stack.copyName"] = "invoice-tool - Copy",
    ["addSources.stack.after"] = "You pick one copy as the keeper, then choose to merge, keep only the keeper, or keep all. You handle just this one stack at a time.",
  };
  // i18n:end

  // i18n:zh-TW
  private static readonly Dictionary<string, string> AddSourcesChinese = new(StringComparer.Ordinal)
  {
    ["addSources.title.first"] = "先加入你常放東西的地方",
    ["addSources.title.more"] = "加入資料夾",
    ["addSources.lead.first"] = "不用一次掃完。加入的資料夾會在背景建立索引；同一份東西只要在兩個地方都被掃到，就會變成「一疊」出現在工作台，你可以一疊一疊整理。",
    ["addSources.lead.more"] = "加入的資料夾會排在掃描佇列最後。掃描時可以照常在工作台整理已經找到的疊。",
    ["addSources.found"] = "找到的常用位置",
    ["addSources.choose"] = "選擇其他資料夾…",
    ["addSources.pickerTitle"] = "選擇要加入的資料夾",
    ["addSources.chosen"] = "你選的資料夾",
    ["addSources.options.title"] = "每個來源都會套用",
    ["addSources.options.skipRegenerable"] = "略過可重建的資料夾",
    ["addSources.options.skipRegenerableHint"] = "node_modules、bin、obj、.vs、dist、__pycache__ 等。只記錄大小，不逐檔比對。",
    ["addSources.options.skipCloud"] = "不下載只在雲端的檔",
    ["addSources.options.skipCloudHint"] = "OneDrive 佔位檔只記錄名稱與大小，不會為了比對而下載。",
    ["addSources.options.protect"] = "把 {path} 設為受保護",
    ["addSources.options.protectHint"] = "受保護的檔案不會被移入回收區，也不會被覆寫。",
    ["addSources.options.later"] = "之後可以在「來源」裡逐一調整。",
    ["addSources.addAndScan.one"] = "加入 {count} 個來源並開始掃描",
    ["addSources.addAndScan.other"] = "加入 {count} 個來源並開始掃描",
    ["addSources.addOnly"] = "只加入，稍後再掃",
    ["addSources.selectFirst"] = "先勾選至少一個位置",
    ["addSources.cancel"] = "取消",
    ["addSources.problems"] = "有些資料夾沒有加入：",
    ["addSources.safe"] = "加入來源不會移動或修改任何檔案。整理要等你逐疊決定、確認之後才會執行。",
    ["addSources.how.title"] = "加入之後會發生什麼",
    ["addSources.how.discover"] = "探索",
    ["addSources.how.discoverText"] = "記錄每個檔的路徑、大小與日期。速度快，隨時可以暫停。",
    ["addSources.how.candidates"] = "找可能的複本",
    ["addSources.how.candidatesText"] = "結構與大小相近的資料夾、大小相同的檔，會先排進比對。",
    ["addSources.how.confirm"] = "確認內容",
    ["addSources.how.confirmText"] = "只對可能的複本計算內容指紋。檔名相同不算數。",
    ["addSources.how.workbench"] = "出現在工作台",
    ["addSources.how.workbenchText"] = "確認一疊就能整理一疊，不用等全部掃完。",
    ["addSources.stack.title"] = "什麼是一疊",
    ["addSources.stack.text"] = "同一份東西散在各處的幾個複本。可能是整個專案資料夾，也可能只是一個檔。下面是一疊的例子：",
    ["addSources.stack.backupFolder"] = "E:\\備份\\2023\\",
    ["addSources.stack.copyName"] = "invoice-tool - 複製",
    ["addSources.stack.after"] = "你決定其中一份當正本，再選擇合併、只留正本，或都保留。一次只處理這一疊。",
  };
  // i18n:end
}
