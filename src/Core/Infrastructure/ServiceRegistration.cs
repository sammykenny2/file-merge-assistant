using FileMergeAssistant.Reporting;
using FileMergeAssistant.Resolution;
using FileMergeAssistant.Services;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace FileMergeAssistant.Infrastructure;

public static class ServiceRegistration
{
  public static IServiceCollection RegisterCoreServices(this IServiceCollection services, ILogger logger)
  {
    services.AddSingleton(logger);

    services.AddSingleton<IFileScanner, FileScanner>();
    services.AddSingleton<IDuplicateDetector, DuplicateDetector>();
    services.AddSingleton<IMetadataExtractor, ExifMetadataExtractor>();
    services.AddSingleton<IScanResultStore, JsonScanResultStore>();
    services.AddSingleton<IFileOperationService, FileOperationService>();
    services.AddSingleton<IResolutionService, ResolutionService>();

    services.AddSingleton<IReportGenerator, JsonReportGenerator>();
    services.AddSingleton<IReportGenerator, CsvReportGenerator>();

    return services;
  }
}
