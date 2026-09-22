using HDREZKA.App.Controls;
using HDREZKA.App.Services;
using HDREZKA.Core.Api;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;

namespace HDREZKA.App.Views;

public sealed partial class HomePage : Page
{
    private const int SectionPreviewCount = 12;
    private const int HeroCount = 5;
    private const int HomeContinueCount = 3;

    private readonly Dictionary<string, bool> _sectionHasContent = new(StringComparer.OrdinalIgnoreCase);

    private bool _loaded;
    private readonly List<MovieSimple> _heroItems = new();
    private int _heroIndex;
    private readonly DispatcherTimer _heroTimer = new() { Interval = TimeSpan.FromSeconds(6) };

    public HomePage()
    {
        InitializeComponent();

        _heroTimer.Tick += (_, _) => ShowHero(_heroIndex + 1);

        FilmsAllButton.Tag = new CatalogLaunch(Section.Films, ListFilter.Latest);
        SeriesAllButton.Tag = new CatalogLaunch(Section.Series, ListFilter.Latest);
        CartoonsAllButton.Tag = new CatalogLaunch(Section.Cartoons, ListFilter.Latest);
        PopularAllButton.Tag = new CatalogLaunch(Section.Films, ListFilter.Popular);
        WatchingAllButton.Tag = new CatalogLaunch(Section.Films, ListFilter.WatchingNow);
        SoonAllButton.Tag = new CatalogLaunch(Section.Films, ListFilter.Soon);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ApplyLocalization();
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        if (!_loaded)
        {
            _loaded = true;
            Load();
        }
        else if (_heroItems.Count > 1)
        {
            _heroTimer.Start();
        }

        ApplyHeroMetrics();
        SettingsService.Instance.PosterSizeChanged += OnPosterSizeChanged;
        TrackedSeriesService.UpdatesChanged += OnTrackedUpdatesChanged;
        RenderContinue();
        RenderTracked();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _heroTimer.Stop();
        SettingsService.Instance.PosterSizeChanged -= OnPosterSizeChanged;
        TrackedSeriesService.UpdatesChanged -= OnTrackedUpdatesChanged;
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
    }

    private void OnTrackedUpdatesChanged()
    {
        DispatcherQueue.TryEnqueue(RenderTracked);
    }

    private void OnPosterSizeChanged() => DispatcherQueue.TryEnqueue(ApplyHeroMetrics);

    private void ApplyHeroMetrics()
    {
        HeroCard.Height = PosterMetrics.HeroCardHeight;
        HeroPosterBorder.Width = PosterMetrics.HeroWidth;
        HeroPosterBorder.Height = PosterMetrics.HeroHeight;
        HeroPosterClip.Rect = new Windows.Foundation.Rect(0, 0, PosterMetrics.HeroWidth, PosterMetrics.HeroHeight);
    }

