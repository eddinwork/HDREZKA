using HDREZKA.App.Services;
using HDREZKA.App.Views;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace HDREZKA.App;

public sealed partial class MainWindow : Window
{
    public string SearchText
    {
        get => _searchText;
        set
        {
            _searchText = value;
            Bindings.Update();
        }
    }

    private string _searchText = "";
    private bool _refreshingTitleBar;

    public MainWindow()
    {
        InitializeComponent();

        Title = "HDREZKA";
        ExtendsContentIntoTitleBar = true;
        SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();

        try
        {
            AppWindow.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "logo.ico"));
        }
        catch (Exception ex)
        {
            App.TryLog(ex);
        }

        ThemeHelper.SetRoot(RootGrid);
        RootGrid.ActualThemeChanged += (_, _) =>
        {
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, RefreshCaptionButtons);
        };
        try
        {
            SetTitleBar(CustomTitleBar);
        }
        catch (Exception ex)
        {
            App.TryLog(ex);
        }
        RootGrid.SizeChanged += (_, _) =>
        {
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, RefreshTitleBarLayout);
        };
        AppWindow.Changed += (_, e) =>
        {
            if (e.DidPresenterChange || e.DidSizeChange)
            {
                DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, RefreshTitleBarLayout);
            }
        };
        RefreshTitleBarLayout();
        RefreshCaptionButtons();
        Nav.SetFrame(ContentFrame);
        ContentFrame.Navigated += ContentFrame_Navigated;
        LocalizationService.Instance.LanguageChanged += ApplyLocalization;
        RezkaService.Instance.AuthStateChanged += () => DispatcherQueue.TryEnqueue(ApplyAccountState);
        TrackedSeriesService.UpdatesChanged += () => DispatcherQueue.TryEnqueue(UpdateBadge);
        DownloadService.Instance.Changed += () => DispatcherQueue.TryEnqueue(UpdateDownloadsButton);
        ApplyLocalization();
        ApplyAccountState();
        UpdateBadge();
        UpdateDownloadsButton();

        ContentFrame.Navigate(typeof(HomePage));
        NavView.SelectedItem = NavHome;
    }

    public bool IsTheaterMode { get; private set; }

    public void SetTheaterMode(bool on)
    {
        if (IsTheaterMode == on) return;
        IsTheaterMode = on;
        NavView.IsPaneVisible = !on;
        NavView.IsPaneToggleButtonVisible = !on;
        NavView.IsBackButtonVisible = on
            ? NavigationViewBackButtonVisible.Collapsed
            : NavigationViewBackButtonVisible.Auto;
        RefreshCaptionButtons();

        try
        {
            CustomTitleBar.Background = on
                ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Black)
                : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }
        catch
        {
        }
    }

    /// <summary>
    /// Keeps the custom title bar aligned with system caption buttons:
    /// reserves insets, matches the system height and re-applies the drag
    /// region. Must run on launch, SizeChanged and every presenter change,
    /// because SetPresenter resets drag rectangles.
    /// </summary>

     private void RefreshTitleBarLayout()
    {
        if (_refreshingTitleBar) return;
        _refreshingTitleBar = true;
        try
        {
            var tb = AppWindow.TitleBar;
            if (tb is null) return;

            CustomTitleBar.Height = tb.Height;
            LeftInsetColumn.Width = new GridLength(Math.Max(0, tb.LeftInset));
            RightInsetColumn.Width = new GridLength(Math.Max(0, tb.RightInset));

            double scale = 1.0;
            try { scale = RootGrid.XamlRoot?.RasterizationScale ?? 1.0; } catch { }

            var dragWidth = Math.Max(0, CustomTitleBar.ActualWidth - tb.LeftInset - tb.RightInset);
            if (dragWidth > 0 && tb.Height > 0)
            {
                tb.SetDragRectangles(new[]
                {
                    new Windows.Graphics.RectInt32(
                        (int)(tb.LeftInset * scale), 0,
                        (int)(dragWidth * scale), (int)(tb.Height * scale)),
                });
            }
        }
        catch { }
        finally { _refreshingTitleBar = false; }
    }

    /// <summary>
    /// Single place for caption button colors. Theater mode forces the black
    /// scheme; otherwise colors follow the effective theme — the system
    /// defaults leave _ □ X nearly invisible on the light theme.
    /// </summary>
    private void RefreshCaptionButtons()
    {
        try
        {
            var tb = AppWindow.TitleBar;
            if (IsTheaterMode)
            {
                tb.BackgroundColor = Microsoft.UI.Colors.Black;
                tb.InactiveBackgroundColor = Microsoft.UI.Colors.Black;
                tb.ForegroundColor = Microsoft.UI.Colors.White;
                tb.InactiveForegroundColor = Microsoft.UI.Colors.Gray;
                tb.ButtonBackgroundColor = Microsoft.UI.Colors.Black;
                tb.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Black;
                tb.ButtonForegroundColor = Microsoft.UI.Colors.White;
                tb.ButtonInactiveForegroundColor = Microsoft.UI.Colors.Gray;
                tb.ButtonHoverBackgroundColor = Microsoft.UI.Colors.DimGray;
                tb.ButtonHoverForegroundColor = Microsoft.UI.Colors.White;
                return;
            }

            var dark = RootGrid.ActualTheme == ElementTheme.Dark;
            var fg = dark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
            var fgDim = dark ? Microsoft.UI.Colors.Gray : Microsoft.UI.Colors.DimGray;
            tb.BackgroundColor = null;
            tb.InactiveBackgroundColor = null;
            tb.ForegroundColor = null;
            tb.InactiveForegroundColor = null;
            tb.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
            tb.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
            tb.ButtonForegroundColor = fg;
            tb.ButtonInactiveForegroundColor = fgDim;
            tb.ButtonHoverForegroundColor = fg;
            tb.ButtonPressedForegroundColor = fg;
            tb.ButtonHoverBackgroundColor = dark
                ? Microsoft.UI.Colors.DimGray
                : Windows.UI.Color.FromArgb(0xFF, 0xE5, 0xE5, 0xE5);
            tb.ButtonPressedBackgroundColor = dark
                ? Microsoft.UI.Colors.Gray
                : Windows.UI.Color.FromArgb(0xFF, 0xCC, 0xCC, 0xCC);
        }
        catch
        {
            // title bar theming is best effort
        }
    }

    private void ApplyLocalization()
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                NavHome.Content = Loc.Get("Nav.Home");
                NavCatalog.Content = Loc.Get("Nav.Catalog");
                NavBookmarks.Content = Loc.Get("Nav.Bookmarks");
                NavContinue.Content = Loc.Get("Nav.Continue");
                NavCollections.Content = Loc.Get("Nav.Collections");
                NavUpdates.Content = Loc.Get("Nav.Updates");
                NavAccount.Content = Loc.Get("Nav.Account");
                NavSettings.Content = Loc.Get("Nav.Settings");
                SearchBox.PlaceholderText = Loc.Get("Search.Placeholder");
                ApplyAccountState();
            });
        }

    private void ApplyAccountState()
    {
        NavAccount.Content = RezkaService.Instance.IsLoggedIn
            ? Loc.Get("Nav.Account")
            : Loc.Get("Common.Login");
    }

    private void UpdateBadge()
    {
        try
        {
            var count = TrackedSeriesService.UnreadCount;
            UpdatesBadge.Value = count;
            UpdatesBadge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch
        {
        }
    }

    private void UpdateDownloadsButton()
    {
        try
        {
            var active = DownloadService.Instance.Jobs
                .Where(j => j.State is DownloadState.Queued or DownloadState.Resolving or DownloadState.Downloading)
                .ToList();
            DownloadsFab.Visibility = active.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (active.Count == 0) return;
            DownloadsCountText.Text = active.Count.ToString();
            DownloadsRing.Value = active.Average(j => j.State == DownloadState.Downloading ? j.Progress : 0);
            ToolTipService.SetToolTip(DownloadsFab, $"{Loc.Get("Downloads.Title")} ({active.Count})");
        }
        catch
        {
        }
    }

    private async void DownloadsButton_Click(object sender, RoutedEventArgs e)
    {
        await Views.DownloadDialog.ShowActiveAsync(ContentFrame.XamlRoot);
    }

private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
        {
            NavView.IsBackEnabled = ContentFrame.CanGoBack;
            SetTheaterMode(e.SourcePageType == typeof(PlayerPage));

            var tag = e.SourcePageType switch
            {
                var t when t == typeof(HomePage) => "Home",
                var t when t == typeof(CatalogPage) => "Catalog",
                var t when t == typeof(BookmarksPage) => "Bookmarks",
                var t when t == typeof(SettingsPage) => "Settings",
                var t when t == typeof(ContinueWatchingPage) => "Continue",
                var t when t == typeof(CollectionsPage) => "Collections",
                var t when t == typeof(UpdatesPage) => "Updates",
                var t when t == typeof(AccountPage) => "Account",
                _ => (string?)null,
            };

        if (tag == null)
        {
            NavView.SelectedItem = null;
        }
else
            {
                NavView.SelectedItem = tag switch
                {
                    "Home" => NavHome,
                    "Catalog" => NavCatalog,
                    "Bookmarks" => NavBookmarks,
                    "Settings" => NavSettings,
                    "Continue" => NavContinue,
                    "Collections" => NavCollections,
                    "Updates" => NavUpdates,
                    "Account" => NavAccount,
                    _ => null,
                };
            }
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item || item.Tag is not string tag) return;

        switch (tag)
        {
            case "Home":
                if (ContentFrame.Content is not HomePage) ContentFrame.Navigate(typeof(HomePage));
                break;
            case "Catalog":
                if (ContentFrame.Content is not CatalogPage) ContentFrame.Navigate(typeof(CatalogPage));
                break;
            case "Bookmarks":
                if (!RezkaService.Instance.IsLoggedIn)
                {
                    _ = ShowLoginDialog();
                    sender.SelectedItem = null;
                    return;
                }
                if (ContentFrame.Content is not BookmarksPage) ContentFrame.Navigate(typeof(BookmarksPage));
                break;
            case "Continue":
                if (!RezkaService.Instance.IsLoggedIn)
                {
                    _ = ShowLoginDialog();
                    sender.SelectedItem = null;
                    return;
                }
                if (ContentFrame.Content is not ContinueWatchingPage) ContentFrame.Navigate(typeof(ContinueWatchingPage));
                break;
            case "Settings":
                if (ContentFrame.Content is not SettingsPage) ContentFrame.Navigate(typeof(SettingsPage));
                break;
            case "Collections":
                if (ContentFrame.Content is not CollectionsPage) ContentFrame.Navigate(typeof(CollectionsPage));
                break;
            case "Updates":
                if (ContentFrame.Content is not UpdatesPage) ContentFrame.Navigate(typeof(UpdatesPage));
                break;
            case "Account":
                if (RezkaService.Instance.IsLoggedIn)
                {
                    if (ContentFrame.Content is not AccountPage) ContentFrame.Navigate(typeof(AccountPage));
                }
                else
                {
                    sender.SelectedItem = null;
                    _ = ShowLoginDialog();
                }
                break;
        }
    }

    public async Task ShowLoginDialog()
    {
        var dialog = new LoginDialog
        {
            XamlRoot = ContentFrame.XamlRoot,
        };
        await dialog.ShowAsync();
    }

    private void NavView_BackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args)
    {
        if (ContentFrame.CanGoBack)
        {
            ContentFrame.GoBack();
        }
    }

    private void NavView_DisplayModeChanged(NavigationView sender, NavigationViewDisplayModeChangedEventArgs args)
    {
        // reserved for adaptive tweaks
    }

    private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var query = args.QueryText.Trim();
        if (query.Length < 2)
        {
            sender.Text = "";
            return;
        }

        ContentFrame.Navigate(typeof(SearchPage), query);
        sender.Text = "";
    }
}
