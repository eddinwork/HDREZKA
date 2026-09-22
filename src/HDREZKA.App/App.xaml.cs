using HDREZKA.App.Services;
using Microsoft.UI.Xaml;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace HDREZKA.App;

public partial class App : Application
{
    public static MainWindow? MainWindow { get; private set; }

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        TryLog(e.Exception);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        TryLog(e.Exception);
    }

    public static void TryLog(Exception ex)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HDREZKA");
            Directory.CreateDirectory(dir);
            var sb = new StringBuilder();
            sb.AppendLine("=== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            sb.AppendLine(ex.ToString());
            sb.AppendLine();
            File.AppendAllText(Path.Combine(dir, "crash.log"), sb.ToString());
        }
        catch
        {
        }
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        SettingsService.Instance.Load();
        RezkaService.Instance.RestoreSession();
        LocalizationService.Instance.Apply(SettingsService.Instance.Language);
        ThemeHelper.Apply(SettingsService.Instance.Theme);

        MainWindow = new MainWindow();
        MainWindow.Activate();

        // Update check on every launch (notifies at most once per version).
        _ = Services.UpdateService.CheckAndNotifyAsync(MainWindow);

        // First-launch mirror autopick (silent, skips logged-in sessions).
        _ = Services.MirrorService.AutoPickOnStartupAsync();

        // New-episode notifications for watched series (delayed, background).
        _ = Services.TrackedSeriesService.CheckOnStartupAsync();

        // If the previous session died mid-playback (marker left behind),
        // tell the user it's most likely the video driver/overlay.
        _ = CheckLastPlaybackCrashAsync(MainWindow);
    }

    private static async Task CheckLastPlaybackCrashAsync(MainWindow window)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            var title = Views.PlayerPage.TakePlaybackMarker();
            if (title == null) return;
            try { TryLog(new Exception("[Crash] showing aftermath dialog for " + title)); } catch { }

            var tcs = new TaskCompletionSource();
            var enqueued = window.DispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    var xamlRoot = window.Content?.XamlRoot;
                    if (xamlRoot == null) return;
                    var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
                    {
                        Title = Services.Loc.Get("Crash.Title"),
                        Content = Services.Loc.Get("Crash.Text", title),
                        CloseButtonText = "OK",
                        DefaultButton = Microsoft.UI.Xaml.Controls.ContentDialogButton.Close,
                        XamlRoot = xamlRoot,
                    };
                    await dialog.ShowAsync();
                }
                catch (Exception ex)
                {
                    TryLog(ex);
                }
                finally
                {
                    tcs.TrySetResult();
                }
            });
            if (!enqueued)
            {
                return;
            }

            await tcs.Task.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            TryLog(ex);
        }
    }
}
