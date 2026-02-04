using System.Globalization;
using Serilog;

namespace FileMergeAssistant.Infrastructure;

public static class LoggingSetup
{
  public static ILogger CreateLogger()
  {
    return new LoggerConfiguration()
      .MinimumLevel.Information()
      .WriteTo.File(
        Path.Combine("fma-results", "fma.log"),
        rollingInterval: RollingInterval.Day,
        formatProvider: CultureInfo.InvariantCulture,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
      .CreateLogger();
  }
}
