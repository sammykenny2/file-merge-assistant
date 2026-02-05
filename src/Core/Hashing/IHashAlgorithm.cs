namespace FileMergeAssistant.Hashing;

public interface IHashAlgorithm
{
  string Name { get; }
  Task<string> ComputeHashAsync(string filePath, CancellationToken cancellationToken = default);
}
