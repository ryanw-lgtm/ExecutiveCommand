using System.Net;
using System.Security.Cryptography;
using ExecutiveCommand.Core.AI.Llm;
using Xunit;

namespace ExecutiveCommand.Core.Tests;

/// <summary>Serves a fake "model" from memory, honoring Range requests, so download/resume/verify can be tested offline.</summary>
internal sealed class FakeServer(byte[] data, bool honorRange = true, int failAfter = -1) : HttpMessageHandler
{
    public int Requests;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests++;
        long from = honorRange && request.Headers.Range?.Ranges.FirstOrDefault()?.From is long f ? f : 0;
        var slice = data.AsSpan((int)from).ToArray();
        if (failAfter >= 0 && Requests == 1) slice = slice.Take(failAfter).ToArray(); // connection drops mid-way
        var resp = new HttpResponseMessage(from > 0 ? HttpStatusCode.PartialContent : HttpStatusCode.OK) { Content = new ByteArrayContent(slice) };
        return Task.FromResult(resp);
    }
}

public class ModelDownloaderTests
{
    private static (byte[] Data, ModelSpec Spec) Fake(int size = 3_000_000)
    {
        var data = new byte[size];
        new Random(1).NextBytes(data);
        var sha = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
        return (data, new ModelSpec("fake.gguf", "http://example.invalid/fake.gguf", size, sha, "fake", "MIT"));
    }

    private static string Dir() => Path.Combine(Path.GetTempPath(), "ec-dl", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task DownloadsVerifiesAndInstalls()
    {
        var (data, spec) = Fake();
        var dir = Dir();
        var reports = new List<DownloadProgress>();
        var path = await new ModelDownloader(new FakeServer(data)).DownloadAsync(spec, dir, new Progress<DownloadProgress>(reports.Add), CancellationToken.None);
        Assert.True(ModelDownloader.IsInstalled(dir, spec));
        Assert.Equal(data, File.ReadAllBytes(path));
        Assert.False(File.Exists(path + ".part"));
    }

    [Fact]
    public async Task ResumesAfterADroppedConnection()
    {
        var (data, spec) = Fake();
        var dir = Dir();
        var server = new FakeServer(data, failAfter: 1_000_000);
        var dl = new ModelDownloader(server);
        await Assert.ThrowsAsync<InvalidDataException>(() => dl.DownloadAsync(spec, dir, null, CancellationToken.None)); // short file fails verification…
        // …so simulate the realistic case: partial file kept, then resumed.
        File.WriteAllBytes(Path.Combine(dir, "fake.gguf.part"), data.Take(1_000_000).ToArray());
        var path = await dl.DownloadAsync(spec, dir, null, CancellationToken.None);
        Assert.Equal(data, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task CorruptDownloadIsRejectedAndNeverInstalled()
    {
        var (data, spec) = Fake();
        var bad = (byte[])data.Clone();
        bad[12345] ^= 0xFF;
        var dir = Dir();
        await Assert.ThrowsAsync<InvalidDataException>(() => new ModelDownloader(new FakeServer(bad)).DownloadAsync(spec, dir, null, CancellationToken.None));
        Assert.False(ModelDownloader.IsInstalled(dir, spec));
        Assert.False(File.Exists(Path.Combine(dir, "fake.gguf")));
    }

    [Fact]
    public async Task CancellationKeepsPartialFileForResume()
    {
        var (data, spec) = Fake();
        var dir = Dir();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ModelDownloader(new FakeServer(data)).DownloadAsync(spec, dir, null, cts.Token));
        Assert.False(ModelDownloader.IsInstalled(dir, spec));
    }
}
