using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace FileMergeAssistant.Analysis;

internal sealed class NativeAnalysisApi
{
  internal const int MaxMessage = 2 * 1024 * 1024;
  // Modules stay loaded for the process lifetime, including cancellation of a slow decoder.
  private static readonly ConcurrentDictionary<string, NativeAnalysisApi> Loaded = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
  private readonly CreateCall _create;
  private readonly Add _add;
  private readonly HandleCall _start;
  private readonly HandleCall _cancel;
  private readonly HandleCall _destroy;
  private readonly HandleCall _finished;
  private readonly PollCall _poll;

  private NativeAnalysisApi(string path)
  {
    var module = NativeLibrary.Load(path);
    try
    {
      var abi = Export<Abi>(module, "fma_get_abi_version");
      if (abi() != 1) { throw new InvalidDataException("Unsupported Czkawka adapter ABI."); }
      var info = Export<WriteBuffer>(module, "fma_engine_info");
      Descriptor = JsonSerializer.Deserialize<AnalysisEngineDescriptor>(Read(info), AnalysisJson.Options)
        ?? throw new InvalidDataException("The native engine descriptor is missing.");
      if (Descriptor.Id != NativeAnalysisEngine.EngineId || Descriptor.Version != NativeAnalysisEngine.Version
        || Descriptor.UpstreamCommit != NativeAnalysisEngine.UpstreamCommit || Descriptor.ContractVersion != 1 || Descriptor.AbiVersion != 1
        || Descriptor.Platform != RuntimeInformation.RuntimeIdentifier || Descriptor.Capabilities.IsDefault
        || Descriptor.Availability != AnalysisAvailability.Available)
      {
        throw new InvalidDataException("The native library does not match this adapter version, upstream commit or platform.");
      }
      _create = Export<CreateCall>(module, "fma_job_create");
      _add = Export<Add>(module, "fma_job_add_files");
      _start = Export<HandleCall>(module, "fma_job_start");
      _cancel = Export<HandleCall>(module, "fma_job_cancel");
      _destroy = Export<HandleCall>(module, "fma_job_destroy");
      _finished = Export<HandleCall>(module, "fma_job_is_finished");
      _poll = Export<PollCall>(module, "fma_job_poll");
    }
    catch
    {
      // No jobs exist until descriptor validation and all export lookups have succeeded.
      NativeLibrary.Free(module);
      throw;
    }
  }

  public AnalysisEngineDescriptor Descriptor { get; }
  public static NativeAnalysisApi Load(string path) => Loaded.GetOrAdd(Path.GetFullPath(path), static full => new(full));

  public ulong Create(AnalysisRequest request)
  {
    var bytes = JsonSerializer.SerializeToUtf8Bytes(new { request.JobId, request.Engine }, AnalysisJson.Options);
    RequireSize(bytes);
    Require(_create(bytes, (nuint)bytes.Length, out var handle));
    if (handle == 0 || handle > long.MaxValue) { throw new InvalidDataException("The native engine returned an invalid job handle."); }
    return handle;
  }

  public void AddFiles(ulong handle, ImmutableArray<AnalysisInput> files, CancellationToken cancellationToken)
  {
    var offset = 0;
    while (offset < files.Length)
    {
      cancellationToken.ThrowIfCancellationRequested();
      var count = Math.Min(128, files.Length - offset);
      var bytes = JsonSerializer.SerializeToUtf8Bytes(files.Slice(offset, count), AnalysisJson.Options);
      while (bytes.Length > MaxMessage && count > 1)
      {
        count /= 2;
        bytes = JsonSerializer.SerializeToUtf8Bytes(files.Slice(offset, count), AnalysisJson.Options);
      }
      RequireSize(bytes);
      var status = _add(handle, bytes, (nuint)bytes.Length);
      cancellationToken.ThrowIfCancellationRequested();
      Require(status);
      offset += count;
    }
  }

  public void Start(ulong handle) => Require(_start(handle));
  public void Cancel(ulong handle) => _cancel(handle);
  public bool Destroy(ulong handle) => _destroy(handle) is 0 or 3;
  public bool IsFinished(ulong handle)
  {
    var state = _finished(handle);
    if (state < 0) { throw new InvalidDataException($"Native analysis status error: {-state}."); }
    return state == 1;
  }
  public NativePollEvent Poll(ulong handle)
  {
    var data = Read((byte[] buffer, nuint capacity, out nuint written) => _poll(handle, buffer, capacity, out written));
    return JsonSerializer.Deserialize<NativePollEvent>(data, AnalysisJson.Options) ?? throw new InvalidDataException("Empty native event.");
  }

  private static byte[] Read(WriteBuffer write)
  {
    var buffer = new byte[4096];
    var status = write(buffer, (nuint)buffer.Length, out var written);
    if (status == 2)
    {
      if (written == 0 || written > MaxMessage) { throw new InvalidDataException("The native result exceeds the message limit."); }
      buffer = new byte[(int)written];
      status = write(buffer, (nuint)buffer.Length, out written);
    }
    Require(status);
    if (written > (nuint)buffer.Length) { throw new InvalidDataException("The native engine returned an invalid buffer length."); }
    return buffer.AsSpan(0, (int)written).ToArray();
  }

  private static void RequireSize(byte[] bytes)
  {
    if (bytes.Length > MaxMessage) { throw new ArgumentException("The native request exceeds the message limit.", nameof(bytes)); }
  }

  private static void Require(int status)
  {
    if (status == 7) { throw new AnalysisEngineUnavailableException(AnalysisAvailability.Unsupported, "The native engine rejected the requested version, capability or parameters."); }
    if (status != 0) { throw new InvalidDataException($"Native analysis protocol error: {status}."); }
  }

  private static T Export<T>(nint module, string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(module, name));

  [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
  private delegate uint Abi();
  [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
  private delegate int CreateCall([In] byte[] input, nuint length, out ulong handle);
  [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
  private delegate int Add(ulong handle, [In] byte[] input, nuint length);
  [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
  private delegate int HandleCall(ulong handle);
  [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
  private delegate int WriteBuffer([Out] byte[] output, nuint capacity, out nuint written);
  [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
  private delegate int PollCall(ulong handle, [Out] byte[] output, nuint capacity, out nuint written);
}

internal sealed record NativePollEvent(AnalysisBatch? Batch, bool Cancelled, string? Error);
