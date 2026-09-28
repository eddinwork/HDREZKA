using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace HDREZKA.App.Services;

/// <summary>
/// Poster cache: memory LRU + disk (%LocalAppData%/HDREZKA/postercache,
/// 200MB / 30 days). Stops re-downloading posters on every scroll.
/// </summary>
public static class PosterCache
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly ConcurrentDictionary<string, BitmapImage> Memory = new();
    private const int MemoryCap = 300;
    private const long DirCapBytes = 200L * 1024 * 1024;
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(30);

    private static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HDREZKA", "postercache");

    static PosterCache()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("HDREZKA/1.0.0 (Windows; WinUI)");
        _ = Task.Run(CleanupAsync);
    }

    public static void SetSource(Image image, string? url)
    {
        if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            image.Source = null;
            image.Tag = null;
            return;
        }

        var key = url;
        image.Tag = key;
        if (Memory.TryGetValue(key, out var cached))
        {
            image.Source = cached;
            return;
        }

        var queue = image.DispatcherQueue;
        _ = Task.Run(async () =>
        {
            string path;
            try
            {
                path = await GetFileAsync(key).ConfigureAwait(false);
            }
            catch
            {
                return;
            }

            queue.TryEnqueue(() =>
            {
                try
                {
                    if ((string?)image.Tag != key) return; // recycled for another item
                    var bmp = Memory.GetOrAdd(key, _ => new BitmapImage(new Uri(path)));
                    image.Source = bmp;
                    if (Memory.Count > MemoryCap)
                    {
                        foreach (var k in Memory.Keys.Take(Memory.Count - 200).ToList())
                        {
                            Memory.TryRemove(k, out _);
                        }
                    }
                }
                catch
                {
                }
            });
        });
    }

    private static async Task<string> GetFileAsync(string url)
    {
        Directory.CreateDirectory(Dir);
        var ext = Path.GetExtension(new Uri(url).AbsolutePath).ToLowerInvariant();
        if (ext is not (".jpg" or ".jpeg" or ".png" or ".webp" or ".gif" or ".bmp")) ext = ".jpg";
        var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))) + ext;
        var path = Path.Combine(Dir, name);

        var info = new FileInfo(path);
        if (info.Exists && info.Length > 0 && DateTime.UtcNow - info.LastWriteTimeUtc < MaxAge)
        {
            return path;
        }

        using var resp = await Http.GetAsync(url).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        using var net = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);
        var tmp = path + ".tmp";
        using (var file = File.Create(tmp))
        {
            await net.CopyToAsync(file).ConfigureAwait(false);
        }

        File.Move(tmp, path, overwrite: true);
        return path;
    }

    private static async Task CleanupAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            if (!Directory.Exists(Dir)) return;
            var files = new DirectoryInfo(Dir).GetFiles()
                .OrderBy(f => f.LastWriteTimeUtc)
                .ToList();
            foreach (var f in files.Where(f => DateTime.UtcNow - f.LastWriteTimeUtc > MaxAge))
            {
                try { f.Delete(); } catch { }
            }

            files = new DirectoryInfo(Dir).GetFiles().OrderBy(f => f.LastWriteTimeUtc).ToList();
            long total = files.Sum(f => f.Length);
            foreach (var f in files)
            {
                if (total <= DirCapBytes) break;
                try
                {
                    total -= f.Length;
                    f.Delete();
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
    }
}
