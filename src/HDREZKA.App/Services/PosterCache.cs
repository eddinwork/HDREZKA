using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using HDREZKA.Core.Api;
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

    // Один URL — одна загрузка, даже если его одновременно просят 5 карточек.
    private static readonly ConcurrentDictionary<string, Task<string>> InFlight =
        new(StringComparer.OrdinalIgnoreCase);

    // Ограничение параллелизма: GridView при быстром скролле просит десятки
    // постеров разом, без гейта это шторм из HTTP-запросов и декодирований.
    private static readonly SemaphoreSlim DownloadGate = new(6, 6);

    private const int MemoryCap = 160;
    private const int DecodeWidth = 360;
    private const long DirCapBytes = 200L * 1024 * 1024;
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(30);

    private static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HDREZKA", "postercache");

    static PosterCache()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd(BuildInfo.UserAgent);
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
                path = await GetFileForUrlAsync(key).ConfigureAwait(false);
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
                    var bmp = Memory.GetOrAdd(key, _ =>
                    {
                        // Декодируем в размер карточки (×2 для HiDPI), а не в
                        // оригинал: постеры на сайте бывают 500+ px, и 300 таких
                        // в памяти — это сотни мегабайт.
                        var decoded = new BitmapImage { DecodePixelWidth = DecodeWidth };
                        decoded.UriSource = new Uri(path);
                        return decoded;
                    });
                    image.Source = bmp;
                    if (Memory.Count > MemoryCap)
                    {
                        foreach (var k in Memory.Keys.Take(Memory.Count - MemoryCap / 2).ToList())
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

    private static Task<string> GetFileForUrlAsync(string url) =>
        InFlight.GetOrAdd(url, static key => DownloadAndForgetAsync(key));

    private static async Task<string> DownloadAndForgetAsync(string url)
    {
        try
        {
            return await GetFileAsync(url).ConfigureAwait(false);
        }
        finally
        {
            InFlight.TryRemove(url, out _);
        }
    }

    private static async Task<string> GetFileAsync(string url)
    {
        Directory.CreateDirectory(Dir);
        var ext = Path.GetExtension(new Uri(url).AbsolutePath).ToLowerInvariant();
        if (ext is not (".jpg" or ".jpeg" or ".png" or ".webp" or ".gif" or ".bmp")) ext = ".jpg";
        var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))) + ext;
        var path = Path.Combine(Dir, name);

        if (IsFresh(path)) return path;

        await DownloadGate.WaitAsync().ConfigureAwait(false);
        try
        {
            // Пока ждали слот, файл мог скачать соседний поток.
            if (IsFresh(path)) return path;

            using var resp = await Http.GetAsync(url).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            using var net = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);

            // Уникальный временный файл: два потока с одним ".tmp" ломали
            // и файл, и его последующее 30-дневное кэширование.
            var tmp = $"{path}.{Guid.NewGuid():N}.tmp";
            try
            {
                using (var file = File.Create(tmp))
                {
                    await net.CopyToAsync(file).ConfigureAwait(false);
                }

                File.Move(tmp, path, overwrite: true);
            }
            catch
            {
                try { File.Delete(tmp); } catch { }
                throw;
            }

            return path;
        }
        finally
        {
            DownloadGate.Release();
        }
    }

    private static bool IsFresh(string path)
    {
        var info = new FileInfo(path);
        return info.Exists && info.Length > 0 && DateTime.UtcNow - info.LastWriteTimeUtc < MaxAge;
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
