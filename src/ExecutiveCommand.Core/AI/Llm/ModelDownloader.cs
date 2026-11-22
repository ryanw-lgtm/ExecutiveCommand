using System.Net;
using System.Security.Cryptography;

namespace ExecutiveCommand.Core.AI.Llm;

/// <summary>The language model the game ships with, pinned to an exact file and checksum.</summary>
public sealed record ModelSpec(string FileName, string Url, long SizeBytes, string Sha256, string DisplayName, string License)
{
    /// <summary>
    /// Qwen3-4B-Instruct-2507 (Apache-2.0, verified on the official model card and LICENSE file),
    /// Q4_K_M quantization from the unsloth GGUF repository, pinned to revision a06e946.
    /// </summary>
    public static readonly ModelSpec Default = new(
        "Qwen3-4B-Instruct-2507-Q4_K_M.gguf",
        "https://huggingface.co/unsloth/Qwen3-4B-Instruct-2507-GGUF/resolve/a06e946bb6b655725eafa393f4a9745d460374c9/Qwen3-4B-Instruct-2507-Q4_K_M.gguf",
        2_497_281_120,
        "3605803b982cb64aead44f6c1b2ae36e3acdb41d8e46c8a94c6533bc4c67e597",
        "Qwen3 4B Instruct (2507), 4-bit",
        "Apache-2.0");
}

public sealed record DownloadProgress(long Received, long Total, double BytesPerSecond, string Stage)
{
    public double Fraction => Total <= 0 ? 0 : Math.Clamp(Received / (double)Total, 0, 1);
}

/// <summary>
/// Downloads the model once, with resume (HTTP Range), progress, cancellation and SHA-256 verification.
/// The file is written to "&lt;name&gt;.part" and only renamed into place after the checksum matches,
/// so a half-finished or tampered download is never loaded.
/// </summary>
public sealed class ModelDownloader(HttpMessageHandler? handler = null)
{
    private readonly HttpClient _http = new(handler ?? new SocketsHttpHandler { AllowAutoRedirect = true }) { Timeout = Timeout.InfiniteTimeSpan };

    public static bool IsInstalled(string directory, ModelSpec spec)
    {
        var path = Path.Combine(directory, spec.FileName);
        return File.Exists(path) && new FileInfo(path).Length == spec.SizeBytes;
    }

    public async Task<string> DownloadAsync(ModelSpec spec, string directory, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(directory);
        string final = Path.Combine(directory, spec.FileName);
        string part = final + ".part";
        if (IsInstalled(directory, spec)) return final;

        long existing = File.Exists(part) ? new FileInfo(part).Length : 0;
        if (existing > spec.SizeBytes) { File.Delete(part); existing = 0; }

        if (existing < spec.SizeBytes)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, spec.Url);
            if (existing > 0) req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existing, null);
            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (existing > 0 && resp.StatusCode == HttpStatusCode.OK) existing = 0; // server ignored Range: start over
            else if (!resp.IsSuccessStatusCode) throw new IOException($"Download failed: HTTP {(int)resp.StatusCode}.");

            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            await using var dst = new FileStream(part, existing > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, useAsync: true);
            var buffer = new byte[1 << 20];
            long received = existing;
            var started = DateTime.UtcNow;
            long sinceStart = 0;
            var lastReport = DateTime.MinValue;
            int n;
            while ((n = await src.ReadAsync(buffer, ct)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, n), ct);
                received += n;
                sinceStart += n;
                if ((DateTime.UtcNow - lastReport).TotalMilliseconds > 250)
                {
                    lastReport = DateTime.UtcNow;
                    double secs = Math.Max(0.001, (DateTime.UtcNow - started).TotalSeconds);
                    progress?.Report(new DownloadProgress(received, spec.SizeBytes, sinceStart / secs, "Downloading"));
                }
            }
        }

        progress?.Report(new DownloadProgress(spec.SizeBytes, spec.SizeBytes, 0, "Verifying"));
        string hash;
        await using (var fs = new FileStream(part, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, useAsync: true))
            hash = Convert.ToHexString(await SHA256.HashDataAsync(fs, ct)).ToLowerInvariant();
        if (!string.Equals(hash, spec.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(part);
            throw new InvalidDataException("The downloaded model did not match its expected checksum and was discarded. Please try again.");
        }
        File.Move(part, final, overwrite: true);
        progress?.Report(new DownloadProgress(spec.SizeBytes, spec.SizeBytes, 0, "Ready"));
        return final;
    }
}
