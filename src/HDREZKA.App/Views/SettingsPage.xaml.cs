using HDREZKA.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace HDREZKA.App.Views;

public sealed partial class SettingsPage : Page
{
    private bool _initialized;

    private const string DonateAddress = "UQBNd1gXEZi4oahqNeJEy18KCUXfVvmBnPgXlokPpzU_PbFQ";

    public SettingsPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ApplyLocalization();
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;

        if (_initialized) return;

        var settings = SettingsService.Instance;
        MirrorBox.Text = settings.Mirror;

        LanguageBox.Items.Clear();
        LanguageBox.Items.Add(new ComboBoxItem { Content = "Русский", Tag = AppLanguage.Ru });
        LanguageBox.Items.Add(new ComboBoxItem { Content = "English", Tag = AppLanguage.En });
        LanguageBox.Items.Add(new ComboBoxItem { Content = "Українська", Tag = AppLanguage.Uk });
        LanguageBox.SelectedIndex = (int)settings.Language;

        ThemeBox.Items.Clear();
        ThemeBox.Items.Add(new ComboBoxItem { Content = Loc.Get("Settings.ThemeSystem"), Tag = AppTheme.System });
        ThemeBox.Items.Add(new ComboBoxItem { Content = Loc.Get("Settings.ThemeLight"), Tag = AppTheme.Light });
        ThemeBox.Items.Add(new ComboBoxItem { Content = Loc.Get("Settings.ThemeDark"), Tag = AppTheme.Dark });
        ThemeBox.SelectedIndex = (int)settings.Theme;

        QualityBox.Items.Clear();
        foreach (var quality in new[] { "360p", "480p", "720p", "1080p", "1440p", "2160p" })
        {
            QualityBox.Items.Add(new ComboBoxItem { Content = quality, Tag = quality });
        }

        var qualityIndex = QualityBox.Items.IndexOf(QualityBox.Items.OfType<ComboBoxItem>().FirstOrDefault(q => (string?)q.Tag == settings.DefaultQuality));
        QualityBox.SelectedIndex = qualityIndex >= 0 ? qualityIndex : 3;

        PosterSizeBox.Items.Clear();
        foreach (var (label, width) in new[] { ("S", 104), ("M", 132), ("L", 164), ("XL", 196) })
        {
            PosterSizeBox.Items.Add(new ComboBoxItem { Content = $"{label} · {width}px", Tag = width });
        }

        var posterIndex = PosterSizeBox.Items.IndexOf(PosterSizeBox.Items.OfType<ComboBoxItem>().FirstOrDefault(q => (int?)q.Tag == settings.PosterSize));
        PosterSizeBox.SelectedIndex = posterIndex >= 0 ? posterIndex : 1;

        HeadersToggle.IsOn = settings.UseAndroidHeaders;
        VersionText.Text = $"HDREZKA for Windows · {UpdateService.CurrentVersion} (20.09.2026)";

