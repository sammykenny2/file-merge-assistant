using System.Diagnostics;
using System.Globalization;

namespace FileWorkbench.Stacks;

/// <summary>
/// Reads git facts about copies, read-only (ADR-005): branch, HEAD, uncommitted changes, and how far a copy is behind or
/// ahead of the keeper. Runs <c>git --no-optional-locks</c>, so not even the index's stat cache is rewritten. When git is
/// not installed or a folder is not a repository, the copy simply has no git facts.
/// </summary>
public static class GitStatusReader
{
  private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

  /// <summary>Fills <see cref="StackCopy.Git"/> for every online folder copy that is a git working tree.</summary>
  public static Stack Enrich(Stack stack)
  {
    ArgumentNullException.ThrowIfNull(stack);
    if (stack.Kind != StackKind.Folder)
    {
      return stack;
    }
    var statuses = stack.Copies.Select(c => c.IsOffline ? null : Read(c.Path)).ToList();
    if (statuses.All(s => s is null))
    {
      return stack;
    }
    var keeper = statuses[stack.SuggestedKeeper];
    var copies = stack.Copies.Select((copy, i) =>
    {
      var status = statuses[i];
      if (status is null)
      {
        return copy;
      }
      if (i != stack.SuggestedKeeper && keeper?.Head is { } keeperHead && status.Head is { } head && head != keeperHead)
      {
        var (behind, ahead) = Compare(stack.SuggestedKeeperCopy.Path, copy.Path, keeperHead, head);
        status = status with { BehindKeeper = behind, AheadOfKeeper = ahead };
      }
      return copy with { Git = status };
    }).ToList();
    return stack with { Copies = copies };
  }

  /// <summary>Branch, HEAD and the number of changed or untracked files, or null when the folder is not a repository.</summary>
  public static GitStatus? Read(string folder)
  {
    ArgumentNullException.ThrowIfNull(folder);
    var marker = Path.Combine(folder, ".git");
    if (!Directory.Exists(marker) && !File.Exists(marker))
    {
      return null;
    }
    var output = Run(folder, "status --porcelain=v2 --branch");
    if (output is null)
    {
      return null;
    }
    string? head = null;
    var branch = "(detached)";
    var changes = 0;
    foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
    {
      if (line.StartsWith("# branch.oid ", StringComparison.Ordinal))
      {
        var oid = line["# branch.oid ".Length..].Trim();
        head = oid == "(initial)" ? null : oid;
      }
      else if (line.StartsWith("# branch.head ", StringComparison.Ordinal))
      {
        branch = line["# branch.head ".Length..].Trim();
      }
      else if (!line.StartsWith('#'))
      {
        changes++;
      }
    }
    return new GitStatus(branch, head, changes);
  }

  /// <summary>
  /// Commits the keeper has that the copy lacks (behind) and the reverse (ahead), when one repository knows both
  /// commits; otherwise (null, null).
  /// </summary>
  private static (int? Behind, int? Ahead) Compare(string keeperFolder, string copyFolder, string keeperHead, string copyHead)
  {
    // In the keeper's repository: left = keeper-only commits, right = copy-only commits.
    if (Run(keeperFolder, $"cat-file -e {copyHead}^{{commit}}") is not null
      && Counts(Run(keeperFolder, $"rev-list --left-right --count {keeperHead}...{copyHead}")) is { } fromKeeper)
    {
      return (fromKeeper.Left, fromKeeper.Right);
    }
    if (Run(copyFolder, $"cat-file -e {keeperHead}^{{commit}}") is not null
      && Counts(Run(copyFolder, $"rev-list --left-right --count {copyHead}...{keeperHead}")) is { } fromCopy)
    {
      return (fromCopy.Right, fromCopy.Left);
    }
    return (null, null);
  }

  private static (int Left, int Right)? Counts(string? output)
  {
    var parts = output?.Split((char[])['\t', ' '], StringSplitOptions.RemoveEmptyEntries);
    return parts is { Length: 2 }
      && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var left)
      && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var right)
      ? (left, right)
      : null;
  }

  /// <summary>Runs a read-only git command; returns its output, or null when git is missing, fails or times out.</summary>
  private static string? Run(string folder, string arguments)
  {
    var start = new ProcessStartInfo("git", $"--no-optional-locks -C \"{folder}\" {arguments}")
    {
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
      CreateNoWindow = true,
    };
    start.Environment["GIT_OPTIONAL_LOCKS"] = "0";
    start.Environment["GIT_TERMINAL_PROMPT"] = "0";
    try
    {
      using var process = Process.Start(start);
      if (process is null)
      {
        return null;
      }
      var output = process.StandardOutput.ReadToEndAsync();
      _ = process.StandardError.ReadToEndAsync();
      if (!process.WaitForExit(Timeout))
      {
        process.Kill(entireProcessTree: true);
        return null;
      }
      return process.ExitCode == 0 ? output.GetAwaiter().GetResult() : null;
    }
    catch (System.ComponentModel.Win32Exception)
    {
      // git is not installed.
      return null;
    }
    catch (InvalidOperationException)
    {
      return null;
    }
  }
}
