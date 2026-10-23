using System.Buffers;
using System.Globalization;
using System.IO.Hashing;
using System.Text;
using System.Text.RegularExpressions;
using FileMergeAssistant.Scanning;
using FileMergeAssistant.Sources;

namespace FileMergeAssistant.Plans;

/// <summary>
/// Where recycled items go (ADR-007): a recycle area on the same volume, so recycling is a rename and never copies data.
/// The volume that holds the app's data folder uses a folder there; every other volume uses a hidden
/// <c>.fma-recycle</c> folder at its root.
/// </summary>
public sealed class RecycleAreaLocator
{
  private readonly string _appDataRoot;

  public RecycleAreaLocator(string appDataRoot)
  {
    _appDataRoot = Path.GetFullPath(appDataRoot);
  }

  /// <summary>The app's data folder; never scanned and never a source of copies.</summary>
  public string AppDataRoot => _appDataRoot;

  public string AreaFor(string path)
  {
    var volume = Volume(path);
    return string.Equals(volume, Volume(_appDataRoot), StringComparison.OrdinalIgnoreCase)
      ? Path.Combine(_appDataRoot, "recycle-areas")
      : Path.Combine(volume, ScanFilter.RecycleFolderName);
  }

  /// <summary>True when <paramref name="path"/> is inside a recycle area this app manages.</summary>
  public bool IsInsideArea(string path)
  {
    var full = Path.GetFullPath(path);
    var area = AreaFor(full).TrimEnd('\\') + "\\";
    return full.StartsWith(area, StringComparison.OrdinalIgnoreCase)
      || full.Contains("\\" + ScanFilter.RecycleFolderName + "\\", StringComparison.OrdinalIgnoreCase);
  }

  public static string Volume(string path) => Path.GetPathRoot(Path.GetFullPath(path)) ?? string.Empty;
}

/// <summary>Outcome of a restore that could not put an item back because something is at its original place.</summary>
public sealed record RestoreOutcome(BatchItem Item, RestoreConflict? Conflict);

/// <summary>
/// Runs a pending plan (ADR-002, ADR-003, ADR-007). For each stack, copies into the keeper run first, each written to a
/// temporary file, verified against the source by fingerprint, and only then moved into place; existing files are never
/// overwritten. If any copy of a stack fails or changed, that stack's recycling does not run. Recycling moves a copy
/// into the recycle area of its own volume; a folder that cannot move as a whole moves file by file, and what is still
/// in use stays for a retry. Protected copies are never recycled or overwritten. Every item is re-checked just before it
/// runs and is journalled before it acts and after. Nothing is ever deleted except when the recycle areas are emptied,
/// and emptying never follows links and never touches a partly moved item.
/// </summary>
public sealed partial class PlanExecutor
{
  private readonly RecycleAreaLocator _areas;

  public PlanExecutor(RecycleAreaLocator areas)
  {
    _areas = areas;
  }

