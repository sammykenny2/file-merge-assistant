namespace FileMergeAssistant.Localization;

// The shared formats of the workbench screens (Gui Workbench Fmt): relative days, date patterns, and how lists,
// clauses and sentences are joined. The date patterns are .NET custom date and time format strings, read with
// AppText.Culture; the time of day stays HH:mm in both languages.
public static partial class AppText
{
  // i18n:en
  private static readonly Dictionary<string, string> FormatEnglish = new(StringComparer.Ordinal)
  {
    ["format.today"] = "Today {time}",
    ["format.yesterday"] = "Yesterday {time}",
    ["format.date.thisYear"] = "MMM d",
    ["format.date.otherYear"] = "MMM d, yyyy",
    ["format.dateTime"] = "{date}, {time}",
    ["format.listSeparator"] = ", ",
    ["format.clauses"] = "{first}; {second}",
    ["format.sentences"] = "{first} {second}",
  };
  // i18n:end

  // i18n:zh-TW
  private static readonly Dictionary<string, string> FormatChinese = new(StringComparer.Ordinal)
  {
    ["format.today"] = "今天 {time}",
    ["format.yesterday"] = "昨天 {time}",
    ["format.date.thisYear"] = "M/d",
    ["format.date.otherYear"] = "yyyy/MM/dd",
    ["format.dateTime"] = "{date} {time}",
    ["format.listSeparator"] = "、",
    ["format.clauses"] = "{first}；{second}",
    ["format.sentences"] = "{first}{second}",
  };
  // i18n:end
}
