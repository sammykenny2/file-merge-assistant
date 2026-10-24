namespace FileWorkbench.Localization;

// Stack detection (StackDetector): how the copies were found to be the same, why a copy is the suggested keeper, and the
// notes on a copy. The keeper's reasons are listed with the separator and the closing punctuation of each language.
public static partial class AppText
{
  // i18n:en
  private static readonly Dictionary<string, string> DetectEnglish = new(StringComparer.Ordinal)
  {
    ["detect.listSeparator"] = ", ",
    ["detect.note.project"] = "Code project",
    ["detect.sameLabel.file"] = "File content",
    ["detect.evidence.folder.one"] = "Compared by relative path and content fingerprint: {count} file matches in every copy, so they count as the same folder.",
    ["detect.evidence.folder.other"] = "Compared by relative path and content fingerprint: {count} files match in every copy, so they count as the same folder.",
    ["detect.evidence.folderRegenerable.one"] = "Compared by relative path and content fingerprint: {count} file matches in every copy, so they count as the same folder. Regenerable folders are compared by size only, not file by file.",
    ["detect.evidence.folderRegenerable.other"] = "Compared by relative path and content fingerprint: {count} files match in every copy, so they count as the same folder. Regenerable folders are compared by size only, not file by file.",
    ["detect.evidence.file"] = "Compared by content fingerprint: identical ({size} each). Different file names don't affect the result.",
    ["detect.keeper.protected"] = "In a protected location",
    ["detect.keeper.plainName"] = "No backup or copy marker in the name",
    ["detect.keeper.mostComplete"] = "Most complete",
    ["detect.keeper.mostRecent"] = "Most recently modified",
    ["detect.keeper.firstSource"] = "In the first source, with the same content as the other copies",
    ["detect.keeper.separator"] = ". ",
    ["detect.keeper.reasons"] = "{reasons}.",
  };
  // i18n:end

  // i18n:zh-TW
  private static readonly Dictionary<string, string> DetectChinese = new(StringComparer.Ordinal)
  {
    ["detect.listSeparator"] = "、",
    ["detect.note.project"] = "程式碼專案",
    ["detect.sameLabel.file"] = "檔案內容",
    ["detect.evidence.folder.one"] = "比對方式：{count} 個檔的相對路徑與內容指紋都相同，才判定為同一份資料夾。",
    ["detect.evidence.folder.other"] = "比對方式：{count} 個檔的相對路徑與內容指紋都相同，才判定為同一份資料夾。",
    ["detect.evidence.folderRegenerable.one"] = "比對方式：{count} 個檔的相對路徑與內容指紋都相同，才判定為同一份資料夾。可重建的資料夾只比大小，不逐檔比對。",
    ["detect.evidence.folderRegenerable.other"] = "比對方式：{count} 個檔的相對路徑與內容指紋都相同，才判定為同一份資料夾。可重建的資料夾只比大小，不逐檔比對。",
    ["detect.evidence.file"] = "比對方式：內容指紋相同（每份 {size}）。檔名不同不影響判定。",
    ["detect.keeper.protected"] = "在受保護的位置",
    ["detect.keeper.plainName"] = "名稱沒有備份或複製的字樣",
    ["detect.keeper.mostComplete"] = "檔案最完整",
    ["detect.keeper.mostRecent"] = "最近修改",
    ["detect.keeper.firstSource"] = "排在第一個來源，內容和其他份相同",
    ["detect.keeper.separator"] = "，",
    ["detect.keeper.reasons"] = "{reasons}。",
  };
  // i18n:end
}
