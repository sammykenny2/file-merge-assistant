using System.Buffers;
using System.Globalization;
using System.IO.Hashing;
using FileMergeAssistant.Scanning;
using FileMergeAssistant.Stacks;

namespace FileMergeAssistant.Plans;

/// <summary>
/// Where recycled items go (ADR-007): a recycle area on the same volume, so recycling is a rename and never copies data.
/// The volume that holds the app's data folder uses a folder there; every other volume uses a hidden
/// <c>.fma-recycle</c> folder at its root.
/// </summary>
public sealed class RecycleAreaLocator
{
  public const string VolumeFolderName = ".fma-recycle";

  private readonly string _appDataRoot;

  public RecycleAreaLocator(string appDataRoot)
  {
    _appDataRoot = Path.GetFullPath(appDataRoot);
  }

  public string AreaFor(string path)
  {
    var volume = Volume(path);
    return string.Equals(volume, Volume(_appDataRoot), StringComparison.OrdinalIgnoreCase)
      ? Path.Combine(_appDataRoot, "recycle-areas")
      : Path.Combine(volume, VolumeFolderName);
  }

  /// <summary>True when <paramref name="path"/> is inside a recycle area this app manages.</summary>
  public bool IsInsideArea(string path)
  {
    var full = Path.GetFullPath(path);
    var area = AreaFor(full).TrimEnd('\\') + "\\";
    return full.StartsWith(area, StringComparison.OrdinalIgnoreCase);
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
/// runs. Nothing is ever deleted except when the recycle areas are emptied.
/// </summary>
public sealed class PlanExecutor
{
  private const string PartialSuffix = ".fma-partial";

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
  /// <param name="cancellationToken">Stops before the next item; finished items stay recorded.</param>
  public ExecutionBatch Execute(string batchId, DateTimeOffset ranAt, IReadOnlyList<PlanEntry> plan, Func<PlanEntry, PlanItem, string?> checkNow,
    CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(plan);
    ArgumentNullException.ThrowIfNull(checkNow);
    var results = new List<BatchItem>();
    var slot = 0;
    foreach (var entry in plan)
    {
      var copyIncomplete = false;
      foreach (var item in entry.Items.Where(i => i.Enabled).OrderBy(i => i.IsCopyLike ? 0 : 1))
      {
        if (cancellationToken.IsCancellationRequested)
        {
          return new ExecutionBatch(batchId, ranAt, results);
        }
        slot++;
        if (item.Check == PlanCheck.Changed)
        {
          results.Add(Skipped(entry, item, item.ChangeDetail ?? "決定之後有變動"));
          copyIncomplete |= item.IsCopyLike;
          continue;
        }
        if (!item.IsCopyLike && copyIncomplete)
        {
          results.Add(Skipped(entry, item, "同一疊有檔還沒複製進正本，所以這份先不移入回收區，以免只在這份裡的檔跟著被移走。"));
          continue;
        }
        var changed = checkNow(entry, item);
        if (changed is not null)
        {
          results.Add(Skipped(entry, item, changed));
          copyIncomplete |= item.IsCopyLike;
          continue;
        }

        BatchItem result;
        try
        {
          result = item.Kind switch
          {
            PlanItemKind.Copy or PlanItemKind.SaveAs => Copy(entry, item),
            PlanItemKind.Replace => Replace(entry, item, batchId, slot),
            _ => Recycle(entry, item, batchId, slot),
          };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
          result = Base(entry, item, BatchItemState.Failed) with { FailureReason = e.Message, FailedFiles = item.FileCount };
        }
        results.Add(result);
        copyIncomplete |= item.IsCopyLike && result.State != BatchItemState.Done;
      }
    }
    return new ExecutionBatch(batchId, ranAt, results);
  }

  /// <summary>Retries a failed item: a copy is attempted again; a partly recycled folder moves its remaining files.</summary>
  public static BatchItem Retry(BatchItem item)
  {
    ArgumentNullException.ThrowIfNull(item);
    if (item.State != BatchItemState.Failed)
    {
      return item;
    }
    try
    {
      if (item.Kind is PlanItemKind.Copy or PlanItemKind.SaveAs && item.SourcePath is { } source && item.CreatedPath is { } created)
      {
        CopyVerified(source, created);
        return item with { State = BatchItemState.Done, FailureReason = null, FailedFiles = 0, Retried = true };
      }
      if (item.Kind == PlanItemKind.Recycle && item.OriginalPath is { } original && item.RecycledPath is { } recycled)
      {
        var failed = MoveRemaining(original, recycled, out var reason);
        return failed == 0
          ? item with { State = BatchItemState.Done, FailureReason = null, FailedFiles = 0, Retried = true }
          : item with { FailedFiles = failed, FailureReason = reason };
      }
    }
    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
    {
      return item with { FailureReason = e.Message };
    }
    return item;
  }

  /// <summary>
  /// Undoes one item: a recycled copy goes back to where it was; a file this run created in the keeper goes to the recycle
  /// area; a replaced keeper file gets its old version back. Nothing at the original place is ever overwritten.
  /// </summary>
  public RestoreOutcome Restore(string batchId, BatchItem item, DateTimeOffset now)
  {
    ArgumentNullException.ThrowIfNull(item);
    if (item.State != BatchItemState.Done)
    {
      return new RestoreOutcome(item, null);
    }
    switch (item.Kind)
    {
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
        RecycleCreated(batchId, item);
        return new RestoreOutcome(item with { State = BatchItemState.Restored }, null);
      default:
      {
        // Replace: the copied-in version goes to the recycle area, then the keeper's own version comes back.
        RecycleCreated(batchId, item);
        if (item.RecycledPath is { } old && item.OriginalPath is { } original)
        {
          if (File.Exists(original))
          {
            return new RestoreOutcome(item, Conflict(batchId, item, original, old));
          }
          PutBack(old, original);
        }
        return new RestoreOutcome(item with { State = BatchItemState.Restored }, null);
      }
    }
  }

  /// <summary>Settles a restore conflict without overwriting what is at the original place.</summary>
  /// <returns>Where the item was put back, or null when it stays in the recycle area.</returns>
  public static string? Resolve(RestoreConflict conflict, BatchItem item, RestoreConflictChoice choice, string? folder, DateTimeOffset now)
  {
    ArgumentNullException.ThrowIfNull(conflict);
    ArgumentNullException.ThrowIfNull(item);
    var recycled = item.Kind == PlanItemKind.Recycle || item.Kind == PlanItemKind.Replace ? item.RecycledPath : null;
    if (choice == RestoreConflictChoice.KeepInRecycleArea || recycled is null)
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

  /// <summary>Permanently deletes what this batch left in the recycle areas. Only paths inside recycle areas are touched.</summary>
  public void Empty(ExecutionBatch batch)
  {
    ArgumentNullException.ThrowIfNull(batch);
    foreach (var path in batch.Items.Select(i => i.RecycledPath).Where(p => p is not null).Select(p => p!))
    {
      if (!_areas.IsInsideArea(path))
      {
        continue;
      }
      DeleteTree(path);
    }
    foreach (var volume in batch.Items.Select(i => i.OriginalPath ?? i.CreatedPath).Where(p => p is not null).Select(p => RecycleAreaLocator.Volume(p!)).Distinct(StringComparer.OrdinalIgnoreCase))
    {
      var folder = Path.Combine(_areas.AreaFor(volume), batch.Id);
      if (Directory.Exists(folder))
      {
        DeleteTree(folder);
      }
    }
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
    var partial = destination + PartialSuffix;
    var hasher = new XxHash128();
    var buffer = ArrayPool<byte>.Shared.Rent(1 << 20);
    DateTime modified;
    try
    {
      // FileShare.Read: nobody may write the source while it is being copied.
      using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan))
      using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16))
      {
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
          hasher.Append(buffer.AsSpan(0, read));
          output.Write(buffer, 0, read);
        }
        output.Flush(flushToDisk: true);
        modified = File.GetLastWriteTimeUtc(source);
      }
    }
    catch
    {
      TryDeletePartial(partial);
      throw;
    }
    finally
    {
      ArrayPool<byte>.Shared.Return(buffer);
    }

    var expected = SourceScanner.HashPrefix + Convert.ToHexStringLower(hasher.GetCurrentHash());
    if (SourceScanner.Fingerprint(partial, CancellationToken.None) != expected)
    {
      TryDeletePartial(partial);
      throw new IOException($"複製 {source} 之後內容不一致，已放棄這次複製。");
    }
    File.SetLastWriteTimeUtc(partial, modified);
    File.Move(partial, destination, overwrite: false);
  }

  private static BatchItem Copy(PlanEntry entry, PlanItem item)
  {
    RequireInside(item.DestinationPath, entry.Keeper.Path);
    CopyVerified(item.SourcePath, item.DestinationPath);
    return Base(entry, item, BatchItemState.Done) with { CreatedPath = item.DestinationPath };
  }

  private BatchItem Replace(PlanEntry entry, PlanItem item, string batchId, int slot)
  {
    if (entry.Keeper.IsProtected)
    {
      throw new InvalidOperationException("正本受保護，不會被覆寫。");
    }
    RequireInside(item.DestinationPath, entry.Keeper.Path);
    var folder = SlotFolder(item.DestinationPath, batchId, slot);
    var old = Path.Combine(folder, Path.GetFileName(item.DestinationPath));
    File.Move(item.DestinationPath, old, overwrite: false);
    try
    {
      CopyVerified(item.SourcePath, item.DestinationPath);
    }
    catch
    {
      // Put the keeper's own version back before reporting the failure.
      File.Move(old, item.DestinationPath, overwrite: false);
      throw;
    }
    return Base(entry, item, BatchItemState.Done) with
    {
      CreatedPath = item.DestinationPath,
      OriginalPath = item.DestinationPath,
      RecycledPath = old,
      RecycleVolume = RecycleAreaLocator.Volume(item.DestinationPath),
    };
  }

  private BatchItem Recycle(PlanEntry entry, PlanItem item, string batchId, int slot)
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
    var target = Path.Combine(SlotFolder(copy.Path, batchId, slot), Path.GetFileName(copy.Path.TrimEnd('\\')));
    var result = Base(entry, item, BatchItemState.Done) with
    {
      OriginalPath = copy.Path,
      RecycledPath = target,
      RecycleVolume = RecycleAreaLocator.Volume(copy.Path),
    };
    if (File.Exists(copy.Path))
    {
      File.Move(copy.Path, target, overwrite: false);
      return result;
    }
    try
    {
      Directory.Move(copy.Path, target);
      return result;
    }
    catch (IOException)
    {
      // Something inside is in use: move what can move, leave the rest for a retry.
    }
    catch (UnauthorizedAccessException)
    {
      // Same as above.
    }
    var failed = MoveRemaining(copy.Path, target, out var reason);
    return failed == 0 ? result : result with { State = BatchItemState.Failed, FailedFiles = failed, FailureReason = reason };
  }

  /// <summary>Moves every file still under <paramref name="from"/> to the same relative place under <paramref name="to"/>.</summary>
  /// <returns>How many files could not move.</returns>
  private static int MoveRemaining(string from, string to, out string? reason)
  {
    reason = null;
    if (!Directory.Exists(from))
    {
      return 0;
    }
    var failed = 0;
    var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false };
    foreach (var file in Directory.EnumerateFiles(from, "*", options).ToList())
    {
      var target = Path.Combine(to, Path.GetRelativePath(from, file));
      try
      {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Move(file, target, overwrite: false);
      }
      catch (Exception e) when (e is IOException or UnauthorizedAccessException)
      {
        failed++;
        reason ??= $"{Path.GetFileName(file)} 正在被其他程式使用或無法移動，所以還在原處。關閉它之後再重試。";
      }
    }
    RemoveEmptyFolders(from);
    return failed;
  }

  /// <summary>Removes folders left empty after file-by-file moves, deepest first, never following links.</summary>
  private static void RemoveEmptyFolders(string root)
  {
    if (!Directory.Exists(root))
    {
      return;
    }
    var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true };
    foreach (var folder in Directory.EnumerateDirectories(root, "*", options).OrderByDescending(f => f.Length).Append(root))
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

  private void RecycleCreated(string batchId, BatchItem item)
  {
    if (item.CreatedPath is not { } created || !File.Exists(created))
    {
      return;
    }
    // Item ids contain ':' and '#', which are not allowed (or not wise) in folder names.
    var safe = new string(item.Id.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray());
    var folder = SlotFolder(created, batchId, "restore-" + safe);
    File.Move(created, FreeName(Path.Combine(folder, Path.GetFileName(created))), overwrite: false);
  }

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
    if (created && Path.GetFileName(area) == RecycleAreaLocator.VolumeFolderName)
    {
      File.SetAttributes(area, File.GetAttributes(area) | FileAttributes.Hidden);
    }
    return folder;
  }

  private static BatchItem Base(PlanEntry entry, PlanItem item, BatchItemState state) =>
    new(item.Id, entry.Stack.Id, item.Kind, item.Target, state)
    {
      Detail = item.Kind == PlanItemKind.Recycle ? string.Empty : entry.Keeper.Path,
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

  private static void DeleteTree(string path)
  {
    if (File.Exists(path))
    {
      File.SetAttributes(path, FileAttributes.Normal);
      File.Delete(path);
      return;
    }
    if (!Directory.Exists(path))
    {
      return;
    }
    var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0, IgnoreInaccessible = true };
    foreach (var file in Directory.EnumerateFiles(path, "*", options))
    {
      File.SetAttributes(file, FileAttributes.Normal);
    }
    Directory.Delete(path, recursive: true);
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
      // Our own partial file; it is ignored by scans and removed on the next attempt.
    }
  }
}