  /// <summary>Runs every enabled item of <paramref name="plan"/>.</summary>
  /// <param name="batchId">Id of this run; it names the run's folder in each recycle area.</param>
  /// <param name="ranAt">When the run started.</param>
  /// <param name="plan">The pending plan, already re-checked.</param>
  /// <param name="checkNow">Re-checks one item right before it runs; returns why it changed, or null.</param>
  /// <param name="record">Journals each item: once as <see cref="BatchItemState.Running"/> before it acts, then its result.</param>
  /// <param name="cancellationToken">Stops before the next item; finished items stay recorded.</param>
  public ExecutionBatch Execute(string batchId, DateTimeOffset ranAt, IReadOnlyList<PlanEntry> plan, Func<PlanEntry, PlanItem, string?> checkNow,
    Action<BatchItem> record, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(plan);
    ArgumentNullException.ThrowIfNull(checkNow);
    ArgumentNullException.ThrowIfNull(record);
    RequireBatchId(batchId);
    var results = new List<BatchItem>();
    void Add(BatchItem item)
    {
      results.Add(item);
      record(item);
    }

    var slot = 0;
    foreach (var entry in plan)
    {
      var copyIncomplete = false;
      // Every planned item gets a result, also the ones that do not run, so a stack only counts as done when all of it ran.
      foreach (var item in entry.Items.OrderBy(i => i.IsCopyLike ? 0 : 1))
      {
        if (cancellationToken.IsCancellationRequested)
        {
          Add(Skipped(entry, item, "執行已取消，這一項沒有執行。"));
          continue;
        }
        if (!item.Enabled)
        {
          Add(Skipped(entry, item, "你取消勾選了這一項，這次沒有執行。"));
          continue;
        }
        slot++;
        if (item.Check == PlanCheck.Changed)
        {
          Add(Skipped(entry, item, item.ChangeDetail ?? "決定之後有變動"));
          copyIncomplete |= item.IsCopyLike;
          continue;
        }
        if (!item.IsCopyLike && copyIncomplete)
        {
          Add(Skipped(entry, item, "同一疊有檔還沒複製進正本，所以這份先不移入回收區，以免只在這份裡的檔跟著被移走。"));
          continue;
        }
        string? changed;
        try
        {
          changed = checkNow(entry, item);
        }
#pragma warning disable CA1031 // Any failure to check means the item does not run.
        catch (Exception e)
#pragma warning restore CA1031
        {
          changed = $"執行前無法確認現況：{e.Message}";
        }
        if (changed is not null)
        {
          Add(Skipped(entry, item, changed));
          copyIncomplete |= item.IsCopyLike;
          continue;
        }

        BatchItem intent;
        BatchItem result;
        try
        {
          intent = Intent(entry, item, batchId, slot);
          record(intent);
          result = item.Kind switch
          {
            PlanItemKind.Copy or PlanItemKind.SaveAs => Copy(entry, intent),
            PlanItemKind.Replace => Replace(entry, intent),
            _ => Recycle(entry, item, intent),
          };
        }
#pragma warning disable CA1031 // Whatever goes wrong, the item is journalled as failed with its paths, never lost.
        catch (Exception e)
#pragma warning restore CA1031
        {
          result = Base(entry, item, BatchItemState.Failed) with { FailureReason = e.Message, FailedFiles = item.FileCount };
          if (TryIntent(entry, item, batchId, slot) is { } planned)
          {
            result = planned with { State = BatchItemState.Failed, FailureReason = e.Message, FailedFiles = item.FileCount };
            // Nothing reached the recycle area: there is nothing there to restore.
            if (result.RecycledPath is { } recycled && !File.Exists(recycled) && !Directory.Exists(recycled))
            {
              result = result with { RecycledPath = null };
            }
          }
        }
        Add(result);
        copyIncomplete |= item.IsCopyLike && result.State != BatchItemState.Done;
      }
    }
    return new ExecutionBatch(batchId, ranAt, results);
  }

  /// <summary>
  /// Retries a failed item: a copy runs again if its source is unchanged; a recycled file or a partly recycled folder moves
  /// only what is still exactly as it was when the run checked it, and only while it is provably not the keeper.
  /// </summary>
  public BatchItem Retry(string batchId, BatchItem item)
  {
    ArgumentNullException.ThrowIfNull(item);
    RequireBatchId(batchId);
    if (item.State != BatchItemState.Failed)
    {
      return item;
    }
    try
    {
      if (item.Kind is PlanItemKind.Copy or PlanItemKind.SaveAs && item.SourcePath is { } source && item.CreatedPath is { } created)
      {
        if (item.SourceStamp is not null && Stamp(source) != item.SourceStamp)
        {
          return item with { FailureReason = $"{source} 在整理之後被修改過，沒有重試。請重新比對這疊。" };
        }
        CopyVerified(source, created);
        return item with { State = BatchItemState.Done, FailureReason = null, FailedFiles = 0, Retried = true };
      }
      if (item.Kind == PlanItemKind.Recycle && item.OriginalPath is { } file && item.Manifest is null && File.Exists(file))
      {
        // A single file that could not move (in use): it moves now only if it is still exactly what was checked.
        if (item.SourceStamp is null || Stamp(file) != item.SourceStamp)
        {
          return item with { FailureReason = $"{file} 在整理之後被修改過，沒有重試。請重新比對這疊。" };
        }
        var target = item.RecycledPath ?? FreeName(Path.Combine(SlotFolder(file, batchId, "retry-" + Safe(item.Id)), Path.GetFileName(file)));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (item.KeeperPath is { } keeper)
        {
          RequireSeparate(file, keeper, Path.GetDirectoryName(target)!);
        }
        File.Move(file, target, overwrite: false);
        return item with
        {
          State = BatchItemState.Done, FailureReason = null, FailedFiles = 0, Retried = true, RecycledPath = target,
          RecycleVolume = RecycleAreaLocator.Volume(file),
        };
      }
      if (item.Kind == PlanItemKind.Recycle && item.OriginalPath is { } original && item.RecycledPath is { } recycled && item.Manifest is { } manifest)
      {
        if (item.KeeperPath is { } keeper && Directory.Exists(original))
        {
          RequireSeparate(original, keeper, Path.GetDirectoryName(recycled)!);
        }
        var allowed = ParseManifest(manifest);
        var failed = MoveFiles(original, recycled, allowed, out var reason);
        return failed == 0
          ? item with { State = BatchItemState.Done, FailureReason = null, FailedFiles = 0, Retried = true }
          : item with { FailedFiles = failed, FailureReason = reason };
      }
    }
    catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
    {
      return item with { FailureReason = e.Message };
    }
    return item;
  }

