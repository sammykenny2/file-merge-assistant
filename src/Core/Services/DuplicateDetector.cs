using FileMergeAssistant.Models;

namespace FileMergeAssistant.Services;

public class DuplicateDetector : IDuplicateDetector
{
  public IReadOnlyList<DuplicateGroup> FindDuplicates(IReadOnlyList<ScannedFile> files)
  {
    return files
      .Where(f => f.Hash != null)
      .GroupBy(f => f.Hash!)
      .Where(g => g.Count() >= 2)
      .Select(g => new DuplicateGroup
      {
        Hash = g.Key,
        Files = g.OrderBy(f => f.CreatedUtc).ToList()
      })
      .OrderByDescending(g => g.WastedBytes)
      .ToList();
  }

  public IReadOnlyList<DuplicateGroup> FindDuplicatesBySize(IReadOnlyList<ScannedFile> files)
  {
    return files
      .GroupBy(f => f.SizeBytes)
      .Where(g => g.Count() >= 2)
      .SelectMany(sizeGroup =>
      {
        return sizeGroup
          .Where(f => f.Hash != null)
          .GroupBy(f => f.Hash!)
          .Where(g => g.Count() >= 2)
          .Select(g => new DuplicateGroup
          {
            Hash = g.Key,
            Files = g.OrderBy(f => f.CreatedUtc).ToList()
          });
      })
      .OrderByDescending(g => g.WastedBytes)
      .ToList();
  }

  public IReadOnlyList<DuplicateGroup> FindDuplicatesByName(IReadOnlyList<ScannedFile> files)
  {
    return files
      .GroupBy(f => Path.GetFileName(f.FilePath), StringComparer.OrdinalIgnoreCase)
      .Where(g => g.Count() >= 2)
      .Select(g => new DuplicateGroup
      {
        Hash = $"name:{g.Key}",
        Files = g.OrderBy(f => f.CreatedUtc).ToList()
      })
      .OrderByDescending(g => g.WastedBytes)
      .ToList();
  }

  public IReadOnlyList<DuplicateGroup> FindDuplicatesByNameAndSize(IReadOnlyList<ScannedFile> files)
  {
    return files
      .GroupBy(f => new { Name = Path.GetFileName(f.FilePath).ToLowerInvariant(), f.SizeBytes })
      .Where(g => g.Count() >= 2)
      .Select(g => new DuplicateGroup
      {
        Hash = $"namesize:{g.Key.Name}:{g.Key.SizeBytes}",
        Files = g.OrderBy(f => f.CreatedUtc).ToList()
      })
      .OrderByDescending(g => g.WastedBytes)
      .ToList();
  }

  public async Task<IReadOnlyList<DuplicateGroup>> VerifyGroupsByteByByteAsync(
    IReadOnlyList<DuplicateGroup> groups,
    IByteByByteVerifier verifier,
    CancellationToken cancellationToken = default)
  {
    var verified = new List<DuplicateGroup>();

    foreach (var group in groups)
    {
      var confirmedFiles = new List<ScannedFile> { group.Files[0] };

      for (var i = 1; i < group.Files.Count; i++)
      {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
          var identical = await verifier.AreFilesIdenticalAsync(
            group.Files[0].FilePath, group.Files[i].FilePath, cancellationToken);
          if (identical)
            confirmedFiles.Add(group.Files[i]);
        }
        catch
        {
          // If we can't verify, exclude the file from the group
        }
      }

      if (confirmedFiles.Count >= 2)
      {
        verified.Add(new DuplicateGroup
        {
          Hash = group.Hash,
          Files = confirmedFiles
        });
      }
    }

    return verified.OrderByDescending(g => g.WastedBytes).ToList();
  }

  public IReadOnlyList<DuplicateGroup> FilterHardlinks(
    IReadOnlyList<DuplicateGroup> groups,
    IHardlinkDetector hardlinkDetector)
  {
    var filtered = new List<DuplicateGroup>();

    foreach (var group in groups)
    {
      var nonLinked = new List<ScannedFile>();

      foreach (var file in group.Files)
      {
        var isHardlinkedToExisting = nonLinked.Any(existing =>
          hardlinkDetector.AreHardlinked(existing.FilePath, file.FilePath));

        if (!isHardlinkedToExisting)
          nonLinked.Add(file);
      }

      if (nonLinked.Count >= 2)
      {
        filtered.Add(new DuplicateGroup
        {
          Hash = group.Hash,
          Files = nonLinked
        });
      }
    }

    return filtered.OrderByDescending(g => g.WastedBytes).ToList();
  }
}
