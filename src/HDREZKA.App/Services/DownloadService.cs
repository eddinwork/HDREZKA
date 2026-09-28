namespace HDREZKA.App.Services;

public enum DownloadState
{
    Queued,
    Resolving,
    Downloading,
    Done,
    Failed,
    Skipped,
    Cancelled,
}

public sealed class DownloadJob
{
    public required string Title { get; init; }
    public string? Label { get; init; }
    public required string FileName { get; init; }
    public required string FilePath { get; init; }

    /// <summary>Resolves the direct mp4 URL (null = skip episode).</summary>
    public required Func<CancellationToken, Task<string?>> ResolveUrl { get; init; }

    public DownloadState State { get; set; } = DownloadState.Queued;
    public double Progress { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// Sequential mp4 downloader (donor perk). One batch at a time.
/// </summary>
public sealed class DownloadService
{
    public static DownloadService Instance { get; } = new();

    public event Action? Changed;

    private readonly List<DownloadJob> _jobs = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _runCts;

    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    static DownloadService()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("HDREZKA/1.0.0 (Windows; WinUI)");
    }

    private DownloadService()
    {
    }

    public static string DownloadFolder
    {
        get
        {
            try
            {
                var custom = SettingsService.Instance.DownloadFolder?.Trim();
                var dir = !string.IsNullOrEmpty(custom)
                    ? custom
                    : Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        "Downloads", "HDREZKA");
                Directory.CreateDirectory(dir);
                return dir;
            }
            catch
            {
                var fallback = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Downloads", "HDREZKA");
                Directory.CreateDirectory(fallback);
                return fallback;
            }
        }
    }

    private volatile bool _paused;
    private DateTime _lastProgressReport = DateTime.MinValue;

    public bool IsPaused => _paused;

    public void SetPaused(bool paused)
    {
        _paused = paused;
        Changed?.Invoke();
    }

    public static string SanitizeFileName(string name)
    {
        foreach (var ch in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(ch, '_');
        }

        name = name.Trim().TrimEnd('.');
        return name.Length == 0 ? "video" : name;
    }

    public IReadOnlyList<DownloadJob> Jobs
    {
        get
        {
            lock (_jobs) return _jobs.ToList();
        }
    }

    public static bool HasActive
    {
        get
        {
            lock (Instance._jobs)
            {
                return Instance._jobs.Any(j => j.State is DownloadState.Queued
                    or DownloadState.Resolving or DownloadState.Downloading);
            }
        }
    }

    public static readonly string[] Qualities = ["2160p", "1440p", "1080p", "720p", "480p", "360p"];

    public void Enqueue(IEnumerable<DownloadJob> jobs)
    {
        lock (_jobs) _jobs.AddRange(jobs);
        Changed?.Invoke();
        _ = ProcessAsync();
    }

    public void CancelActive()
    {
        try { _runCts?.Cancel(); } catch { }
    }

    public void ClearFinished()
    {
        lock (_jobs)
        {
            _jobs.RemoveAll(j => j.State is DownloadState.Done or DownloadState.Failed
                or DownloadState.Skipped or DownloadState.Cancelled);
        }

        Changed?.Invoke();
    }

    private async Task ProcessAsync()
    {
        if (!await _gate.WaitAsync(0).ConfigureAwait(false)) return;
        _runCts = new CancellationTokenSource();
        try
        {
            while (true)
            {
                DownloadJob? job;
                lock (_jobs) job = _jobs.FirstOrDefault(j => j.State == DownloadState.Queued);
                if (job == null) break;
                await RunJobAsync(job, _runCts.Token).ConfigureAwait(false);
            }
        }
        catch
        {
        }
        finally
        {
            try { _runCts?.Dispose(); } catch { }
            _runCts = null;
            _gate.Release();
        }
    }