  /// <summary>
  /// Undoes one item: a recycled copy goes back to where it was (file by file for a partly moved one); a file this run
  /// created in the keeper goes to the recycle area if it is still exactly what was copied; a replaced keeper file gets
  /// its old version back. Nothing at the original place is ever overwritten.
  /// </summary>
  public RestoreOutcome Restore(string batchId, BatchItem item, DateTimeOffset now)
  {
    ArgumentNullException.ThrowIfNull(item);
    RequireBatchId(batchId);
    if (!item.CanRestore)
    {
      return new RestoreOutcome(item, null);
    }
    switch (item.Kind)
    {
      case PlanItemKind.Recycle when item.State == BatchItemState.Failed:
      {
        // A partly moved folder: put back what moved, file by file, never over something already there.
        var left = MoveFiles(item.RecycledPath!, item.OriginalPath!, null, out _);
        return left == 0
          ? new RestoreOutcome(item with { State = BatchItemState.Restored, FailureReason = null }, null)
          : new RestoreOutcome(item with { FailureReason = $"{left} 個檔的原位置已經有檔案，那些檔留在回收區。" }, null);
      }
      case PlanItemKind.Recycle:
      {
        var original = item.OriginalPath!;
        var recycled = item.RecycledPath!;
        if (File.Exists(original) || Directory.Exists(original))
        {
          return new RestoreOutcome(item, Conflict(batchId, item, original, recycled));
        }
        PutBack(recycled, original);
        return new RestoreOutcome(item with { State = BatchItemState.Restored }, null);
      }
      case PlanItemKind.Copy or PlanItemKind.SaveAs:
      {
        if (SourceMissing(item) is { } missing)
        {
          return new RestoreOutcome(item with { FailureReason = missing }, null);
        }
        if (!CreatedUnchanged(item, out var why))
        {
          return new RestoreOutcome(item with { FailureReason = why }, null);
        }
        var undone = RecycleCreated(batchId, item);
        return new RestoreOutcome(item with { State = BatchItemState.Restored, UndonePath = undone }, null);
      }
      default:
      {
        // Replace: the copied-in version goes to the recycle area, then the keeper's own version comes back.
        if (item.State == BatchItemState.Done && SourceMissing(item) is { } missing)
        {
          return new RestoreOutcome(item with { FailureReason = missing }, null);
        }
        if (item.State == BatchItemState.Done && !CreatedUnchanged(item, out var why))
        {
          return new RestoreOutcome(item with { FailureReason = why }, null);
        }
        var undone = item.State == BatchItemState.Done ? RecycleCreated(batchId, item) : null;
        if (item.RecycledPath is { } old && item.OriginalPath is { } original && File.Exists(old))
        {
          if (File.Exists(original))
          {
            return new RestoreOutcome(item with { UndonePath = undone }, Conflict(batchId, item, original, old));
          }
          PutBack(old, original);
        }
        return new RestoreOutcome(item with { State = BatchItemState.Restored, UndonePath = undone }, null);
      }
    }
  }

  /// <summary>Settles a restore conflict without overwriting what is at the original place.</summary>
  /// <returns>Where the item was put back, or null when it stays in the recycle area.</returns>
  public static string? Resolve(RestoreConflict conflict, BatchItem item, RestoreConflictChoice choice, string? folder, DateTimeOffset now)
  {
    ArgumentNullException.ThrowIfNull(conflict);
    ArgumentNullException.ThrowIfNull(item);
    var recycled = item.Kind is PlanItemKind.Recycle or PlanItemKind.Replace ? item.RecycledPath : null;
    if (choice == RestoreConflictChoice.KeepInRecycleArea || recycled is null || (!File.Exists(recycled) && !Directory.Exists(recycled)))
    {
      return null;
    }
    var name = Path.GetFileName(conflict.OriginalPath.TrimEnd('\\'));
    var target = choice == RestoreConflictChoice.OtherFolder
      ? FreeName(Path.Combine(folder ?? throw new ArgumentNullException(nameof(folder)), name))
      : FreeName(WithSuffix(conflict.OriginalPath, string.Create(CultureInfo.InvariantCulture, $" (還原 {now:MM-dd})")));
    PutBack(recycled, target);
    return target;
  }