    private void OnLanguageChanged()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            ApplyLocalization();
            RenderContinue();
            RenderTracked();
        });
    }

    private void ApplyLocalization()
    {
        LoadingText.Text = Loc.Get("Common.Loading");
        RetryButton.Content = Loc.Get("Common.Retry");
        MirrorButton.Content = Loc.Get("Settings.AutoMirror");
        HeroWatchButton.Content = Loc.Get("Common.Watch");
        ContinueHeader.Text = Loc.Get("Continue.Home");
        TrackedHeader.Text = Loc.Get("Home.Tracked");
        FilmsHeader.Text = Loc.Get("Section.Films") + " — " + Loc.Get("Filter.Latest");
        SeriesHeader.Text = Loc.Get("Section.Series") + " — " + Loc.Get("Filter.Latest");
        CartoonsHeader.Text = Loc.Get("Section.Cartoons") + " — " + Loc.Get("Filter.Latest");
        PopularHeader.Text = Loc.Get("Filter.Popular");
        WatchingHeader.Text = Loc.Get("Filter.WatchingNow");
        SoonHeader.Text = Loc.Get("Filter.Soon");

        var showAll = Loc.Get("Common.ShowAll");
        FilmsAllText.Text = showAll;
        SeriesAllText.Text = showAll;
        CartoonsAllText.Text = showAll;
        PopularAllText.Text = showAll;
        WatchingAllText.Text = showAll;
        SoonAllText.Text = showAll;
        ContinueAllText.Text = showAll;
        TrackedAllText.Text = showAll;
    }

    private async void Load()
    {
        LoadingPanel.Visibility = Visibility.Visible;
        ErrorPanel.Visibility = Visibility.Collapsed;

        var client = RezkaService.Instance.Client;
        try
        {
            var hot = WrapList(client.GetHotMoviesAsync());
            var films = WrapTitle(client.GetSectionListAsync(Section.Films, ListFilter.Latest));
            var series = WrapTitle(client.GetSectionListAsync(Section.Series, ListFilter.Latest));
            var cartoons = WrapTitle(client.GetSectionListAsync(Section.Cartoons, ListFilter.Latest));
            var popular = WrapTitle(client.GetHomeListAsync(ListFilter.Popular));
            var watching = WrapTitle(client.GetHomeListAsync(ListFilter.WatchingNow));
            var soon = WrapTitle(client.GetHomeListAsync(ListFilter.Soon));

            await Task.WhenAll(hot, films, series, cartoons, popular, watching, soon);

            BindHero(hot.Result.Take(HeroCount).ToList());
            BindSection("Films", FilmsSection, FilmsGrid, films.Result.Items);
            BindSection("Series", SeriesSection, SeriesGrid, series.Result.Items);
            BindSection("Cartoons", CartoonsSection, CartoonsGrid, cartoons.Result.Items);
            BindSection("Popular", PopularSection, PopularGrid, popular.Result.Items);
            BindSection("Watching", WatchingSection, WatchingGrid, watching.Result.Items);
            BindSection("Soon", SoonSection, SoonGrid, soon.Result.Items);
            ApplySectionLayout();

            LoadingPanel.Visibility = Visibility.Collapsed;
        }
        catch (RezkaException ex)
        {
            ShowError(RezkaService.Instance.ErrorText(ex),
                ex.Kind is RezkaError.MirrorBanned or RezkaError.AccessDenied);
        }
    }

    private void BindHero(IReadOnlyList<MovieSimple> movies)
    {
        _heroTimer.Stop();
        _heroItems.Clear();
        _heroItems.AddRange(movies.Where(m => m != null));
        _heroIndex = 0;
        _sectionHasContent["Hero"] = _heroItems.Count > 0;

        HeroDots.Children.Clear();
        for (var i = 0; i < _heroItems.Count; i++)
        {
            var dot = new Microsoft.UI.Xaml.Shapes.Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray),
                Tag = i,
            };
            dot.Tapped += HeroDot_Tapped;
            HeroDots.Children.Add(dot);
        }

        var single = _heroItems.Count < 2;
        HeroPrevButton.Visibility = single ? Visibility.Collapsed : Visibility.Visible;
        HeroNextButton.Visibility = single ? Visibility.Collapsed : Visibility.Visible;
        HeroDots.Visibility = single ? Visibility.Collapsed : Visibility.Visible;

        if (_heroItems.Count == 0)
        {
            HeroCard.Visibility = Visibility.Collapsed;
            return;
        }

        HeroCard.Visibility = Visibility.Visible;
        ShowHero(0, animate: false);
        if (_heroItems.Count > 1)
        {
            _heroTimer.Start();
        }
    }

    private void ShowHero(int index, bool animate = true)
    {
        if (_heroItems.Count == 0) return;
        _heroIndex = ((index % _heroItems.Count) + _heroItems.Count) % _heroItems.Count;

        if (animate)
        {
            FadeHeroCard(apply: ApplyHeroContent);
        }
        else
        {
            ApplyHeroContent();
        }
    }

    private void ApplyHeroContent()
    {
        var movie = _heroItems[_heroIndex];
        HeroKicker.Text = $"{Loc.Get("Filter.Hot")} · #{_heroIndex + 1}";
        HeroTitle.Text = movie.Name ?? "";
        HeroDetails.Text = movie.Details ?? "";

        if (!string.IsNullOrEmpty(movie.Poster) && Uri.TryCreate(movie.Poster, UriKind.Absolute, out var uri))
        {
            var image = new BitmapImage(uri);
            HeroPoster.Source = image;
            HeroBackdrop.Source = image;
        }
        else
        {
            HeroPoster.Source = null;
            HeroBackdrop.Source = null;
        }

        if (movie.Rating is { } rating)
        {
            HeroRatingBadge.Visibility = Visibility.Visible;
            HeroRatingText.Text = rating.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        }
        else
        {
            HeroRatingBadge.Visibility = Visibility.Collapsed;
        }

        for (var i = 0; i < HeroDots.Children.Count; i++)
        {
            if (HeroDots.Children[i] is Microsoft.UI.Xaml.Shapes.Ellipse dot)
            {
                dot.Fill = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    i == _heroIndex ? Microsoft.UI.Colors.White : Windows.UI.Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF));
                dot.Width = i == _heroIndex ? 20 : 8;
            }
        }
    }

    private void FadeHeroCard(Action apply)
    {
        var fadeOut = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        var toZero = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            To = 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(150)),
            EnableDependentAnimation = true,
        };
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(toZero, HeroCard);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(toZero, "Opacity");
        fadeOut.Children.Add(toZero);
        fadeOut.Completed += (_, _) =>
        {
            apply();
            var fadeIn = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
            var toOne = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
            {
                To = 1,
                Duration = new Duration(TimeSpan.FromMilliseconds(250)),
                EnableDependentAnimation = true,
            };
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(toOne, HeroCard);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(toOne, "Opacity");
            fadeIn.Children.Add(toOne);
            fadeIn.Begin();
        };
        fadeOut.Begin();
    }

    private void RestartHeroTimer()
    {
        if (_heroItems.Count <= 1) return;
        _heroTimer.Stop();
        _heroTimer.Start();
    }

    private void BindSection(string id, StackPanel section, GridView grid, IReadOnlyList<MovieSimple> items)
    {
        var preview = items.Take(SectionPreviewCount).ToList();
        grid.ItemsSource = preview;
        var show = preview.Count > 0;
        _sectionHasContent[id] = show;
        section.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Orders + hides home blocks per Settings → Home screen.
    /// Content presence comes from _sectionHasContent (bind methods).
    /// </summary>
    private void ApplySectionLayout()
    {
        var map = new Dictionary<string, UIElement>(StringComparer.OrdinalIgnoreCase)
        {
            ["Hero"] = HeroCard,
            ["Continue"] = ContinueSection,
            ["Tracked"] = TrackedSection,
            ["Films"] = FilmsSection,
            ["Series"] = SeriesSection,
            ["Cartoons"] = CartoonsSection,
            ["Popular"] = PopularSection,
            ["Watching"] = WatchingSection,
            ["Soon"] = SoonSection,
        };

        var pos = 0;
        foreach (var s in SettingsService.Instance.HomeSections)
        {
            if (!map.TryGetValue(s.Id, out var el)) continue;
            el.Visibility = s.Visible && _sectionHasContent.GetValueOrDefault(s.Id)
                ? Visibility.Visible
                : Visibility.Collapsed;
            var idx = ContentPanel.Children.IndexOf(el);
            if (idx >= 0 && idx != pos)
            {
                ContentPanel.Children.Move((uint)idx, (uint)pos);
            }

            pos++;
        }
    }

    /// <summary>
    /// Local "continue watching" row: top-N positions from the app itself,
    /// no server requests. Refreshed on every navigation to Home.
    /// </summary>
    private void RenderContinue()
    {
        ContinueRow.Children.Clear();

        var items = PositionService.Instance.GetAll()
            .Where(p => !ContinueWatchingPage.IsLocalTestEntry(p.MovieId, p.Title))
            .Take(HomeContinueCount)
            .Select(ContinueWatchingPage.FromLocal)
            .ToList();

        foreach (var item in items)
        {
            var card = new ContinueCard
            {
                Data = item,
                Margin = new Thickness(0, 0, 18, 26),
                IsHitTestVisible = true,
                PlayDirectly = SettingsService.Instance.PlayFromHomeDirectly,
            };
            card.DeleteRequested += OnHomeContinueDeleteRequested;
            card.WatchedToggled += OnHomeContinueWatchedToggled;
            card.PlayRequested += OnHomeContinuePlayRequested;
            ContinueRow.Children.Add(card);
        }

        _sectionHasContent["Continue"] = items.Count > 0;
        ApplySectionLayout();
    }

    private void OnHomeContinueDeleteRequested(object? sender, ContinueItem item)
    {
        PositionService.Instance.Remove(item.Id);
        RenderContinue();
    }

    private async void OnHomeContinueWatchedToggled(object? sender, ContinueItem item)
    {
        // Home row is local-only: just flip the offline key and re-read.
        if (await ContinueWatchingPage.ToggleWatchedAsync(item, Content.XamlRoot) != null)
        {
            RenderContinue();
        }
    }

    private void GoToDetails(ContinueItem item)
    {
        Nav.Go<DetailsPage>(new MovieSimple(Id: item.Id, Name: item.Title, Poster: item.Poster));
    }

    /// <summary>
    /// Direct resume from Home: resolves the saved voice/season/episode
    /// and opens the player without the details page. Falls back to
    /// details on any failure.
    /// </summary>
    private async void OnHomeContinuePlayRequested(object? sender, ContinueItem item)
    {
        if (sender is ContinueCard card) card.IsEnabled = false;
        try
        {
            if (item.MovieKey == null || item.TranslatorKey == null)
            {
                GoToDetails(item);
                return;
            }

            var client = RezkaService.Instance.Client;
            var details = await client.GetDetailsAsync(item.Id);
            var voice = details.VoiceActings?.FirstOrDefault(v => v.TranslatorId == item.TranslatorKey)
                ?? details.VoiceActings?.FirstOrDefault(v => v.IsSelected)
                ?? details.VoiceActings?.FirstOrDefault();
            if (voice == null || !details.IsAvailable || details.IsComingSoon)
            {
                GoToDetails(item);
                return;
            }

            IReadOnlyList<MovieSeason>? seasons = details.Seasons;
            if (voice.Url == null && seasons == null)
            {
                var numeric = ContinueWatchingPage.ExtractNumericId(details.Id);
                if (numeric == null)
                {
                    GoToDetails(item);
                    return;
                }

                seasons = await client.GetSeriesSeasonsAsync(numeric, voice, details.Favs);
            }

            var season = seasons?.FirstOrDefault(s => s.SeasonId == item.SeasonKey)
                ?? seasons?.FirstOrDefault(s => s.IsSelected)
                ?? seasons?.FirstOrDefault();
            var episode = season?.Episodes.FirstOrDefault(e => e.EpisodeId == item.EpisodeKey)
                ?? season?.Episodes.FirstOrDefault(e => e.IsSelected)
                ?? season?.Episodes.FirstOrDefault();

            var playerWindow = new PlayerWindow();
            playerWindow.ShowPlayer(new PlayerLaunch(details, voice, seasons, season, episode));
        }
        catch (Exception ex)
        {
            App.TryLog(ex);
            GoToDetails(item);
        }
        finally
        {
            if (sender is ContinueCard card2) card2.IsEnabled = true;
        }
    }

    private void ContinueAll_Click(object sender, RoutedEventArgs e)
    {
        Nav.Go<ContinueWatchingPage>();
    }

    /// <summary>
    /// Tracked series with released episodes (from Updates store).
    /// Delete consumes the entry; eye toggle is a no-op here (no watch key).
    /// </summary>
    private void RenderTracked()
    {
        TrackedRow.Children.Clear();

        var items = TrackedSeriesService.GetUpdates()
            .Take(HomeContinueCount)
            .Select(u => new ContinueItem(
                Id: u.PagePath,
                Title: u.Title,
                Poster: u.Poster,
                Subtitle: $"{u.Text} · {u.Date:dd.MM.yyyy}",
                PositionSeconds: 0,
                DurationSeconds: 0,
                UpdatedAt: u.Date))
            .ToList();

        foreach (var item in items)
        {
            var card = new ContinueCard
            {
                Data = item,
                Margin = new Thickness(0, 0, 18, 26),
                IsHitTestVisible = true,
            };
            card.DeleteRequested += OnTrackedDeleteRequested;
            TrackedRow.Children.Add(card);
        }

        _sectionHasContent["Tracked"] = items.Count > 0;
        ApplySectionLayout();
    }

    private void OnTrackedDeleteRequested(object? sender, ContinueItem item)
    {
        var stored = TrackedSeriesService.GetUpdates()
            .FirstOrDefault(u => u.PagePath == item.Id && u.Title == item.Title && u.Date == item.UpdatedAt);
        if (stored != null)
        {
            TrackedSeriesService.RemoveUpdate(stored);
        }

        RenderTracked();
    }

    private void TrackedAll_Click(object sender, RoutedEventArgs e)
    {
        Nav.Go<UpdatesPage>();
    }

    private void HeroWatchButton_Click(object sender, RoutedEventArgs e)
    {
        if (_heroItems.Count > 0)
        {
            Nav.Go<DetailsPage>(_heroItems[_heroIndex]);
        }
    }

    private void HeroPrevButton_Click(object sender, RoutedEventArgs e)
    {
        ShowHero(_heroIndex - 1);
        RestartHeroTimer();
    }

    private void HeroNextButton_Click(object sender, RoutedEventArgs e)
    {
        ShowHero(_heroIndex + 1);
        RestartHeroTimer();
    }

    private void HeroDot_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is int index && index != _heroIndex)
        {
            ShowHero(index);
            RestartHeroTimer();
        }
    }

    private void HeroCard_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        HeroClipRect.Rect = new Windows.Foundation.Rect(0, 0, Math.Max(0, e.NewSize.Width), Math.Max(0, e.NewSize.Height));
    }

    private void ShowAll_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is CatalogLaunch launch)
        {
            Nav.Go<CatalogPage>(launch);
        }
    }

    private static async Task<IReadOnlyList<MovieSimple>> WrapList(Task<IReadOnlyList<MovieSimple>> task)
    {
        try
        {
            return await task;
        }
        catch (RezkaException ex) when (ex.Kind is RezkaError.Network or RezkaError.MirrorBanned)
        {
            throw;
        }
        catch
        {
            return [];
        }
    }

    private static async Task<(string Title, IReadOnlyList<MovieSimple> Items)> WrapTitle(
        Task<(string Title, IReadOnlyList<MovieSimple> Items)> task)
    {
        try
        {
            return await task;
        }
        catch (RezkaException ex) when (ex.Kind is RezkaError.Network or RezkaError.MirrorBanned)
        {
            throw;
        }
        catch
        {
            return ("", []);
        }
    }

    private void ShowError(string message, bool showMirrorButton = false)
    {
        LoadingPanel.Visibility = Visibility.Collapsed;
        ErrorPanel.Visibility = Visibility.Visible;
        ErrorText.Text = message;
        MirrorButton.Visibility = showMirrorButton ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RetryButton_Click(object sender, RoutedEventArgs e) => Load();

    private async void MirrorButton_Click(object sender, RoutedEventArgs e)
    {
        MirrorButton.IsEnabled = false;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var applied = await MirrorService.PickAndApplyAsync(Content.XamlRoot, DispatcherQueue, cts.Token);
            if (applied != null) Load();
        }
        catch
        {
        }
        finally
        {
            MirrorButton.IsEnabled = true;
        }
    }
}
