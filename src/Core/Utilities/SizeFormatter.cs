namespace FileMergeAssistant.Utilities;

public static class SizeFormatter
{
  private static readonly string[] Suffixes = ["B", "KB", "MB", "GB", "TB"];

  public static string Format(long bytes)
  {
    var index = 0;
    var size = (double)bytes;
    while (size >= 1024 && index < Suffixes.Length - 1)
    {
      size /= 1024;
      index++;
    }
    return $"{size:F2} {Suffixes[index]}";
  }
}
