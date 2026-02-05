using Spectre.Console;

namespace FileMergeAssistant.Cli.UI;

public static class ProgressReporter
{
  public static async Task<List<T>> RunWithProgressAsync<T>(
    string description,
    IAsyncEnumerable<T> items,
    int totalCount,
    Func<T, string> getLabel,
    CancellationToken cancellationToken = default)
  {
    var results = new List<T>();

    await AnsiConsole.Progress()
      .AutoClear(false)
      .HideCompleted(false)
      .Columns(
        new TaskDescriptionColumn(),
        new ProgressBarColumn(),
        new PercentageColumn(),
        new SpinnerColumn())
      .StartAsync(async ctx =>
      {
        var task = ctx.AddTask(description, maxValue: totalCount);

        await foreach (var item in items.WithCancellation(cancellationToken))
        {
          results.Add(item);
          task.Description = $"{description} - {getLabel(item)}";
          task.Increment(1);
        }

        task.Description = description;
        task.StopTask();
      });

    return results;
  }
}
