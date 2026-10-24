using FileWorkbench.Reporting;
using FileWorkbench.Resolution;
using FileWorkbench.Services;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace FileWorkbench.Infrastructure;

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
    services.AddSingleton<IRecycleBinService>(sp => new RecycleBinService(logger));
    services.AddSingleton<IScanCacheService>(sp => new ScanCacheService(logger));
    services.AddSingleton<ISmartMergeService, SmartMergeService>();
    services.AddSingleton<IWatchService, WatchService>();
    services.AddSingleton<ISpaceSavingsAnalyzer, SpaceSavingsAnalyzer>();
    services.AddSingleton<IPreviewStatisticsService, PreviewStatisticsService>();
    services.AddSingleton<IByteByByteVerifier, ByteByByteVerifier>();
    services.AddSingleton<IHardlinkDetector>(sp => new HardlinkDetector(logger));
    services.AddSingleton<IEmptyDirectoryService>(sp => new EmptyDirectoryService(logger));

    services.AddSingleton<IReportGenerator, JsonReportGenerator>();
    services.AddSingleton<IReportGenerator, CsvReportGenerator>();
    services.AddSingleton<IReportGenerator, PowerShellScriptGenerator>();

    return services;
  }
}
