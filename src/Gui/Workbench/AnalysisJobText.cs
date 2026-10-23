using FileMergeAssistant.Analysis;
using FileMergeAssistant.Localization;
using FileMergeAssistant.Sources;

namespace FileMergeAssistant.Gui.Workbench;

internal static class AnalysisJobText
{
  public static string Summary(ScanJobInfo job, bool demo)
  {
    if (job.Snapshot is not { } snapshot) { return AppText.Get(demo ? "analysis.job.demo" : "analysis.job.legacy"); }
    var engine = snapshot.Engine;
    var name = engine.EngineId == ManagedAnalysisEngine.EngineId ? "C#" : engine.EngineId == NativeAnalysisEngine.EngineId ? "Czkawka" : engine.EngineId;
    return $"{name} {engine.EngineVersion} · {AppText.Get("analysis.capability." + engine.Capability)} · {engine.Algorithm}";
  }

  public static string Details(ScanJobInfo job)
  {
    if (job.Snapshot is not { } snapshot) { return AppText.Get("analysis.job.legacy"); }
    var detail = AppText.Get("analysis.job.scope", ("sources", Fmt.List(snapshot.Sources.Select(item => item.Source.Root))),
      ("parameters", snapshot.Engine.ParametersJson));
    return snapshot.Engine.UpstreamCommit is { } commit ? detail + Environment.NewLine + "commit " + commit : detail;
  }
}