        DonateAddressText.Text = DonateAddress;
        try
        {
            DonateQrImage.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(
                new Uri(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "donate-qr.png")));
        }
        catch (Exception ex)
        {
            App.TryLog(ex);
        }

        _initialized = true;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
    }

    private void OnLanguageChanged()
    {
        DispatcherQueue.TryEnqueue(ApplyLocalization);
    }

    private void ApplyLocalization()
    {
        TitleText.Text = Loc.Get("Settings.Title");
        MirrorHeader.Text = Loc.Get("Settings.Mirror");
        MirrorHint.Text = Loc.Get("Settings.MirrorHint");
        LanguageHeader.Text = Loc.Get("Settings.Language");
        ThemeHeader.Text = Loc.Get("Settings.Theme");
        QualityHeader.Text = Loc.Get("Settings.Quality");
        PosterSizeHeader.Text = Loc.Get("Settings.PosterSize");
        HeadersHeader.Text = Loc.Get("Settings.Headers");
        HeadersHint.Text = Loc.Get("Settings.HeadersHint");
        DonateHeader.Text = Loc.Get("Settings.Donate");
        DonateHint.Text = Loc.Get("Settings.DonateHint");
        DonateCopyButton.Content = Loc.Get("Common.Copy");
        DonateCopiedText.Visibility = Visibility.Collapsed;
        UpdateHeader.Text = Loc.Get("Settings.Updates");
        CheckUpdatesButton.Content = Loc.Get("Settings.CheckUpdates");
        AboutHeader.Text = Loc.Get("Settings.About");
        DisclaimerText.Text = Loc.Get("Settings.Disclaimer");
        LogoutButton.Content = Loc.Get("Common.Logout");
        LogoutButton.Visibility = RezkaService.Instance.IsLoggedIn ? Visibility.Visible : Visibility.Collapsed;

        if (ThemeBox.Items.Count == 3)
        {
            ((ComboBoxItem)ThemeBox.Items[0]).Content = Loc.Get("Settings.ThemeSystem");
            ((ComboBoxItem)ThemeBox.Items[1]).Content = Loc.Get("Settings.ThemeLight");
            ((ComboBoxItem)ThemeBox.Items[2]).Content = Loc.Get("Settings.ThemeDark");
        }
    }

    private void MirrorBox_LostFocus(object sender, RoutedEventArgs e)
    {
        var mirror = MirrorBox.Text.Trim();
        if (!Uri.TryCreate(mirror, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            MirrorBox.Text = SettingsService.Instance.Mirror;
            return;
        }

        if (!mirror.EndsWith('/')) mirror += '/';
        if (mirror == SettingsService.Instance.Mirror) return;

        SettingsService.Instance.Mirror = mirror;
        SettingsService.Instance.Save();
        RezkaService.Instance.ApplySettings();
    }

    private void LanguageBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized) return;
        if (LanguageBox.SelectedItem is ComboBoxItem { Tag: AppLanguage lang })
        {
            SettingsService.Instance.Language = lang;
            SettingsService.Instance.Save();
            LocalizationService.Instance.Apply(lang);
        }
    }

    private void ThemeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized) return;
        if (ThemeBox.SelectedItem is ComboBoxItem { Tag: AppTheme theme })
        {
            SettingsService.Instance.Theme = theme;
            SettingsService.Instance.Save();
            ThemeHelper.Apply(theme);
        }
    }

    private void QualityBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized) return;
        if (QualityBox.SelectedItem is ComboBoxItem { Tag: string quality })
        {
            SettingsService.Instance.DefaultQuality = quality;
            SettingsService.Instance.Save();
        }
    }

    private void PosterSizeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized) return;
        if (PosterSizeBox.SelectedItem is ComboBoxItem { Tag: int width })
        {
            SettingsService.Instance.SetPosterSize(width);
        }
    }

    private void HeadersToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;
        SettingsService.Instance.UseAndroidHeaders = HeadersToggle.IsOn;
        SettingsService.Instance.Save();
        RezkaService.Instance.ApplySettings();
    }

    private void DonateCopyButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(DonateAddress);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            DonateCopiedText.Text = Loc.Get("Common.Copied");
            DonateCopiedText.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            App.TryLog(ex);
        }
    }

    private async void CheckUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdatesButton.IsEnabled = false;
        UpdateStatusText.Text = Loc.Get("Settings.Checking");
        try
        {
            var latest = await UpdateService.GetLatestAsync();
            if (latest == null)
            {
                UpdateStatusText.Text = Loc.Get("Error.Network");
            }
            else if (UpdateService.IsNewer(latest.Version, UpdateService.CurrentVersion))
            {
                UpdateStatusText.Text = Loc.Get("Settings.UpdateAvailable", latest.Version);
                if (App.MainWindow != null)
                {
                    await UpdateService.ShowUpdateDialogAsync(App.MainWindow, latest);
                }
            }
            else
            {
                UpdateStatusText.Text = Loc.Get("Settings.UpToDate");
            }

            SettingsService.Instance.LastUpdateCheckUtc = DateTime.UtcNow;
            SettingsService.Instance.Save();
        }
        finally
        {
            CheckUpdatesButton.IsEnabled = true;
        }
    }

    private async void LogoutButton_Click(object sender, RoutedEventArgs e)
    {
        await RezkaService.Instance.LogoutAsync();
        if (Frame?.CanGoBack == true)
        {
            Frame.GoBack();
        }
    }
}