  /// <summary>
  /// Permanently deletes what this batch's finished items left in the recycle areas. Partly moved or interrupted items
  /// are left alone (they can still be restored or retried); links are removed, never followed; only paths inside
  /// recycle areas are touched.
  /// </summary>
  /// <returns>The items whose recycle-area content is gone now, and whether nothing of the batch is left there.</returns>
  public (IReadOnlySet<string> Emptied, bool Complete) Empty(ExecutionBatch batch)
  {
    ArgumentNullException.ThrowIfNull(batch);
    RequireBatchId(batch.Id);
    var emptied = new HashSet<string>(StringComparer.Ordinal);
    var complete = true;
    foreach (var item in batch.Items)
    {
      if (item.State is BatchItemState.Failed or BatchItemState.Running && item.Kind is PlanItemKind.Recycle or PlanItemKind.Replace
        && item.RecycledPath is { } kept)
      {
        // Partly moved or interrupted: it can still be restored, so it is never deleted here. Only when nothing of it is
        // in the recycle area (and its drive is here to tell) does it not hold the batch back.
        complete &= !File.Exists(kept) && !Directory.Exists(kept) && Directory.Exists(Path.Combine(_areas.AreaFor(kept), batch.Id));
        continue;
      }
      var paths = new List<string>();
      if (item.State == BatchItemState.Done && item.Kind is PlanItemKind.Recycle or PlanItemKind.Replace && item.RecycledPath is { } recycled)
      {
        paths.Add(recycled);
      }
      if (item.UndonePath is { } undone)
      {
        paths.Add(undone);
      }
      try
      {
        var gone = true;
        foreach (var path in paths)
        {
          if (!File.Exists(path) && !Directory.Exists(path))
          {
            // Already gone only if this run's folder in that recycle area is here without it. An unplugged drive, or an
            // area that moved, keeps the item restorable until it is back.
            gone &= Directory.Exists(Path.Combine(_areas.AreaFor(path), batch.Id));
            continue;
          }
          if (!_areas.IsInsideArea(path) || !path.Contains("\\" + batch.Id + "\\", StringComparison.OrdinalIgnoreCase))
          {
            // Not where this app keeps this run (for example, the data folder moved): never deleted from here.
            gone = false;
            continue;
          }
          DeleteWithoutFollowingLinks(path);
        }
        if (!gone)
        {
          complete = false;
        }
        else if (paths.Count > 0)
        {
          emptied.Add(item.Id);
        }
      }
      catch (Exception e) when (e is IOException or UnauthorizedAccessException)
      {
        complete = false;
      }
    }
    foreach (var area in batch.Items.SelectMany(i => new[] { i.RecycledPath, i.UndonePath }).Where(p => p is not null)
      .Select(p => _areas.AreaFor(p!)).Distinct(StringComparer.OrdinalIgnoreCase))
    {
      RemoveEmptyFolders(Path.Combine(area, batch.Id));
    }
    return (emptied, complete);
  }

