using HDREZKA.App.Services;
using HDREZKA.Core.Api;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.AppNotifications.Builder;
using Windows.Storage;

namespace HDREZKA.App.Views;

public static class DownloadJobs
{
    public static DownloadJob Episode(
        MovieDetailed details,
        MovieVoiceActing voice,
        MovieSeason? season,
        MovieEpisode? episode,
        string quality)
    {
        var suffix = season != null && episode != null ? $" S{season.Name}E{episode.Name}" : "";
        var file = DownloadService.SanitizeFileName(details.Name + suffix) + ".mp4";
        var folder = Path.Combine(
            DownloadService.DownloadFolder,
            DownloadService.SanitizeFileName(details.Name));
        return new DownloadJob
        {
            Title = details.Name,
            Label = suffix.Trim(),
            FileName = file,
            FilePath = Path.Combine(folder, file),
            ResolveUrl = ct => ResolveAsync(voice, season, episode, details.Favs, quality, ct),
        };
    }

    public static IEnumerable<DownloadJob> Season(
        MovieDetailed details,
        MovieVoiceActing voice,
        MovieSeason season,
        string quality) =>
        season.Episodes.Select(ep => Episode(details, voice, season, ep, quality)).ToList();

    private static async Task<string?> ResolveAsync(
        MovieVoiceActing voice,
        MovieSeason? season,
        MovieEpisode? episode,
        string favs,
        string quality,
        CancellationToken ct)
    {
        var video = await RezkaService.Instance.Client
            .GetMovieVideoAsync(voice, season, episode, favs, ct)
            .ConfigureAwait(false);
        var track = video.GetClosestTo(quality) ?? video.GetMaxQuality();
        return track?.Urls.FirstOrDefault(u => u.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase));
    }
}

public static class DownloadDialog
{
    public static async Task ShowLockedAsync(XamlRoot? root)
    {
        if (root == null) return;
        var dialog = new ContentDialog
        {
            Title = Loc.Get("Downloads.Title"),
            Content = Loc.Get("Downloads.Locked"),
            CloseButtonText = "OK",
            XamlRoot = root,
        };
        await dialog.ShowAsync();
    }

