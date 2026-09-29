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
            var file = Path.Combine(dir, "crash.log");
            var sb = new StringBuilder();
            sb.AppendLine("=== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            sb.AppendLine(ex.ToString());
            sb.AppendLine();
            File.AppendAllText(file, sb.ToString());

            // Rotation: keep the log from growing forever (cap ~512KB, keep tail).
            try
            {
                var info = new FileInfo(file);
                if (info.Length > 512 * 1024)
                {
                    var lines = File.ReadAllLines(file);
                    File.WriteAllLines(file, lines.Skip(Math.Max(0, lines.Length - 500)));
                }
            }
            catch
            {
            }
        }
        catch
        {
        }
    }

    protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        SettingsService.Instance.Load();
        Services.ProtocolService.Register();

        // Single instance: a second process (e.g. toast protocol click)
        // forwards activation to the running one and exits.
        var keyInstance = Microsoft.Windows.AppLifecycle.AppInstance.FindOrRegisterForKey("HDREZKA-main");
        if (!keyInstance.IsCurrent)
        {
            try
            {
                // The redirected event loses the URI for unpackaged apps,
                // so leave it in a pending file for the main instance.
                WritePendingProtocolUri();
                await keyInstance.RedirectActivationToAsync(
                    Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().GetActivatedEventArgs());
            }
            catch (Exception ex)
            {
                TryLog(ex);
            }

            Exit();
            return;
        }

        RezkaService.Instance.RestoreSession();
        LocalizationService.Instance.Apply(SettingsService.Instance.Language);
        ThemeHelper.Apply(SettingsService.Instance.Theme);

        MainWindow = new MainWindow();
        MainWindow.Activate();

        keyInstance.Activated += OnAppInstanceActivated;

        // Cold start via hdrezka://details?path=... (toast button).
        var pendingDetails = GetProtocolDetailsPath(
            Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().GetActivatedEventArgs());
        if (pendingDetails != null)
        {
            OpenDetails(pendingDetails);
        }

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

    private void OnAppInstanceActivated(object? sender, Microsoft.Windows.AppLifecycle.AppActivationArguments args)
    {
        try { TryLog(new Exception("[Protocol] redirected activation")); } catch { }
        var path = GetProtocolDetailsPath(args) ?? TakePendingProtocolUri();
        if (path == null || MainWindow == null) return;
        MainWindow.DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                MainWindow.Activate();
                OpenDetails(path);
            }
            catch (Exception ex)
            {
                TryLog(ex);
            }
        });
    }

    private static void OpenDetails(string pagePath)
    {
        try
        {
            Services.Nav.Go<Views.DetailsPage>(new Core.Api.MovieSimple(Id: pagePath));
        }
        catch (Exception ex)
        {
            TryLog(ex);
        }
    }

    private static string PendingProtocolPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "HDREZKA", "protocol-pending.txt");

    private static void WritePendingProtocolUri()
    {
        try
        {
            var arg = Environment.GetCommandLineArgs()
                .FirstOrDefault(a => a.StartsWith(
                    Services.ProtocolService.Scheme + "://", StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrEmpty(arg)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(PendingProtocolPath)!);
            File.AppendAllLines(PendingProtocolPath, [arg.Trim().Trim('"')]);
        }
        catch (Exception ex)
        {
            TryLog(ex);
        }
    }

    private static string? TakePendingProtocolUri()
    {
        try
        {
            if (!File.Exists(PendingProtocolPath)) return null;
            var lines = File.ReadAllLines(PendingProtocolPath);
            try { File.Delete(PendingProtocolPath); } catch { }
            for (var i = lines.Length - 1; i >= 0; i--)
            {
                var line = lines[i];
                if (Uri.TryCreate(line.Trim(), UriKind.Absolute, out var uri))
                {
                    var path = Services.ProtocolService.ParseDetailsPath(uri);
                    if (path != null)
                    {
                        TryLog(new Exception($"[Protocol] pending uri={uri}"));
                        return path;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            TryLog(ex);
        }

        return null;
    }

    private static string? GetProtocolDetailsPath(Microsoft.Windows.AppLifecycle.AppActivationArguments args)
    {
        try
        {
            TryLog(new Exception($"[Protocol] kind={args.Kind}"));
            // COM path (packaged) or interface-based activation.
            if (args.Data is Windows.ApplicationModel.Activation.IProtocolActivatedEventArgs proto)
            {
                TryLog(new Exception($"[Protocol] uri={proto.Uri}"));
                return Services.ProtocolService.ParseDetailsPath(proto.Uri);
            }

            // Unpackaged fallback: shell launches the exe with the URI as a
            // plain command-line argument, reported as a normal Launch.
            // Scan both the activation args and the raw command line.
            string? cmd = null;
            try
            {
                if (args.Data is Windows.ApplicationModel.Activation.LaunchActivatedEventArgs launch)
                {
                    cmd = launch.Arguments;
                }
            }
            catch
            {
            }

            cmd ??= Environment.CommandLine;
            if (!string.IsNullOrEmpty(cmd))
            {
                var start = cmd.IndexOf(Services.ProtocolService.Scheme + "://", StringComparison.OrdinalIgnoreCase);
                if (start >= 0)
                {
                    var end = cmd.IndexOf('"', start);
                    var raw = (end > start ? cmd.Substring(start, end - start) : cmd[start..]).Trim().Trim('"');
                    if (Uri.TryCreate(raw, UriKind.Absolute, out var uri))
                    {
                        TryLog(new Exception($"[Protocol] uri={uri}"));
                        return Services.ProtocolService.ParseDetailsPath(uri);
                    }
                }
            }

            if (args.Data != null)
            {
                TryLog(new Exception($"[Protocol] data type={args.Data.GetType().FullName}"));
            }

            return null;
        }
        catch
        {
            return null;
        }
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