  /// <summary>Copies a file through a temporary file, verifies the bytes by fingerprint, then moves it into place.</summary>
  public static void CopyVerified(string source, string destination)
  {
    if (File.Exists(destination) || Directory.Exists(destination))
    {
      throw new IOException($"{destination} 已經存在，不會覆寫。");
    }
    var directory = Path.GetDirectoryName(destination);
    if (!string.IsNullOrEmpty(directory))
    {
      Directory.CreateDirectory(directory);
    }
    var partial = destination + ScanFilter.PartialSuffix;
    var created = false;
    var hasher = new XxHash128();
    var buffer = ArrayPool<byte>.Shared.Rent(1 << 20);
    DateTime modified;
    try
    {
      // FileShare.Read: nobody may write the source while it is being copied.
      using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan))
      // The partial file is this app's own (scans never index *.fma-partial); one left by an earlier attempt is replaced.
      using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
      {
        created = true;
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
          hasher.Append(buffer.AsSpan(0, read));
          output.Write(buffer, 0, read);
        }
        output.Flush(flushToDisk: true);
        modified = File.GetLastWriteTimeUtc(source);
      }

      var expected = SourceScanner.HashPrefix + Convert.ToHexStringLower(hasher.GetCurrentHash());
      if (SourceScanner.Fingerprint(partial, CancellationToken.None) != expected)
      {
        throw new IOException($"複製 {source} 之後內容不一致，已放棄這次複製。");
      }
      File.SetLastWriteTimeUtc(partial, modified);
      File.Move(partial, destination, overwrite: false);
      created = false;
    }
    finally
    {
      ArrayPool<byte>.Shared.Return(buffer);
      if (created)
      {
        TryDeletePartial(partial);
      }
    }
  }

  /// <summary>Size and time of a file as <c>size|ticks</c>.</summary>
  public static string Stamp(string path)
  {
    var file = new FileInfo(path);
    return file.Exists ? string.Create(CultureInfo.InvariantCulture, $"{file.Length}|{file.LastWriteTimeUtc.Ticks}") : "missing";
  }

  private BatchItem Intent(PlanEntry entry, PlanItem item, string batchId, int slot)
  {
    var intent = Base(entry, item, BatchItemState.Running);
    RequireOutsideArea(entry.Keeper.Path);
    RequireOutsideArea(item.SourcePath);
    switch (item.Kind)
    {
      case PlanItemKind.Copy or PlanItemKind.SaveAs:
        return intent with { SourceStamp = Stamp(item.SourcePath) };
      case PlanItemKind.Replace:
        return intent with
        {
          SourceStamp = Stamp(item.SourcePath),
          OriginalPath = item.DestinationPath,
          RecycledPath = Path.Combine(SlotFolder(item.DestinationPath, batchId, slot), Path.GetFileName(item.DestinationPath)),
          RecycleVolume = RecycleAreaLocator.Volume(item.DestinationPath),
        };
      default:
        return intent with
        {
          OriginalPath = item.SourcePath,
          RecycledPath = Path.Combine(SlotFolder(item.SourcePath, batchId, slot), Path.GetFileName(item.SourcePath.TrimEnd('\\'))),
          SourceStamp = File.Exists(item.SourcePath) ? Stamp(item.SourcePath) : null,
        };
    }
  }

  private BatchItem? TryIntent(PlanEntry entry, PlanItem item, string batchId, int slot)
  {
    try
    {
      return Intent(entry, item, batchId, slot);
    }
#pragma warning disable CA1031 // Best effort: a failed item keeps whatever paths can still be worked out.
    catch (Exception)
#pragma warning restore CA1031
    {
      return null;
    }
  }

  private static BatchItem Copy(PlanEntry entry, BatchItem intent)
  {
    RequireInside(intent.CreatedPath!, entry.Keeper.Path);
    CopyVerified(intent.SourcePath!, intent.CreatedPath!);
    return intent with { State = BatchItemState.Done };
  }

  private static BatchItem Replace(PlanEntry entry, BatchItem intent)
  {
    if (entry.Keeper.IsProtected)
    {
      throw new InvalidOperationException("正本受保護，不會被覆寫。");
    }
    var destination = intent.CreatedPath!;
    RequireInside(destination, entry.Keeper.Path);
    var old = intent.RecycledPath!;
    File.Move(destination, old, overwrite: false);
    try
    {
      CopyVerified(intent.SourcePath!, destination);
    }
    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
    {
      try
      {
        File.Move(old, destination, overwrite: false);
        return intent with { State = BatchItemState.Failed, FailureReason = e.Message, RecycledPath = null, OriginalPath = null };
      }
      catch (Exception back) when (back is IOException or UnauthorizedAccessException)
      {
        // The keeper's own version stays recorded in the recycle area; restoring this item puts it back.
        return intent with { State = BatchItemState.Failed, FailureReason = $"{e.Message} 正本原本的版本在回收區，還原這一項可以放回去。" };
      }
    }
    return intent with { State = BatchItemState.Done };
  }

  private BatchItem Recycle(PlanEntry entry, PlanItem item, BatchItem intent)
  {
    var copy = entry.Stack.Copies.FirstOrDefault(c => c.Id == item.CopyId)
      ?? throw new InvalidOperationException("這份已經不在這疊裡。");
    if (copy.IsProtected)
    {
      throw new InvalidOperationException("受保護的來源不會被移入回收區。");
    }
    if (copy.Id == entry.Keeper.Id || !string.Equals(copy.Path, item.SourcePath, StringComparison.OrdinalIgnoreCase))
    {
      throw new InvalidOperationException("計畫與這疊的內容不一致，沒有執行。");
    }
    var target = intent.RecycledPath!;
    if (!File.Exists(copy.Path) && !Directory.Exists(copy.Path))
    {
      return intent with { State = BatchItemState.Failed, FailureReason = "這份已經不在原位置，沒有移動任何檔案。", RecycledPath = null };
    }
    RequireSeparate(copy.Path, entry.Keeper.Path, Path.GetDirectoryName(target)!);
    if (File.Exists(copy.Path))
    {
      File.Move(copy.Path, target, overwrite: false);
      return KeeperStillThere(entry, intent with { State = BatchItemState.Done }, copy.Path, target);
    }
    try
    {
      Directory.Move(copy.Path, target);
      return KeeperStillThere(entry, intent with { State = BatchItemState.Done }, copy.Path, target);
    }
    catch (IOException)
    {
      // Something inside is in use: move what can move, leave the rest for a retry.
    }
    catch (UnauthorizedAccessException)
    {
      // Same as above.
    }
    var manifest = BuildManifest(copy.Path);
    var failed = MoveFiles(copy.Path, target, ParseManifest(manifest), out var reason);
    return failed == 0
      ? intent with { State = BatchItemState.Done }
      : intent with { State = BatchItemState.Failed, FailedFiles = failed, FailureReason = reason, Manifest = manifest };
  }

  /// <summary>
  /// Refuses to recycle unless the copy and the keeper are provably different places, by volume serial and file id rather
  /// than by name: a UNC path (<c>\\server\share</c>, <c>\\localhost\C$</c>), junction or mapped drive can name the keeper a
  /// second time. Also refuses a copy holding the app's own data folder, and a move to another volume.
  /// </summary>
  private void RequireSeparate(string copy, string keeper, string slotFolder)
  {
    if (PathIdentity.SameOrInside(keeper, copy) is not false || PathIdentity.SameOrInside(copy, keeper) is not false)
    {
      throw new InvalidOperationException("無法確認這份和正本是不同的位置（可能是同一個資料夾的另一個名稱，或其中一個包含另一個），沒有移動。");
    }
    if (PathIdentity.SameOrInside(_areas.AppDataRoot, copy) is not false)
    {
      throw new InvalidOperationException("這份裡面有 File Merge Assistant 自己的資料夾，或無法確認沒有，所以沒有移動。");
    }
    if (PathIdentity.Identify(copy) is not { } from || PathIdentity.Identify(slotFolder) is not { } to || from.VolumeSerial != to.VolumeSerial)
    {
      throw new InvalidOperationException("這份實際上在另一顆磁碟上（經由連結、掛載點或網路路徑），不會跨磁碟搬移。");
    }
  }

  /// <summary>
  /// Backstop after a move: if the keeper vanished with it, the two were one place under different names. The move is
  /// undone at once.
  /// </summary>
  private static BatchItem KeeperStillThere(PlanEntry entry, BatchItem done, string original, string target)
  {
    if (File.Exists(entry.Keeper.Path) || Directory.Exists(entry.Keeper.Path))
    {
      return done;
    }
    PutBack(target, original);
    return done with
    {
      State = BatchItemState.Failed,
      FailureReason = "移走這份之後正本也不見了，表示兩者是同一個位置；已經立刻放回原處。",
      RecycledPath = null,
      FailedFiles = done.FileCount,
    };
  }

  /// <summary>
  /// Undoing a copy moves the copied file out of the keeper. That only happens while the file it came from is in place;
  /// otherwise (its copy is in a recycle area, from this run or another, or gone) the copied file would be left only in a
  /// recycle area, to be lost when that is emptied.
  /// </summary>
  private static string? SourceMissing(BatchItem item) =>
    item.SourcePath is { } source && !File.Exists(source)
      ? $"這個檔的來源 {source} 現在不在原處（可能還在回收區）。先還原那一份，再還原這一項。"
      : null;

  /// <summary>Every file under a folder as <c>relative|size|ticks</c> lines, without following links.</summary>
  private static string BuildManifest(string folder)
  {
    var lines = new StringBuilder();
    foreach (var (relative, file) in WalkFiles(folder))
    {
      lines.Append(relative).Append('|').Append(file.Length.ToString(CultureInfo.InvariantCulture)).Append('|')
        .Append(file.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture)).Append('\n');
    }
    return lines.ToString();
  }

  private static Dictionary<string, string> ParseManifest(string manifest)
  {
    var entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var line in manifest.Split('\n', StringSplitOptions.RemoveEmptyEntries))
    {
      var cut = line.IndexOf('|', StringComparison.Ordinal);
      entries[line[..cut]] = line[(cut + 1)..];
    }
    return entries;
  }

  /// <summary>
  /// Moves files under <paramref name="from"/> to the same relative place under <paramref name="to"/>, never overwriting
  /// and never following links. With <paramref name="allowed"/>, only files listed there with the same size and time move.
  /// </summary>
  /// <returns>How many files (and links) stayed behind.</returns>
  private static int MoveFiles(string from, string to, Dictionary<string, string>? allowed, out string? reason)
  {
    reason = null;
    if (!Directory.Exists(from))
    {
      return 0;
    }
    var left = 0;
    foreach (var link in WalkLinks(from))
    {
      left++;
      reason ??= $"{link} 是連結（junction 或捷徑），沒有移動。";
    }
    foreach (var (relative, file) in WalkFiles(from).ToList())
    {
      if (allowed is not null && (!allowed.TryGetValue(relative, out var stamp)
        || stamp != string.Create(CultureInfo.InvariantCulture, $"{file.Length}|{file.LastWriteTimeUtc.Ticks}")))
      {
        left++;
        reason ??= $"{relative} 是整理開始之後才新增或修改的檔，沒有移動。";
        continue;
      }
      var target = Path.Combine(to, relative);
      try
      {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Move(file.FullName, target, overwrite: false);
      }
      catch (Exception e) when (e is IOException or UnauthorizedAccessException)
      {
        left++;
        reason ??= $"{Path.GetFileName(relative)} 正在被其他程式使用、無法移動，或目的地已經有同名的檔，所以還在原處。";
      }
    }
    RemoveEmptyFolders(from);
    return left;
  }

  /// <summary>Files under a folder with their relative paths; links (junctions, symbolic links) are not entered.</summary>
  private static IEnumerable<(string Relative, FileInfo File)> WalkFiles(string root)
  {
    var pending = new Stack<string>();
    pending.Push(string.Empty);
    while (pending.Count > 0)
    {
      var relative = pending.Pop();
      var folder = relative.Length == 0 ? root : Path.Combine(root, relative);
      foreach (var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos().ToList())
      {
        var child = relative.Length == 0 ? entry.Name : Path.Combine(relative, entry.Name);
        if (ScanFilter.IsLink(entry))
        {
          continue;
        }
        if (entry is DirectoryInfo)
        {
          pending.Push(child);
        }
        else
        {
          yield return (child, (FileInfo)entry);
        }
      }
    }
  }

  private static IEnumerable<string> WalkLinks(string root)
  {
    var pending = new Stack<string>();
    pending.Push(root);
    while (pending.Count > 0)
    {
      foreach (var entry in new DirectoryInfo(pending.Pop()).EnumerateFileSystemInfos().ToList())
      {
        if (ScanFilter.IsLink(entry))
        {
          yield return entry.FullName;
        }
        else if (entry is DirectoryInfo)
        {
          pending.Push(entry.FullName);
        }
      }
    }
  }

  /// <summary>Removes folders left empty, deepest first, never following links.</summary>
  private static void RemoveEmptyFolders(string root)
  {
    if (!Directory.Exists(root))
    {
      return;
    }
    var folders = new List<string> { root };
    var pending = new Stack<string>();
    pending.Push(root);
    while (pending.Count > 0)
    {
      foreach (var directory in new DirectoryInfo(pending.Pop()).EnumerateDirectories())
      {
        if (!ScanFilter.IsLink(directory))
        {
          folders.Add(directory.FullName);
          pending.Push(directory.FullName);
        }
      }
    }
    foreach (var folder in folders.OrderByDescending(f => f.Length))
    {
      try
      {
        if (!Directory.EnumerateFileSystemEntries(folder).Any())
        {
          Directory.Delete(folder);
        }
      }
      catch (Exception e) when (e is IOException or UnauthorizedAccessException)
      {
        // A folder in use or not empty stays.
      }
    }
  }

  /// <summary>Deletes a file or folder tree; a link inside is removed as a link, its target is never entered or changed.</summary>
  private static void DeleteWithoutFollowingLinks(string path)
  {
    var info = File.Exists(path) ? (FileSystemInfo)new FileInfo(path) : new DirectoryInfo(path);
    if (!info.Exists)
    {
      return;
    }
    if (ScanFilter.IsLink(info) || info is FileInfo)
    {
      if (info is FileInfo && !ScanFilter.IsLink(info))
      {
        info.Attributes = FileAttributes.Normal;
      }
      if (info is DirectoryInfo)
      {
        Directory.Delete(path, recursive: false);
      }
      else
      {
        File.Delete(path);
      }
      return;
    }
    foreach (var entry in ((DirectoryInfo)info).EnumerateFileSystemInfos().ToList())
    {
      DeleteWithoutFollowingLinks(entry.FullName);
    }
    info.Attributes = FileAttributes.Directory;
    Directory.Delete(path, recursive: false);
  }

  private static bool CreatedUnchanged(BatchItem item, out string? why)
  {
    why = null;
    if (item.CreatedPath is not { } created || !File.Exists(created) || item.SourceStamp is null)
    {
      return true;
    }
    if (Stamp(created) == item.SourceStamp)
    {
      return true;
    }
    why = $"{created} 在整理之後被修改過，所以沒有移走；需要的話請自己處理。";
    return false;
  }

  private string? RecycleCreated(string batchId, BatchItem item)
  {
    if (item.CreatedPath is not { } created || !File.Exists(created))
    {
      return null;
    }
    var folder = SlotFolder(created, batchId, "restore-" + Safe(item.Id));
    var target = FreeName(Path.Combine(folder, Path.GetFileName(created)));
    File.Move(created, target, overwrite: false);
    return target;
  }

  /// <summary>Item ids contain ':' and '#', which are not allowed (or not wise) in folder names.</summary>
  private static string Safe(string id) => new(id.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray());

  private static void PutBack(string recycled, string original)
  {
    var parent = Path.GetDirectoryName(original);
    if (!string.IsNullOrEmpty(parent))
    {
      Directory.CreateDirectory(parent);
    }
    if (File.Exists(recycled))
    {
      File.Move(recycled, original, overwrite: false);
    }
    else
    {
      Directory.Move(recycled, original);
    }
  }

  private static RestoreConflict Conflict(string batchId, BatchItem item, string original, string recycled)
  {
    var existing = new FileInfo(original);
    var current = existing.Exists ? existing.Length : 0;
    var created = existing.Exists ? existing.CreationTime : Directory.GetCreationTime(original);
    var recycledInfo = new FileInfo(recycled);
    return new RestoreConflict($"{batchId}:{item.Id}", batchId, item.Id, original,
      recycledInfo.Exists ? new DateTimeOffset(recycledInfo.CreationTime) : DateTimeOffset.Now,
      recycledInfo.Exists ? recycledInfo.Length : item.Bytes,
      recycledInfo.Exists ? new DateTimeOffset(recycledInfo.LastWriteTime) : DateTimeOffset.Now,
      new DateTimeOffset(created), current);
  }

  private string SlotFolder(string path, string batchId, int slot) =>
    SlotFolder(path, batchId, slot.ToString("000", CultureInfo.InvariantCulture));

  private string SlotFolder(string path, string batchId, string slot)
  {
    var area = _areas.AreaFor(path);
    var created = !Directory.Exists(area);
    var folder = Path.Combine(area, batchId, slot);
    Directory.CreateDirectory(folder);
    if (created && Path.GetFileName(area) == ScanFilter.RecycleFolderName)
    {
      File.SetAttributes(area, File.GetAttributes(area) | FileAttributes.Hidden);
    }
    return folder;
  }

  private static BatchItem Base(PlanEntry entry, PlanItem item, BatchItemState state) =>
    new(item.Id, entry.Stack.Id, item.Kind, item.Target, state)
    {
      Detail = item.Kind == PlanItemKind.Recycle ? string.Empty : entry.Keeper.Path,
      KeeperPath = entry.Keeper.Path,
      Bytes = item.Bytes,
      FileCount = item.FileCount,
      SourcePath = item.IsCopyLike ? item.SourcePath : null,
      CreatedPath = item.IsCopyLike ? item.DestinationPath : null,
      OriginalPath = item.Kind == PlanItemKind.Recycle ? item.SourcePath : null,
      RecycleVolume = item.Kind == PlanItemKind.Recycle ? RecycleAreaLocator.Volume(item.SourcePath) : null,
    };

  private static BatchItem Skipped(PlanEntry entry, PlanItem item, string reason) =>
    Base(entry, item, BatchItemState.Skipped) with { FailureReason = reason, CreatedPath = null };

  private static void RequireInside(string path, string folder)
  {
    var root = Path.GetFullPath(folder).TrimEnd('\\') + "\\";
    if (!Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase))
    {
      throw new InvalidOperationException($"{path} 不在正本 {folder} 裡，沒有執行。");
    }
  }

  private void RequireOutsideArea(string path)
  {
    if (_areas.IsInsideArea(path))
    {
      throw new InvalidOperationException($"{path} 在回收區裡；回收區裡的東西不會被當成正本或複本處理。");
    }
  }

  private static void RequireBatchId(string batchId)
  {
    if (!BatchIdPattern().IsMatch(batchId))
    {
      throw new ArgumentException($"'{batchId}' is not a run id.", nameof(batchId));
    }
  }

  [GeneratedRegex(@"^b\d{8}-\d{6}-[0-9a-f]{4}$|^b[0-9a-z-]{1,40}$", RegexOptions.CultureInvariant)]
  private static partial Regex BatchIdPattern();

  private static string WithSuffix(string path, string suffix)
  {
    var trimmed = path.TrimEnd('\\');
    if (Directory.Exists(trimmed))
    {
      return trimmed + suffix;
    }
    var extension = Path.GetExtension(trimmed);
    var stem = extension.Length > 0 && Path.GetFileName(trimmed).Length > extension.Length ? trimmed[..^extension.Length] : trimmed;
    return extension.Length > 0 && stem.Length < trimmed.Length ? stem + suffix + extension : trimmed + suffix;
  }

  private static string FreeName(string path)
  {
    if (!File.Exists(path) && !Directory.Exists(path))
    {
      return path;
    }
    for (var n = 2; ; n++)
    {
      var candidate = WithSuffix(path, string.Create(CultureInfo.InvariantCulture, $" ({n})"));
      if (!File.Exists(candidate) && !Directory.Exists(candidate))
      {
        return candidate;
      }
    }
  }

  private static void TryDeletePartial(string partial)
  {
    try
    {
      if (File.Exists(partial))
      {
        File.Delete(partial);
      }
    }
    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
    {
      // Our own partial file; scans never index *.fma-partial, and the next attempt overwrites it.
    }
  }
}
