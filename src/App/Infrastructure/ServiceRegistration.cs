using FileMergeAssistant.Reporting;
using FileMergeAssistant.Services;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace FileMergeAssistant.Infrastructure;

public static class ServiceRegistration
{
  public static IServiceProvider BuildServiceProvider(ILogger logger)
  {
    var services = new ServiceCollection();

    services.AddSingleton(logger);

    services.AddSingleton<IFileScanner, FileScanner>();
    services.AddSingleton<IDuplicateDetector, DuplicateDetector>();
    services.AddSingleton<IMetadataExtractor, ExifMetadataExtractor>();
    services.AddSingleton<IScanResultStore, JsonScanResultStore>();
    services.AddSingleton<IFileOperationService, FileOperationService>();

    services.AddSingleton<IReportGenerator, ConsoleReportGenerator>();
    services.AddSingleton<IReportGenerator, JsonReportGenerator>();
    services.AddSingleton<IReportGenerator, CsvReportGenerator>();

    return services.BuildServiceProvider();
  }
}
