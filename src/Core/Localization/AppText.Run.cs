namespace FileWorkbench.Localization;

// Running the plan, retrying and restoring (PlanExecutor): why an item was skipped, failed or not restored. These
// sentences are written into the run journal and keep the language they were written in (docs/i18n.md).
public static partial class AppText
{
  // i18n:en
  private static readonly Dictionary<string, string> RunEnglish = new(StringComparer.Ordinal)
  {
    ["run.skip.canceled"] = "The run was canceled, so this item didn't run.",
    ["run.skip.unchecked"] = "You unchecked this item, so it didn't run this time.",
    ["run.skip.changed"] = "Changed after you decided.",
    ["run.skip.copyIncomplete"] = "Some files of this stack weren't copied into the keeper, so this copy wasn't moved to the recycle area; files that exist only in this copy would have gone with it.",
    ["run.skip.checkFailed"] = "Couldn't check the current state before running: {error}",
    ["run.retry.modified"] = "{path} was modified after the run, so it wasn't retried. Compare this stack again.",
    ["run.restore.occupied.one"] = "The original place of {count} file already has a file there, so that file stays in the recycle area.",
    ["run.restore.occupied.other"] = "The original places of {count} files already have files there, so those files stay in the recycle area.",
    ["run.restore.suffix"] = "(restored {date})",
    ["run.restore.sourceMissing"] = "The file this was copied from, {path}, isn't in its place now (it may still be in the recycle area). Restore that copy first, then this item.",
    ["run.restore.sourceReplaced"] = "{path} is no longer the file that was copied (it was modified or replaced later). Undoing this would leave the original version only in the recycle area, so it wasn't restored; handle it yourself if needed.",
    ["run.restore.createdChanged"] = "{path} was modified after the run, so it wasn't moved out; handle it yourself if needed.",
    ["run.copy.exists"] = "{path} already exists and won't be overwritten.",
    ["run.copy.mismatch"] = "The content didn't match after copying {path}, so this copy was abandoned.",
    ["run.replace.protected"] = "The keeper is protected and won't be overwritten.",
    ["run.replace.keeperInArea"] = "{error} The keeper's original version is in the recycle area; restoring this item puts it back.",
    ["run.recycle.copyMissing"] = "This copy is no longer in the stack.",
    ["run.recycle.protected"] = "Protected sources are never moved to the recycle area.",
    ["run.recycle.planMismatch"] = "The plan doesn't match this stack's content, so it didn't run.",
    ["run.recycle.gone"] = "This copy is no longer where it was; no files were moved.",
    ["run.recycle.keeperVanished"] = "The keeper disappeared when this copy was moved away, so they're the same place; the copy was put back right away.",
    ["run.separate.same"] = "This copy and the keeper may be the same place (another name for the same folder, or one inside the other), so nothing was moved.",
    ["run.separate.unknown"] = "Couldn't confirm that this copy and the keeper are different places (the check file can't be written here), so nothing was moved.",
    ["run.separate.appFolder"] = "This copy contains File Workbench's own folder, or that can't be ruled out, so nothing was moved.",
    ["run.separate.otherDrive"] = "This copy is actually on another drive (through a link, mount point or network path); nothing is moved across drives.",
    ["run.move.link"] = "{path} is a link (a junction or shortcut), so it wasn't moved.",
    ["run.move.newer"] = "{path} was added or modified after the run started, so it wasn't moved.",
    ["run.move.inUse"] = "{name} is in use by another program, can't be moved, or the destination already has a file with that name, so it's still in place.",
    ["run.require.inside"] = "{path} isn't inside the keeper {folder}, so it didn't run.",
    ["run.require.outsideArea"] = "{path} is in the recycle area; anything in the recycle area is never treated as a keeper or a copy.",
  };
  // i18n:end

  // i18n:zh-TW
  private static readonly Dictionary<string, string> RunChinese = new(StringComparer.Ordinal)
  {
    ["run.skip.canceled"] = "執行已取消，這一項沒有執行。",
    ["run.skip.unchecked"] = "你取消勾選了這一項，這次沒有執行。",
    ["run.skip.changed"] = "決定之後有變動",
    ["run.skip.copyIncomplete"] = "同一疊有檔還沒複製進正本，所以這份先不移入回收區，以免只在這份裡的檔跟著被移走。",
    ["run.skip.checkFailed"] = "執行前無法確認現況：{error}",
    ["run.retry.modified"] = "{path} 在整理之後被修改過，沒有重試。請重新比對這疊。",
    ["run.restore.occupied.one"] = "{count} 個檔的原位置已經有檔案，那些檔留在回收區。",
    ["run.restore.occupied.other"] = "{count} 個檔的原位置已經有檔案，那些檔留在回收區。",
    ["run.restore.suffix"] = "(還原 {date})",
    ["run.restore.sourceMissing"] = "這個檔的來源 {path} 現在不在原處（可能還在回收區）。先還原那一份，再還原這一項。",
    ["run.restore.sourceReplaced"] = "{path} 已經不是當初複製的那個檔（之後被修改或換成了別的檔）。撤銷會讓當初的版本只剩在回收區，所以沒有還原；需要的話請自己處理。",
    ["run.restore.createdChanged"] = "{path} 在整理之後被修改過，所以沒有移走；需要的話請自己處理。",
    ["run.copy.exists"] = "{path} 已經存在，不會覆寫。",
    ["run.copy.mismatch"] = "複製 {path} 之後內容不一致，已放棄這次複製。",
    ["run.replace.protected"] = "正本受保護，不會被覆寫。",
    ["run.replace.keeperInArea"] = "{error} 正本原本的版本在回收區，還原這一項可以放回去。",
    ["run.recycle.copyMissing"] = "這份已經不在這疊裡。",
    ["run.recycle.protected"] = "受保護的來源不會被移入回收區。",
    ["run.recycle.planMismatch"] = "計畫與這疊的內容不一致，沒有執行。",
    ["run.recycle.gone"] = "這份已經不在原位置，沒有移動任何檔案。",
    ["run.recycle.keeperVanished"] = "移走這份之後正本也不見了，表示兩者是同一個位置；已經立刻放回原處。",
    ["run.separate.same"] = "這份和正本可能是同一個位置（同一個資料夾的另一個名稱，或其中一個包含另一個），所以沒有移動。",
    ["run.separate.unknown"] = "無法確認這份和正本是不同的位置（這裡不能寫入確認用的暫存檔），所以沒有移動。",
    ["run.separate.appFolder"] = "這份裡面有 貓檔家 自己的資料夾，或無法確認沒有，所以沒有移動。",
    ["run.separate.otherDrive"] = "這份實際上在另一顆磁碟上（經由連結、掛載點或網路路徑），不會跨磁碟搬移。",
    ["run.move.link"] = "{path} 是連結（junction 或捷徑），沒有移動。",
    ["run.move.newer"] = "{path} 是整理開始之後才新增或修改的檔，沒有移動。",
    ["run.move.inUse"] = "{name} 正在被其他程式使用、無法移動，或目的地已經有同名的檔，所以還在原處。",
    ["run.require.inside"] = "{path} 不在正本 {folder} 裡，沒有執行。",
    ["run.require.outsideArea"] = "{path} 在回收區裡；回收區裡的東西不會被當成正本或複本處理。",
  };
  // i18n:end
}
