using System.Globalization;
using System.Numerics;

namespace FileWorkbench.Analysis;

/// <summary>Perceptual evidence is never accepted as an exact-content fingerprint.</summary>
public static class PerceptualFingerprints
{
  public const string Mean8D8 = "image-mean8-d8-v1";
  public const string DistanceDefinition = "Minimum Hamming distance (0..64 bits) across Mean 8x8 hashes of eight D4 transforms; lower is closer. Not a percentage or exact-content proof.";

  public static bool IsValid(string algorithm, string fingerprint) => TryParse(algorithm, fingerprint, out _);

  private static bool TryParse(string algorithm, string fingerprint, out ulong[] hashes)
  {
    hashes = [];
    if (algorithm != Mean8D8 || !fingerprint.StartsWith(Mean8D8 + ":", StringComparison.Ordinal) || fingerprint.Length > Mean8D8.Length + 1 + 8 * 17) { return false; }
    var values = fingerprint[(Mean8D8.Length + 1)..].Split(',');
    if (values.Length is < 1 or > 8) { return false; }
    var parsed = new ulong[values.Length];
    for (var i = 0; i < values.Length; i++)
    {
      if (values[i].Length != 16 || values[i].Any(value => value is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))
        || !ulong.TryParse(values[i], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out parsed[i])
        || parsed[i] is 0 or ulong.MaxValue || i > 0 && parsed[i - 1] >= parsed[i]) { return false; }
    }
    hashes = parsed;
    return true;
  }

  public static int Distance(string first, string second)
  {
    if (!TryParse(Mean8D8, first, out var a) || !TryParse(Mean8D8, second, out var b)) { throw new ArgumentException("Invalid perceptual evidence."); }
    return Distance(a, b);
  }

  internal static ulong[] Parse(string fingerprint) => TryParse(Mean8D8, fingerprint, out var values)
    ? values : throw new ArgumentException("Invalid perceptual evidence.", nameof(fingerprint));

  internal static int Distance(ulong[] a, ulong[] b)
  {
    var minimum = 64;
    foreach (var left in a)
    {
      foreach (var right in b) { minimum = Math.Min(minimum, BitOperations.PopCount(left ^ right)); }
    }
    return minimum;
  }

  internal static bool IsValid(AnalysisEngineSnapshot engine, string fingerprint) => engine.Capability switch
  {
    AnalysisCapability.ExactDuplicates => ContentFingerprints.IsValid(engine.Algorithm, fingerprint),
    AnalysisCapability.SimilarImages => IsValid(engine.Algorithm, fingerprint),
    _ => false,
  };
}