    private async Task RunJobAsync(DownloadJob job, CancellationToken ct)
    {
        try
        {
            job.State = DownloadState.Resolving;
            Changed?.Invoke();

            var url = await job.ResolveUrl(ct).ConfigureAwait(false);
            if (ct.IsCancellationRequested)
            {
                job.State = DownloadState.Cancelled;
            }
            else if (string.IsNullOrEmpty(url))
            {
                job.State = DownloadState.Skipped;
            }
            else
            {
                job.State = DownloadState.Downloading;
                Changed?.Invoke();
                await DownloadFileAsync(url!, job.FilePath, p =>
                {
                    job.Progress = p;
                    // Throttle: rebuilding UI per 80KB chunk freezes it.
                    var now = DateTime.UtcNow;
                    if ((now - _lastProgressReport).TotalMilliseconds >= 250 || p >= 100)
                    {
                        _lastProgressReport = now;
                        Changed?.Invoke();
                    }
                }, ct).ConfigureAwait(false);
                job.Progress = 100;
                job.State = DownloadState.Done;
            }
        }
        catch (OperationCanceledException)
        {
            job.State = DownloadState.Cancelled;
        }
        catch (Exception ex)
        {
            job.State = DownloadState.Failed;
            job.Error = ex.Message;
            try { App.TryLog(ex); } catch { }
        }

        Changed?.Invoke();
    }

    private async Task DownloadFileAsync(
        string url, string destPath, Action<double> progress, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

        // Fast path: parallel segmented download (each connection gets
        // full throttle, total is N times faster on capped CDNs).
        var total = await ProbeRangesAsync(url, ct).ConfigureAwait(false);
        if (total is > 4 * 1024 * 1024)
        {
            await ParallelDownloadAsync(url, destPath, total.Value, progress, ct).ConfigureAwait(false);
            progress(100);
            return;
        }

        long singleRead = 0;
        await SingleDownloadAsync(url, destPath, null, null, (n, total) =>
        {
            singleRead += n;
            if (total is > 0) progress(100.0 * singleRead / total.Value);
        }, ct).ConfigureAwait(false);
        progress(100);
    }

    /// <summary>Total length if the server honors Range requests, else null.</summary>
    private async Task<long?> ProbeRangesAsync(string url, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 0);
            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (resp.StatusCode != System.Net.HttpStatusCode.PartialContent) return null;
            var length = resp.Content.Headers.ContentRange?.Length;
            return length is > 0 ? length : null;
        }
        catch
        {
            return null;
        }
    }

    private async Task ParallelDownloadAsync(
        string url, string destPath, long total, Action<double> progress, CancellationToken ct)
    {
        var parts = (int)Math.Clamp((total + 8 * 1024 * 1024 - 1) / (8 * 1024 * 1024), 2, 8);
        var partsDir = destPath + ".parts";
        Directory.CreateDirectory(partsDir);

        long totalRead = 0;
        void OnBytes(long n, long? _)
        {
            var read = Interlocked.Add(ref totalRead, n);
            progress(100.0 * read / total);
        }

        try
        {
            var tasks = new List<Task>();
            for (var i = 0; i < parts; i++)
            {
                var from = total * i / parts;
                var to = (i == parts - 1) ? total - 1 : total * (i + 1) / parts - 1;
                var partPath = Path.Combine(partsDir, $"part{i:00}");
                tasks.Add(SingleDownloadAsync(url, partPath, from, to, OnBytes, ct));
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);

            using (var dest = File.Create(destPath))
            {
                for (var i = 0; i < parts; i++)
                {
                    var partPath = Path.Combine(partsDir, $"part{i:00}");
                    using var part = File.OpenRead(partPath);
                    await part.CopyToAsync(dest, ct).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            try { Directory.Delete(partsDir, recursive: true); } catch { }
        }
    }

    private async Task SingleDownloadAsync(
        string url, string destPath, long? from, long? to, Action<long, long?> onBytes, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (from != null) req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(from, to);
        using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (from != null && resp.StatusCode != System.Net.HttpStatusCode.PartialContent)
        {
            throw new IOException("Server refused ranged download");
        }

        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength;
        using var net = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var tmp = from == null ? destPath + ".tmp" : destPath;
        using (var file = File.Create(tmp))
        {
            var buffer = new byte[81920];
            int n;
            while ((n = await net.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                while (_paused && !ct.IsCancellationRequested)
                {
                    await Task.Delay(200, ct).ConfigureAwait(false);
                }

                ct.ThrowIfCancellationRequested();
                await file.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
                onBytes(n, total);
            }
        }

        if (from == null) File.Move(tmp, destPath, overwrite: true);
    }
}
