using HDREZKA.App.Views;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace HDREZKA.App;

/// <summary>
/// Separate window for video playback.
/// Uses the default system title bar on purpose: no ExtendsContentIntoTitleBar,
/// no custom drag rectangles, so fullscreen transitions cannot break the main
/// window layout. Only one player window exists at a time.
/// </summary>
public sealed partial class PlayerWindow : Window
{
    public static new PlayerWindow? Current { get; private set; }

    public PlayerWindow()
    {
        // Replace any existing player window instead of stacking them.
        try { Current?.Close(); } catch { }
        Current = this;

        InitializeComponent();

        // No system title bar at all: close/pin live in the app top bar,
        // the window keeps only a resizable border. Content extends
        // into the title area from the start.
        ExtendsContentIntoTitleBar = true;

        Title = "HDREZKA Player";

        try
        {
            AppWindow.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "logo.ico"));
        }
        catch (Exception ex)
        {
            App.TryLog(ex);
        }

        try
        {
            const int width = 1280;
            const int height = 720;
            AppWindow.Resize(new SizeInt32(width, height));

            var display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
            var area = display.WorkArea;
            AppWindow.Move(new PointInt32(
                area.X + Math.Max(0, (area.Width - width) / 2),
                area.Y + Math.Max(0, (area.Height - height) / 2)));
        }
        catch (Exception ex)
        {
            App.TryLog(ex);
        }

        Closed += OnClosed;
        ApplyChromePolicy();
    }

    /// <summary>
    /// Chromeless player: no system title bar (close/pin are custom buttons
    /// in the app top bar), keeps the resizable border, min/max disabled.
    /// Must be re-applied after every SetPresenter call — it creates
    /// a new presenter with default flags.
    /// </summary>
    public void ApplyChromePolicy()
    {
        try
        {
            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.SetBorderAndTitleBar(true, false);
                presenter.IsMinimizable = false;
                presenter.IsMaximizable = false;
            }
        }
        catch (Exception ex)
        {
            App.TryLog(ex);
        }
    }

    public void ShowPlayer(PlayerLaunch launch)    {
        try
        {
            Title = string.IsNullOrWhiteSpace(launch.Details.Name)
                ? "HDREZKA Player"
                : launch.Details.Name;
        }
        catch
        {
        }

        PlayerFrame.Navigate(typeof(PlayerPage), launch);
        if (PlayerFrame.Content is PlayerPage page)
        {
            page.HostWindow = this;
        }

        Activate();
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        if (ReferenceEquals(Current, this))
        {
            Current = null;
        }

        try
        {
            if (PlayerFrame.Content is PlayerPage page)
            {
                page.Shutdown();
            }
        }
        catch (Exception ex)
        {
            App.TryLog(ex);
        }
    }
}
