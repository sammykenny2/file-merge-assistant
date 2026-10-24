using System.Data.Common;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using FileWorkbench.Analysis;
using FileWorkbench.Localization;

namespace FileWorkbench.Gui.Settings;

public sealed record AnalysisChoice(AnalysisEngineSnapshot Snapshot, string Label, string Detail, bool Enabled);

public sealed partial class SettingsViewModel
{
  [ObservableProperty]
  private AnalysisChoice? _selectedAnalysis;

  [ObservableProperty]
  private bool _analysisChoicesEnabled = true;

  [ObservableProperty]
  private string _analysisDetail = string.Empty;

  [ObservableProperty]
  private string _analysisDiagnostics = string.Empty;

  public IReadOnlyList<AnalysisChoice> AnalysisChoices { get; private set; } = [];

  private void RenderAnalysis()
  {
    var choices = new List<AnalysisChoice>();
    var diagnostics = new List<string>();
    foreach (var engine in Store.Engines.Descriptors)
    {
      var available = AppText.Get("analysis.availability." + engine.Availability);
      if (engine.Availability != AnalysisAvailability.Available) { diagnostics.Add($"{engine.Name}: {available}"); }
      foreach (var capability in engine.Capabilities.Where(item => item.Capability == AnalysisCapability.ExactDuplicates))
      {
        foreach (var algorithm in capability.Algorithms)
        {
          var snapshot = new AnalysisEngineSnapshot(engine.Id, engine.Version, engine.UpstreamCommit, engine.ContractVersion, engine.AbiVersion,
            capability.Capability, algorithm, "{}", "{}");
          var label = $"{engine.Name} {engine.Version} · {algorithm}";
          var detail = $"{AppText.Get("analysis.capability." + capability.Capability)} · {AppText.Get("analysis.maturity." + capability.Maturity)} · {available}";
          choices.Add(new(snapshot, label, detail, engine.Availability == AnalysisAvailability.Available));
        }
      }
    }
    var current = Store.NewJobEngine with { ScopeJson = "{}" };
    if (!choices.Any(choice => choice.Snapshot == current))
    {
      choices.Add(new(current, $"{current.EngineId} {current.EngineVersion} · {current.Algorithm}", AppText.Get("analysis.settings.unavailableSaved"), false));
    }
    AnalysisChoices = choices;
    OnPropertyChanged(nameof(AnalysisChoices));
    SelectedAnalysis = choices.Single(choice => choice.Snapshot == current);
    AnalysisDetail = SelectedAnalysis.Detail;
    if (Store.AnalysisConfigurationError is not null) { diagnostics.Insert(0, AppText.Get("analysis.settings.unavailableSaved")); }
    AnalysisDiagnostics = string.Join(Environment.NewLine, diagnostics);
  }

  partial void OnSelectedAnalysisChanged(AnalysisChoice? value)
  {
    if (!_rendering && AnalysisChoicesEnabled && value?.Enabled == true && value.Snapshot != (Store.NewJobEngine with { ScopeJson = "{}" }))
    {
      _ = SaveAnalysisChoiceAsync(value);
    }
  }

  private async Task SaveAnalysisChoiceAsync(AnalysisChoice choice)
  {
    AnalysisChoicesEnabled = false;
    try
    {
      Message = null;
      await Store.SelectNewJobEngineAsync(choice.Snapshot);
    }
    catch (Exception error) when (error is InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException or DbException)
    {
      Message = AppText.Get("analysis.settings.saveFailed", ("error", error.Message));
    }
    finally
    {
      AnalysisChoicesEnabled = true;
      Refresh();
    }
  }
}
