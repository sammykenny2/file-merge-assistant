namespace FileMergeAssistant.Hashing;

public static class HashAlgorithmFactory
{
  public static IHashAlgorithm Create(string algorithmName)
  {
    return algorithmName.ToLowerInvariant() switch
    {
      "sha256" => new Sha256HashAlgorithm(),
      "xxhash" => new XxHashAlgorithm(),
      "perceptual" => new PerceptualHashAlgorithm(),
      _ => throw new ArgumentException($"Unknown hash algorithm: {algorithmName}. Supported: sha256, xxhash, perceptual")
    };
  }
}