    public static async Task<string?> PickQualityAsync(XamlRoot? root, string current)
    {
        if (root == null) return null;
        var box = new ComboBox { MinWidth = 200 };
        foreach (var q in DownloadService.Qualities)
        {
            box.Items.Add(new ComboBoxItem { Content = q, Tag = q });
        }

        box.SelectedIndex = Math.Max(0, DownloadService.Qualities
            .ToList()
            .FindIndex(q => q.Equals(current, StringComparison.OrdinalIgnoreCase)));
        var dialog = new ContentDialog
        {
            Title = Loc.Get("Downloads.Quality"),
            Content = box,
            PrimaryButtonText = "OK",
            CloseButtonText = Loc.Get("Common.Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = root,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;
        return (box.SelectedItem as ComboBoxItem)?.Tag as string;
    }

    /// <summary>Enqueue a new batch and show progress. Closing the dialog
    /// keeps downloads running; a toast summarizes when the batch ends.</summary>
    public static async Task RunAsync(XamlRoot? root, IReadOnlyList<DownloadJob> jobs)
    {
        if (root == null || jobs.Count == 0) return;
        DownloadService.Instance.Enqueue(jobs);
        await ShowInternalAsync(root, jobs);
        _ = WatchBatchAsync(jobs);
    }

    /// <summary>Show progress of currently active downloads (no new jobs).</summary>
    public static Task ShowActiveAsync(XamlRoot? root)
    {
        if (root == null) return Task.CompletedTask;
        return ShowInternalAsync(root, null);
    }

    private static async Task ShowInternalAsync(XamlRoot root, IReadOnlyList<DownloadJob>? batch)
    {
        var list = new StackPanel { Spacing = 10, MinWidth = 340 };
        var folderText = new TextBlock
        {
            Text = $"{Loc.Get("Downloads.Folder")} {DownloadService.DownloadFolder}",
            FontSize = 12,
            Style = Application.Current.Resources["SecondaryText"] as Style,
            TextWrapping = TextWrapping.Wrap,
        };
        var changeFolderButton = new Button { Content = Loc.Get("Downloads.Change") };
        changeFolderButton.Click += async (_, _) =>
        {
            var picked = await PickFolderAsync();
            if (picked == null) return;
            SettingsService.Instance.DownloadFolder = picked;
            SettingsService.Instance.Save();
            try { folderText.Text = $"{Loc.Get("Downloads.Folder")} {DownloadService.DownloadFolder}"; } catch { }
        };
        var pauseButton = new Button { Content = Loc.Get("Downloads.Pause") };
        pauseButton.Click += (_, _) =>
        {
            DownloadService.Instance.SetPaused(!DownloadService.Instance.IsPaused);
            try { pauseButton.Content = Loc.Get(DownloadService.Instance.IsPaused ? "Downloads.Resume" : "Downloads.Pause"); } catch { }
        };
        var openFolderButton = new Button { Content = Loc.Get("Downloads.OpenFolder") };
        openFolderButton.Click += async (_, _) =>
        {
            try
            {
                var folder = await StorageFolder.GetFolderFromPathAsync(DownloadService.DownloadFolder);
                await Windows.System.Launcher.LaunchFolderAsync(folder);
            }
            catch (Exception ex)
            {
                App.TryLog(ex);
            }
        };
        var topRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        topRow.Children.Add(changeFolderButton);
        topRow.Children.Add(pauseButton);
        topRow.Children.Add(openFolderButton);

        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(folderText);
        content.Children.Add(topRow);
        content.Children.Add(list);

        var dialog = new ContentDialog
        {
            Title = Loc.Get("Downloads.Title"),
            Content = new ScrollViewer { Content = content, MaxHeight = 440 },
            PrimaryButtonText = Loc.Get("Downloads.Cancel"),
            CloseButtonText = "OK",
            XamlRoot = root,
        };

        IReadOnlyList<DownloadJob> CurrentJobs() =>
            batch ?? DownloadService.Instance.Jobs
                .Where(j => j.State is DownloadState.Queued or DownloadState.Resolving
                    or DownloadState.Downloading or DownloadState.Done
                    or DownloadState.Failed or DownloadState.Skipped or DownloadState.Cancelled)
                .ToList();

        var rows = new Dictionary<DownloadJob, (ProgressBar Bar, TextBlock Status)>();
        void EnsureRows()
        {
            foreach (var job in CurrentJobs())
            {
                if (rows.ContainsKey(job)) continue;
                var row = new StackPanel { Spacing = 4 };
                row.Children.Add(new TextBlock
                {
                    Text = string.IsNullOrEmpty(job.Label) ? job.Title : $"{job.Title} · {job.Label}",
                    TextWrapping = TextWrapping.Wrap,
                });
                var bar = new ProgressBar { Minimum = 0, Maximum = 100, Value = job.Progress };
                var status = new TextBlock
                {
                    Text = StatusText(job),
                    FontSize = 12,
                    Style = Application.Current.Resources["SecondaryText"] as Style,
                };
                row.Children.Add(bar);
                row.Children.Add(status);
                list.Children.Add(row);
                rows[job] = (bar, status);
            }
        }

        void RefreshRows()
        {
            foreach (var (job, (bar, status)) in rows)
            {
                bar.Value = job.State == DownloadState.Done ? 100 : job.Progress;
                status.Text = StatusText(job);
            }
        }

        void OnChanged()
        {
            try
            {
                list.DispatcherQueue.TryEnqueue(() =>
                {
                    try
                    {
                        EnsureRows();
                        RefreshRows();
                    }
                    catch
                    {
                    }
                });
            }
            catch
            {
            }
        }

        DownloadService.Instance.Changed += OnChanged;
        try
        {
            EnsureRows();
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                DownloadService.Instance.CancelActive();
            }
        }
        finally
        {
            DownloadService.Instance.Changed -= OnChanged;
        }
    }

    private static async Task WatchBatchAsync(IReadOnlyList<DownloadJob> jobs)
    {
        try
        {
            while (true)
            {
                await Task.Delay(1000).ConfigureAwait(false);
                if (jobs.All(j => j.State is DownloadState.Done or DownloadState.Failed
                        or DownloadState.Skipped or DownloadState.Cancelled))
                {
                    break;
                }
            }

            var done = jobs.Count(j => j.State == DownloadState.Done);
            var skipped = jobs.Count(j => j.State == DownloadState.Skipped);
            var failed = jobs.Count(j => j.State == DownloadState.Failed);
            var cancelled = jobs.Count(j => j.State == DownloadState.Cancelled);
            var notification = new AppNotificationBuilder()
                .AddText(Loc.Get("Downloads.FinishedTitle"))
                .AddText(Loc.Get("Downloads.FinishedText", done, skipped, failed, cancelled))
                .BuildNotification();
            Microsoft.Windows.AppNotifications.AppNotificationManager.Default.Show(notification);
        }
        catch (Exception ex)
        {
            App.TryLog(ex);
        }
    }

    private static string StatusText(DownloadJob job) => job.State switch
    {
        DownloadState.Done => Loc.Get("Downloads.Done"),
        DownloadState.Failed => $"{Loc.Get("Downloads.Failed")}: {job.Error}",
        DownloadState.Skipped => Loc.Get("Downloads.Skipped"),
        DownloadState.Cancelled => Loc.Get("Downloads.Cancel"),
        DownloadState.Resolving => "…",
        _ => $"{job.Progress:0}%",
    };

    private static async Task<string?> PickFolderAsync()
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FolderPicker();
            picker.FileTypeFilter.Add("*");
            var window = App.MainWindow;
            if (window == null) return null;
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            var folder = await picker.PickSingleFolderAsync();
            return folder?.Path;
        }
        catch (Exception ex)
        {
            App.TryLog(ex);
            return null;
        }
    }
}
