namespace FileWorkbench.Localization;

// The since-last-time screen: what changed while the app was away, the notices' actions and the open-stack count.
// The notices' own titles and descriptions come from Core.
public static partial class AppText
{
  // i18n:en
  private static readonly Dictionary<string, string> SinceEnglish = new(StringComparer.Ordinal)
  {
    ["since.none"] = "No new changes",
    ["since.kicker.one"] = "{day}, {days} day since you last consolidated",
    ["since.kicker.other"] = "{day}, {days} days since you last consolidated",
    ["since.title"] = "Since {day}",
    ["since.lead"] = "Files were added and removed in the meantime. First come the changes that need a look, then the ones just so you know; when you're done, go back to the workbench for the next stack.",
    ["since.demoNote"] = "Demo screen: sample data shows what you'd see coming back after a week; the dates are simulated.",
    ["since.section.attention"] = "Needs a look",
    ["since.section.info"] = "Just so you know",
    ["since.action.sameDrive"] = "Same drive, start updating",
    ["since.action.newSource"] = "No, treat it as a new source",
    ["since.action.openStack"] = "Open this stack",
    ["since.action.notInStack"] = "Doesn't belong to this stack",
    ["since.action.lookAgain"] = "Take another look",
    ["since.action.keepAll"] = "Still keep all",
    ["since.action.viewRecycle"] = "View the recycle area",
    ["since.back"] = "Back to the workbench for the next stack",
    ["since.open.one"] = "To consolidate: {count} stack",
    ["since.open.other"] = "To consolidate: {count} stacks",
    ["since.openAtStart.one"] = "You started last time with {count} stack.",
    ["since.openAtStart.other"] = "You started last time with {count} stacks.",
    ["since.keptAllNote"] = "Stacks marked “keep all” only get a reminder when they change; they don't go back to the list by themselves.",
  };
  // i18n:end

  // i18n:zh-TW
  private static readonly Dictionary<string, string> SinceChinese = new(StringComparer.Ordinal)
  {
    ["since.none"] = "沒有新的變化",
    ["since.kicker.one"] = "{day}，距離上次整理 {days} 天",
    ["since.kicker.other"] = "{day}，距離上次整理 {days} 天",
    ["since.title"] = "自 {day} 以來",
    ["since.lead"] = "檔案在這段時間有增有減。下面先列出需要你看一下的，再列出只是讓你知道的；處理完再回工作台繼續下一疊。",
    ["since.demoNote"] = "示範畫面：用模擬資料呈現一週後回來時會看到的內容，日期是模擬的。",
    ["since.section.attention"] = "需要你看一下",
    ["since.section.info"] = "只是讓你知道",
    ["since.action.sameDrive"] = "是同一顆，開始更新",
    ["since.action.newSource"] = "不是，當成新的來源",
    ["since.action.openStack"] = "打開這疊",
    ["since.action.notInStack"] = "不屬於這疊",
    ["since.action.lookAgain"] = "重新看看",
    ["since.action.keepAll"] = "維持都保留",
    ["since.action.viewRecycle"] = "檢視回收區",
    ["since.back"] = "回工作台，繼續下一疊",
    ["since.open.one"] = "待整理：{count} 疊",
    ["since.open.other"] = "待整理：{count} 疊",
    ["since.openAtStart.one"] = "上次開始時是 {count} 疊。",
    ["since.openAtStart.other"] = "上次開始時是 {count} 疊。",
    ["since.keptAllNote"] = "選擇「都保留」的疊有變動時只會提醒，不會自動放回待整理。",
  };
  // i18n:end
}
