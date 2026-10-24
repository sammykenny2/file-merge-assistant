using System.Net;
using System.Security.Cryptography;
using FileWorkbench.Updates;
using Xunit;

namespace FileWorkbench.Tests.Updates;

public sealed class UpdateServiceTests : IDisposable
{
  private const string Repo = "owner/app";
  private const string FeedUrl = "https://github.com/owner/app/releases.atom";
  private const string ManifestUrl = "https://github.com/owner/app/releases/download/v0.2.0/" + UpdateFeed.ManifestFileName;
  private const string InstallerName = "FileWorkbench-0.2.0-x64-setup.exe";
  private const string InstallerUrl = "https://github.com/owner/app/releases/download/v0.2.0/" + InstallerName;
  private static readonly Version Current = new(0, 1, 0);
  private readonly string _downloads = Path.Combine(Path.GetTempPath(), $"file-workbench-update-test-{Guid.NewGuid():N}");

  public void Dispose()
  {
    if (Directory.Exists(_downloads))
      Directory.Delete(_downloads, recursive: true);
  }

  private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
  {
    public List<Uri> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
      Requests.Add(request.RequestUri!);
      return Task.FromResult(respond(request));
    }
  }

  private static HttpResponseMessage Ok(byte[] body) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };

  private static HttpResponseMessage Status(HttpStatusCode code) => new(code) { Content = new ByteArrayContent([]) };

  private static HttpResponseMessage Redirect(string location)
  {
    var response = new HttpResponseMessage(HttpStatusCode.Found) { Content = new ByteArrayContent([]) };
    response.Headers.Location = new Uri(location);
    return response;
  }

  private UpdateService Create(FakeHandler handler) => new(Repo, _downloads, handler);

  private static UpdatePackage Package(byte[] content, long? size = null, string? sha256 = null) => new(
    new Version(0, 2, 0), "v0.2.0", "", InstallerName, size ?? content.Length,
    sha256 ?? Convert.ToHexStringLower(SHA256.HashData(content)), new Uri(InstallerUrl),
    new Uri("https://github.com/owner/app/releases/tag/v0.2.0"));

  [Fact]
  public async Task Check_Reads_The_Feed_Then_The_Manifest_Through_Asset_Redirects()
  {
    var handler = new FakeHandler(request => request.RequestUri!.AbsoluteUri switch
    {
      FeedUrl => Ok(UpdateFeedTests.Feed(UpdateFeedTests.Page("v0.2.0"))),
      ManifestUrl => Redirect("https://release-assets.githubusercontent.com/manifest?sig=1"),
      "https://release-assets.githubusercontent.com/manifest?sig=1" => Ok(UpdateFeedTests.Manifest("0.2.0", "v0.2.0")),
      _ => Status(HttpStatusCode.NotFound)
    });
    using var sut = Create(handler);

    var package = await sut.CheckAsync(Current);

    Assert.NotNull(package);
    Assert.Equal(new Version(0, 2, 0), package.Version);
    Assert.Equal(3, handler.Requests.Count);
  }

  [Fact]
  public async Task Check_Returns_Null_When_Up_To_Date_Without_Fetching_A_Manifest()
  {
    var handler = new FakeHandler(_ => Ok(UpdateFeedTests.Feed(UpdateFeedTests.Page("v0.1.0"))));
    using var sut = Create(handler);

    Assert.Null(await sut.CheckAsync(Current));
    Assert.Single(handler.Requests);
  }

  [Fact]
  public async Task Check_Maps_A_Missing_Feed_To_Not_Found()
  {
    using var sut = Create(new FakeHandler(_ => Status(HttpStatusCode.NotFound)));

    var error = await Assert.ThrowsAsync<UpdateException>(() => sut.CheckAsync(Current));

    Assert.Equal("not_found", error.Code);
    Assert.Contains("private", error.Message, StringComparison.Ordinal);
  }

  [Fact]
  public async Task Check_Does_Not_Follow_Redirects_For_The_Feed()
  {
    var handler = new FakeHandler(_ => Redirect("https://github.com/login"));
    using var sut = Create(handler);

    var error = await Assert.ThrowsAsync<UpdateException>(() => sut.CheckAsync(Current));

    Assert.Equal("provider_rejected", error.Code);
    Assert.Single(handler.Requests);
  }

  [Theory]
  [InlineData("https://example.com/manifest")]
  [InlineData("http://objects.githubusercontent.com/manifest")]
  public async Task Check_Refuses_Redirects_Outside_The_GitHub_Asset_Hosts(string location)
  {
    var handler = new FakeHandler(request => request.RequestUri!.AbsoluteUri == FeedUrl
      ? Ok(UpdateFeedTests.Feed(UpdateFeedTests.Page("v0.2.0")))
      : Redirect(location));
    using var sut = Create(handler);

    var error = await Assert.ThrowsAsync<UpdateException>(() => sut.CheckAsync(Current));

    Assert.Equal("provider_rejected", error.Code);
  }

  [Fact]
  public async Task Check_Maps_Too_Many_Requests_To_Rate_Limited()
  {
    using var sut = Create(new FakeHandler(_ => Status(HttpStatusCode.TooManyRequests)));

    var error = await Assert.ThrowsAsync<UpdateException>(() => sut.CheckAsync(Current));

    Assert.Equal("rate_limited", error.Code);
  }

  [Fact]
  public async Task Check_Maps_Forbidden_With_Retry_After_To_Rate_Limited_And_Plain_Forbidden_To_Rejected()
  {
    using var limited = Create(new FakeHandler(_ =>
    {
      var response = Status(HttpStatusCode.Forbidden);
      response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(60));
      return response;
    }));
    using var rejected = Create(new FakeHandler(_ => Status(HttpStatusCode.Forbidden)));

    Assert.Equal("rate_limited", (await Assert.ThrowsAsync<UpdateException>(() => limited.CheckAsync(Current))).Code);
    Assert.Equal("provider_rejected", (await Assert.ThrowsAsync<UpdateException>(() => rejected.CheckAsync(Current))).Code);
  }

  [Fact]
  public async Task Check_Maps_Network_Failures_To_Offline()
  {
    using var sut = Create(new FakeHandler(_ => throw new HttpRequestException("no route")));

    var error = await Assert.ThrowsAsync<UpdateException>(() => sut.CheckAsync(Current));

    Assert.Equal("offline", error.Code);
  }

  [Fact]
  public async Task Download_Verifies_And_Keeps_The_Installer()
  {
    var content = RandomNumberGenerator.GetBytes(300_000);
    var progress = new List<double>();
    using var sut = Create(new FakeHandler(_ => Ok(content)));

    var path = await sut.DownloadAsync(Package(content), new SynchronousProgress(progress.Add));

    Assert.Equal(Path.Combine(_downloads, InstallerName), path);
    Assert.Equal(content, await File.ReadAllBytesAsync(path));
    Assert.Equal(1.0, progress[^1]);
    Assert.False(File.Exists(path + ".partial"));
  }

  [Fact]
  public async Task Download_Deletes_A_File_With_The_Wrong_Checksum()
  {
    var content = RandomNumberGenerator.GetBytes(1000);
    using var sut = Create(new FakeHandler(_ => Ok(content)));

    var error = await Assert.ThrowsAsync<UpdateException>(() => sut.DownloadAsync(Package(content, sha256: new string('0', 64))));

    Assert.Equal("checksum_mismatch", error.Code);
    Assert.Empty(Directory.EnumerateFiles(_downloads));
  }

  [Theory]
  [InlineData(999)]
  [InlineData(1001)]
  public async Task Download_Rejects_A_Different_Size(long publishedSize)
  {
    var content = RandomNumberGenerator.GetBytes(1000);
    using var sut = Create(new FakeHandler(_ => Ok(content)));

    var error = await Assert.ThrowsAsync<UpdateException>(() => sut.DownloadAsync(Package(content, size: publishedSize)));

    Assert.Equal("size_mismatch", error.Code);
    Assert.Empty(Directory.EnumerateFiles(_downloads));
  }

  [Fact]
  public async Task Download_Removes_Earlier_Installers_But_Leaves_Other_Files()
  {
    Directory.CreateDirectory(_downloads);
    var old = Path.Combine(_downloads, "FileWorkbench-0.1.5-x64-setup.exe");
    var unrelated = Path.Combine(_downloads, "notes.txt");
    await File.WriteAllTextAsync(old, "old");
    await File.WriteAllTextAsync(unrelated, "keep");
    var content = RandomNumberGenerator.GetBytes(10);
    using var sut = Create(new FakeHandler(_ => Ok(content)));

    await sut.DownloadAsync(Package(content));

    Assert.False(File.Exists(old));
    Assert.True(File.Exists(unrelated));
  }

  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public async Task Download_Maps_An_Interrupted_Connection_To_Offline_And_Leaves_No_File(bool httpIoException)
  {
    var content = RandomNumberGenerator.GetBytes(200_000);
    using var sut = Create(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
    {
      Content = new StreamContent(new FailingStream(content, failAfter: 50_000, httpIoException))
    }));

    var error = await Assert.ThrowsAsync<UpdateException>(() => sut.DownloadAsync(Package(content)));

    Assert.Equal("offline", error.Code);
    Assert.Empty(Directory.EnumerateFiles(_downloads));
  }

  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public async Task Check_Maps_A_Connection_Reset_While_Reading_The_Feed_To_Offline(bool httpIoException)
  {
    var feed = UpdateFeedTests.Feed(UpdateFeedTests.Page("v0.2.0"));
    using var sut = Create(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
    {
      Content = new StreamContent(new FailingStream(feed, failAfter: 20, httpIoException))
    }));

    var error = await Assert.ThrowsAsync<UpdateException>(() => sut.CheckAsync(Current));

    Assert.Equal("offline", error.Code);
  }

  [Fact]
  public async Task Download_Stops_When_Cancelled_And_Leaves_No_File()
  {
    var content = RandomNumberGenerator.GetBytes(200_000);
    using var cancel = new CancellationTokenSource();
    using var sut = Create(new FakeHandler(_ => Ok(content)));

    await Assert.ThrowsAnyAsync<OperationCanceledException>(
      () => sut.DownloadAsync(Package(content), new SynchronousProgress(_ => cancel.Cancel()), cancel.Token));

    Assert.Empty(Directory.EnumerateFiles(_downloads));
  }

  [Fact]
  public async Task OpenVerifiedInstaller_Accepts_The_Downloaded_File_And_Locks_It()
  {
    var content = RandomNumberGenerator.GetBytes(5000);
    using var sut = Create(new FakeHandler(_ => Ok(content)));
    var path = await sut.DownloadAsync(Package(content));

    using (UpdateService.OpenVerifiedInstaller(path, Package(content)))
    {
      Assert.Throws<IOException>(() => File.WriteAllBytes(path, [1, 2, 3]));
    }
  }

  [Fact]
  public async Task OpenVerifiedInstaller_Rejects_A_File_Changed_After_Download()
  {
    var content = RandomNumberGenerator.GetBytes(5000);
    using var sut = Create(new FakeHandler(_ => Ok(content)));
    var path = await sut.DownloadAsync(Package(content));
    var tampered = (byte[])content.Clone();
    tampered[0] ^= 0xFF;
    await File.WriteAllBytesAsync(path, tampered);

    var error = Assert.Throws<UpdateException>(() => UpdateService.OpenVerifiedInstaller(path, Package(content)));

    Assert.Equal("checksum_mismatch", error.Code);
  }

  /// <summary>
  /// Returns <paramref name="failAfter"/> bytes of the content, then fails the way a dropped connection does: with an
  /// HttpIOException, or with the plain IOException a socket reset produces.
  /// </summary>
  private sealed class FailingStream(byte[] content, int failAfter, bool httpIoException = true) : MemoryStream(content)
  {
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
      if (Position >= failAfter)
        throw httpIoException
          ? new HttpIOException(HttpRequestError.ResponseEnded, "The response ended prematurely.")
          : new IOException("Unable to read data from the transport connection.");
      return base.ReadAsync(buffer[..Math.Min(buffer.Length, failAfter - (int)Position)], cancellationToken);
    }
  }

  private sealed class SynchronousProgress(Action<double> report) : IProgress<double>
  {
    public void Report(double value) => report(value);
  }
}
