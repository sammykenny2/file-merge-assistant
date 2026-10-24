namespace FileMergeAssistant.Hashing;

public class VideoFrameHashAlgorithm : IHashAlgorithm
{
  public const string DisabledReason = "Legacy C# video perceptual hashing is disabled in this preview because ImageSharp is not included. Native video analysis is not exposed by the current adapter.";

  public string Name => "video-perceptual";

  public Task<string> ComputeHashAsync(string filePath, CancellationToken cancellationToken = default)
    => Task.FromException<string>(new NotSupportedException(DisabledReason));
}
