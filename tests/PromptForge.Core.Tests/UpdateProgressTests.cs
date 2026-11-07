using System.Net;
using System.Security.Cryptography;
using PromptForge.Core.Updates;

namespace PromptForge.Core.Tests;

public class UpdateProgressTests
{
    private const string ExeUrl = "https://github.com/Maneet7805/PromptForge/releases/download/v1.1.0/PromptForge.exe";
    private const string SumsUrl = "https://github.com/Maneet7805/PromptForge/releases/download/v1.1.0/SHA256SUMS.txt";
    private static readonly ReleaseInfo Release = new(new Version(1, 1, 0), "v1.1.0", ExeUrl, SumsUrl, "");

    private sealed class Collect : IProgress<UpdateProgress>
    {
        public List<UpdateProgress> Items { get; } = [];
        public void Report(UpdateProgress value) => Items.Add(value);
    }

    /// <summary>A non-seekable body, so the content has no length. Pauses after the first half until told otherwise.</summary>
    private sealed class HalfThenPause(byte[] bytes, Func<CancellationToken, Task>? pause) : Stream
    {
        private int _position;
        private bool _paused;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            var half = bytes.Length / 2;
            if (_position >= half && !_paused)
            {
                _paused = true;
                if (pause is not null) await pause(ct);
            }
            var limit = _position < half ? half : bytes.Length;
            var count = Math.Min(Math.Min(buffer.Length, 65536), limit - _position);
            if (count <= 0) return 0;
            bytes.AsMemory(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static HttpContent Body(byte[] bytes, Func<CancellationToken, Task>? pause = null) => new StreamContent(new HalfThenPause(bytes, pause));

    private static HttpClient Client(byte[] bytes, string hash, Func<HttpContent>? content = null) => new(new FakeHandler(r =>
        r.RequestUri!.ToString() == SumsUrl
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($"{hash}  PromptForge.exe\n") }
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = content?.Invoke() ?? new ByteArrayContent(bytes) }));

    private static string NewDir() => Path.Combine(Path.GetTempPath(), "pf-progress-test-" + Guid.NewGuid().ToString("N"));
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static void Cleanup(string dir)
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    [Theory]
    [InlineData(0L, 100L, 0)]
    [InlineData(42L, 100L, 42)]
    [InlineData(999L, 1000L, 99)]
    [InlineData(150L, 100L, 100)]
    public void Percent_is_a_whole_number_clamped_to_100(long received, long total, int expected)
    {
        Assert.Equal(expected, new UpdateProgress(UpdateStage.Downloading, received, total).Percent);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    public void Percent_is_null_without_a_usable_total(long? total)
    {
        Assert.Null(new UpdateProgress(UpdateStage.Downloading, 500, total).Percent);
    }

    [Fact]
    public void Describe_shows_megabytes_with_or_without_a_total()
    {
        Assert.Equal("1.0 of 2.0 MB", new UpdateProgress(UpdateStage.Downloading, 1_048_576, 2_097_152).Describe());
        Assert.Equal("1.5 MB downloaded", new UpdateProgress(UpdateStage.Downloading, 1_572_864, null).Describe());
    }

    [Fact]
    public async Task Progress_goes_through_the_stages_and_ends_at_100_percent()
    {
        var bytes = new byte[300_000];
        Random.Shared.NextBytes(bytes);
        var dir = NewDir();
        try
        {
            var seen = new Collect();
            await new UpdateChecker(Client(bytes, Hash(bytes))).DownloadVerifiedAsync(Release, dir, "1.0.0", progress: seen);

            Assert.Equal(UpdateStage.Preparing, seen.Items[0].Stage);
            Assert.Equal(UpdateStage.Verifying, seen.Items[^1].Stage);
            var downloading = seen.Items.Where(p => p.Stage == UpdateStage.Downloading).ToList();
            Assert.Equal(100, downloading[^1].Percent);
            Assert.Equal(downloading.Select(p => p.Percent!.Value).Order(), downloading.Select(p => p.Percent!.Value));
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public async Task Without_a_content_length_bytes_are_reported_but_not_a_percent()
    {
        var bytes = new byte[600_000];
        Random.Shared.NextBytes(bytes);
        var dir = NewDir();
        try
        {
            var seen = new Collect();
            await new UpdateChecker(Client(bytes, Hash(bytes), () => Body(bytes))).DownloadVerifiedAsync(Release, dir, "1.0.0", progress: seen);

            var downloading = seen.Items.Where(p => p.Stage == UpdateStage.Downloading).ToList();
            Assert.All(downloading, p => Assert.Null(p.Percent));
            Assert.True(downloading[^1].BytesReceived > 0);
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public async Task Cancelling_mid_download_throws_and_deletes_the_partial_file()
    {
        var bytes = new byte[600_000];
        var dir = NewDir();
        try
        {
            using var cts = new CancellationTokenSource();
            var checker = new UpdateChecker(Client(bytes, Hash(bytes), () => Body(bytes, async ct =>
            {
                await cts.CancelAsync();
                await Task.Delay(Timeout.Infinite, ct);
            })));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => checker.DownloadVerifiedAsync(Release, dir, "1.0.0", cts.Token));

            Assert.False(File.Exists(Path.Combine(dir, "PromptForge.exe")));
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public async Task A_download_that_stalls_past_the_timeout_says_it_took_too_long()
    {
        var bytes = new byte[600_000];
        var dir = NewDir();
        try
        {
            var checker = new UpdateChecker(Client(bytes, Hash(bytes), () => Body(bytes, ct => Task.Delay(Timeout.Infinite, ct))),
                downloadTimeout: TimeSpan.FromMilliseconds(100));

            var ex = await Assert.ThrowsAsync<UpdateException>(() => checker.DownloadVerifiedAsync(Release, dir, "1.0.0"));

            Assert.Contains("too long", ex.Message);
            Assert.False(File.Exists(Path.Combine(dir, "PromptForge.exe")));
        }
        finally
        {
            Cleanup(dir);
        }
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
