using System.Globalization;
using System.Text;
using FileMergeAssistant.Models;
using FileMergeAssistant.Utilities;

namespace FileMergeAssistant.Reporting;

public class PowerShellScriptGenerator : IReportGenerator
{
  public ReportFormat Format => ReportFormat.PowerShell;

  public async Task GenerateAsync(ScanResult result, string? outputPath = null, CancellationToken cancellationToken = default)
  {
    var filePath = outputPath != null
      ? Path.Combine(outputPath, "resolve-duplicates.ps1")
      : "resolve-duplicates.ps1";

    var directory = Path.GetDirectoryName(filePath);
    if (!string.IsNullOrEmpty(directory))
      Directory.CreateDirectory(directory);

    var sb = new StringBuilder();
    sb.AppendLine("#Requires -Version 5.1");
    sb.AppendLine("# File Merge Assistant - Duplicate Resolution Script");
    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
      "# Generated: {0:yyyy-MM-dd HH:mm:ss} UTC", result.ScanDateUtc));
    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
      "# Source scan: {0}", result.ScannedPath));
    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
      "# Total duplicate groups: {0}", result.DuplicateGroups.Count));
    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
      "# Estimated space to reclaim: {0}", SizeFormatter.Format(result.WastedSpaceBytes)));
    sb.AppendLine();
    sb.AppendLine("[CmdletBinding(SupportsShouldProcess)]");
    sb.AppendLine("param(");
    sb.AppendLine("    [switch]$Force,");
    sb.AppendLine("    [string]$RecycleBinPath = \".\\fma-recycle-bin\"");
    sb.AppendLine(")");
    sb.AppendLine();
    sb.AppendLine("$ErrorActionPreference = 'Stop'");
    sb.AppendLine("$deletedCount = 0");
    sb.AppendLine("$freedBytes = 0");
    sb.AppendLine();
    sb.AppendLine("Write-Host '=== File Merge Assistant - Duplicate Resolution ===' -ForegroundColor Cyan");
    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
      "Write-Host 'Processing {0} duplicate groups...' -ForegroundColor Yellow", result.DuplicateGroups.Count));
    sb.AppendLine("Write-Host ''");
    sb.AppendLine();

    var groupIndex = 0;
    foreach (var group in result.DuplicateGroups)
    {
      groupIndex++;
      sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
        "# --- Group {0}: Hash {1}... ({2} files, wasted: {3}) ---",
        groupIndex,
        group.Hash[..Math.Min(16, group.Hash.Length)],
        group.Files.Count,
        SizeFormatter.Format(group.WastedBytes)));

      sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
        "# KEEP: {0}", EscapePowerShell(group.Files[0].FilePath)));

      for (var i = 1; i < group.Files.Count; i++)
      {
        var file = group.Files[i];
        sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
          "$filePath = '{0}'", EscapePowerShell(file.FilePath)));
        sb.AppendLine("if (Test-Path $filePath) {");
        sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
          "    $fileSize = {0}", file.SizeBytes));
        sb.AppendLine("    if ($Force -or $PSCmdlet.ShouldProcess($filePath, 'Delete duplicate file')) {");
        sb.AppendLine("        if (-not $Force) {");
        sb.AppendLine("            $destDir = Join-Path $RecycleBinPath (Split-Path $filePath -Parent | Split-Path -Leaf)");
        sb.AppendLine("            New-Item -ItemType Directory -Path $destDir -Force | Out-Null");
        sb.AppendLine("            Move-Item -Path $filePath -Destination (Join-Path $destDir (Split-Path $filePath -Leaf)) -Force");
        sb.AppendLine("        } else {");
        sb.AppendLine("            Remove-Item -Path $filePath -Force");
        sb.AppendLine("        }");
        sb.AppendLine("        $script:deletedCount++");
        sb.AppendLine("        $script:freedBytes += $fileSize");
        sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
          "        Write-Host \"  Removed: {0}\" -ForegroundColor Red", EscapePowerShell(Path.GetFileName(file.FilePath))));
        sb.AppendLine("    }");
        sb.AppendLine("}");
        sb.AppendLine();
      }
    }

    sb.AppendLine("Write-Host ''");
    sb.AppendLine("Write-Host '=== Summary ===' -ForegroundColor Cyan");
    sb.AppendLine("Write-Host \"Files removed: $deletedCount\" -ForegroundColor White");
    sb.AppendLine("$freedMB = [math]::Round($freedBytes / 1MB, 2)");
    sb.AppendLine("Write-Host \"Space freed: ${freedMB} MB\" -ForegroundColor Green");
    sb.AppendLine("if (-not $Force) {");
    sb.AppendLine("    Write-Host \"Files moved to: $RecycleBinPath (restore manually if needed)\" -ForegroundColor Yellow");
    sb.AppendLine("}");

    await File.WriteAllTextAsync(filePath, sb.ToString(), cancellationToken);
  }

  private static string EscapePowerShell(string value)
  {
    return value.Replace("'", "''");
  }
}
