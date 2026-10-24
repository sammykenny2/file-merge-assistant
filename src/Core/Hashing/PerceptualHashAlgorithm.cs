namespace FileMergeAssistant.Hashing;

public class PerceptualHashAlgorithm : IHashAlgorithm
{
  public const string DisabledReason = "Legacy C# perceptual hashing is disabled in this preview because ImageSharp is not included. Use similar-images --engine czkawka for native PNG/JPEG candidate review.";

  public string Name => "perceptual";

  public Task<string> ComputeHashAsync(string filePath, CancellationToken cancellationToken = default)
    => Task.FromException<string>(new NotSupportedException(DisabledReason));
}
