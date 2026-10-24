namespace FileMergeAssistant.Hashing;

public static class HashAlgorithmFactory
{
  public static IHashAlgorithm Create(string algorithmName)
  {
    return algorithmName.ToLowerInvariant() switch
    {
      "sha256" => new Sha256HashAlgorithm(),
      "xxhash" => new XxHashAlgorithm(),
      "perceptual" => throw new NotSupportedException(PerceptualHashAlgorithm.DisabledReason),
      "video-perceptual" => throw new NotSupportedException(VideoFrameHashAlgorithm.DisabledReason),
      _ => throw new ArgumentException($"Unknown hash algorithm: {algorithmName}. Supported: sha256, xxhash")
    };
  }
}
