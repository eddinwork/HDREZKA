using HDREZKA.App.Services;
using HDREZKA.Core.Api;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.DataTransfer;

namespace HDREZKA.App.Views;

public sealed record PlayerLaunch(
    MovieDetailed Details,
    MovieVoiceActing Voice,
    IReadOnlyList<MovieSeason>? Seasons,
    MovieSeason? Season,
    MovieEpisode? Episode);

public sealed partial class DetailsPage : Page
{
    private MovieSimple? _movie;
    private MovieDetailed? _details;
    private IReadOnlyList<MovieSeason>? _seasons;
    private MovieVoiceActing? _voice;
    private MovieSeason? _season;
    private MovieEpisode? _episode;
    private int _commentsPage = 1;
    private bool _commentsLoaded;
    private string? _replyTo;

    public DetailsPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ApplyLocalization();
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        SettingsService.Instance.PosterSizeChanged += OnPosterSizeChanged;
        ApplyPosterMetrics();

        if (e.Parameter is MovieSimple movie && _movie?.Id != movie.Id)
        {
            _movie = movie;
            _ = LoadAsync(movie);
        }
        else if (e.Parameter is MovieDetailed details && _details?.Id != details.Id)
        {
            _movie = new MovieSimple(Id: details.Id, Name: details.Name, Poster: details.Poster);
            _ = LoadDetailsAsync(details);
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        SettingsService.Instance.PosterSizeChanged -= OnPosterSizeChanged;
    }

    private void OnPosterSizeChanged() => DispatcherQueue.TryEnqueue(ApplyPosterMetrics);

    private void ApplyPosterMetrics()
    {
        PosterImage.Width = PosterMetrics.DetailsWidth;
        PosterImage.Height = PosterMetrics.DetailsHeight;
    }

    private void OnLanguageChanged() => DispatcherQueue.TryEnqueue(ApplyLocalization);

    private void ApplyLocalization()
    {
        WatchText.Text = Loc.Get("Common.Watch");
        BookmarkText.Text = Loc.Get("Common.AddToBookmarks");
        WatchedText.Text = Loc.Get("Continue.ToggleWatched");
        VoicesHeader.Text = Loc.Get("Common.VoiceActing");
        SeasonsHeader.Text = Loc.Get("Common.Season");
        RetryButton.Content = Loc.Get("Common.Retry");
        CommentInput.PlaceholderText = Loc.Get("Comments.Placeholder");
        CommentSendButton.Content = Loc.Get("Common.Send");
        CommentsMoreButton.Content = Loc.Get("Common.LoadMore");
    }

    private async Task LoadAsync(MovieSimple movie)
    {
        MainPanel.Visibility = Visibility.Collapsed;
        LoadingPanel.Visibility = Visibility.Visible;
        ErrorPanel.Visibility = Visibility.Collapsed;

        try
        {
            var details = await RezkaService.Instance.Client.GetDetailsAsync(movie.Id);
            await LoadDetailsAsync(details);
        }
        catch (RezkaException ex)
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            ErrorPanel.Visibility = Visibility.Visible;
            ErrorText.Text = RezkaService.Instance.ErrorText(ex);
        }
    }

    private Task LoadDetailsAsync(MovieDetailed details)
    {
        _details = details;
        DispatcherQueue.TryEnqueue(() =>
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            ErrorPanel.Visibility = Visibility.Collapsed;
            MainPanel.Visibility = Visibility.Visible;
            Render(details);
        });
        return Task.CompletedTask;
    }

    private void Render(MovieDetailed details)
    {
        TitleText.Text = details.Name;
        OrigTitleText.Text = details.OriginalName ?? "";
        if (!string.IsNullOrEmpty(details.Poster))
        {
            PosterImage.Source = new BitmapImage(new Uri(details.Poster));
        }

        DescriptionText.Text = details.Description ?? "";
        RatingsPanel.Children.Clear();
        AddRating("Rezka", details.SiteRating);
        AddRating("IMDb", details.ImdbRating);
        AddRating("KP", details.KpRating);
        BuildRateRow();

        MetaPanel.Children.Clear();
        if (details.Year != null) AddMeta(Loc.Get("Details.Year"), details.Year);
        if (details.Countries is { Count: > 0 }) AddMeta(Loc.Get("Details.Country"), string.Join(", ", details.Countries.Select(c => c.Name)));
        if (details.Genres is { Count: > 0 }) AddMeta(Loc.Get("Details.Genre"), string.Join(", ", details.Genres.Select(g => g.Name)));
        if (details.Duration is { } mins) AddMeta(Loc.Get("Details.Duration"), $"{mins} {Loc.Get("Common.Minutes")}");
        if (details.Producers is { Count: > 0 }) AddMeta(Loc.Get("Details.Director"), string.Join(", ", details.Producers.Select(p => p.Name)));
        if (details.Actors is { Count: > 0 }) AddMeta(Loc.Get("Details.Actors"), string.Join(", ", details.Actors.Select(a => a.Name)));
        if (details.Slogan is { Length: > 0 }) AddMeta(Loc.Get("Details.Slogan"), details.Slogan);
        if (details.AgeRestriction is { } age) AddMeta(Loc.Get("Details.Age"), age);

        // voices
        VoicesPanel.Visibility = details.VoiceActings is { Count: > 0 } ? Visibility.Visible : Visibility.Collapsed;
        VoicesList.Children.Clear();
        if (details.VoiceActings != null)
        {
            foreach (var voice in details.VoiceActings)
            {
                var button = new ToggleButton
                {
                    Content = voice.Name + (voice.IsPremium ? " ★" : ""),
                    Tag = voice,
                    IsChecked = voice.IsSelected,
                    MaxWidth = 340,
                };
                button.Click += VoiceButton_Click;
                VoicesList.Children.Add(button);
            }
        }

        _seasons = details.Seasons;
        _voice = details.VoiceActings?.FirstOrDefault(v => v.IsSelected) ?? details.VoiceActings?.FirstOrDefault();
        _season = _seasons?.FirstOrDefault(s => s.IsSelected) ?? _seasons?.FirstOrDefault();
        _episode = _season?.Episodes.FirstOrDefault(ep => ep.IsSelected) ?? _season?.Episodes.FirstOrDefault();

        RenderSeasons();

        WatchButton.IsEnabled = details.IsAvailable && !details.IsComingSoon && _voice != null;
        if (details.IsComingSoon)
        {
            WatchText.Text = Loc.Get("Details.ComingSoon");
        }
        else if (!details.IsAvailable)
        {
            WatchText.Text = Loc.Get("Details.Unavailable");
        }
        else if (WatchText.Text == Loc.Get("Details.ComingSoon") || WatchText.Text == Loc.Get("Details.Unavailable"))
        {
            WatchText.Text = Loc.Get("Common.Watch");
        }

        CommentsSection.Visibility = details.CommentsCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        CommentsToggleText.Text = $"{Loc.Get("Comments.Show")} ({details.CommentsCount})";

        UpdateWatchedButton();
    }

    private void AddRating(string label, MovieRating? rating)
    {
        if (rating?.Value == null) return;
        var border = new Border
        {
            Background = Application.Current.Resources["CardBackgroundFillColorDefaultBrush"] as Microsoft.UI.Xaml.Media.Brush,
            BorderBrush = Application.Current.Resources["CardStrokeColorDefaultBrush"] as Microsoft.UI.Xaml.Media.Brush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 6, 12, 6),
        };
        var stack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        stack.Children.Add(new TextBlock
        {
            Text = label,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Microsoft.UI.Xaml.Media.Brush,
        });
        stack.Children.Add(new TextBlock
        {
            Text = rating.Value.Value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture),
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
        });
        if (rating.Votes != null)
        {
            stack.Children.Add(new TextBlock
            {
                Text = $"({rating.Votes} {Loc.Get("Common.Votes")})",
                FontSize = 11,
                Foreground = Application.Current.Resources["TextFillColorTertiaryBrush"] as Microsoft.UI.Xaml.Media.Brush,
            });
        }

        border.Child = stack;
        RatingsPanel.Children.Add(border);
    }

    private readonly List<Button> _rateButtons = new();
    private TextBlock? _rateThanksText;

    private void BuildRateRow()
    {
        _rateButtons.Clear();
        _rateThanksText = null;
        UserRatingPanel.Children.Clear();
        UserRatingPanel.Visibility = Visibility.Visible;

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(new TextBlock
        {
            Text = Loc.Get("Details.YourRating") + ":",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
            Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Microsoft.UI.Xaml.Media.Brush,
        });

        for (var i = 1; i <= 10; i++)
        {
            var star = new Button
            {
                Content = new FontIcon { Glyph = "\uE734", FontSize = 15 },
                Tag = i,
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(3, 2, 3, 2),
                MinWidth = 0,
                MinHeight = 0,
            };
            star.Click += RateButton_Click;
            ToolTipService.SetToolTip(star, i.ToString());
            _rateButtons.Add(star);
            row.Children.Add(star);
        }

        PaintRateRow(null);

        var wrap = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        wrap.Children.Add(row);
        _rateThanksText = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 12,
            Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Microsoft.UI.Xaml.Media.Brush,
            Visibility = Visibility.Collapsed,
        };
        wrap.Children.Add(_rateThanksText);
        UserRatingPanel.Children.Add(wrap);
    }

    private void PaintRateRow(int? selected)
    {
        var accent = Application.Current.Resources["SystemFillColorAttentionBrush"] as Microsoft.UI.Xaml.Media.Brush
            ?? new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gold);
        var idle = Application.Current.Resources["TextFillColorTertiaryBrush"] as Microsoft.UI.Xaml.Media.Brush;
        for (var i = 0; i < _rateButtons.Count; i++)
        {
            var button = _rateButtons[i];
            button.IsEnabled = selected == null;
            if (button.Content is FontIcon icon)
            {
                icon.Glyph = selected != null && i < selected ? "\uE735" : "\uE734";
                icon.Foreground = selected != null && i < selected ? accent : idle;
            }
        }
    }

    private async void RateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_details == null) return;
        if ((sender as Button)?.Tag is not int value) return;

        if (!RezkaService.Instance.IsLoggedIn)
        {
            await ShowLoginAsync();
            return;
        }

        var numericId = ExtractNumericId(_details.Id);
        if (numericId == null) return;

        foreach (var b in _rateButtons) b.IsEnabled = false;
        try
        {
            await RezkaService.Instance.Client.RateAsync(numericId, value);
            PaintRateRow(value);
            if (_rateThanksText != null)
            {
                _rateThanksText.Text = Loc.Get("Details.RateThanks");
                _rateThanksText.Visibility = Visibility.Visible;
            }
        }
        catch (RezkaException ex)
        {
            await ShowErrorAsync(RezkaService.Instance.ErrorText(ex));
            PaintRateRow(null);
        }
    }

    private (string MovieId, string TranslatorId, string? SeasonId, string? EpisodeId)? WatchedKey()
    {
        if (_details == null || _voice == null) return null;
        var movieId = ExtractNumericId(_details.Id) ?? _details.Id;
        return (movieId, _voice.TranslatorId, _season?.SeasonId, _episode?.EpisodeId);
    }

    private void UpdateWatchedButton()
    {
        var key = WatchedKey();
        WatchedButton.IsEnabled = key != null;
        WatchedButton.IsChecked = key != null &&
            PositionService.Instance.IsWatched(key.Value.MovieId, key.Value.TranslatorId, key.Value.SeasonId, key.Value.EpisodeId);
    }

    private void WatchedButton_Click(object sender, RoutedEventArgs e)
    {
        var key = WatchedKey();
        if (key == null) return;
        var now = PositionService.Instance.ToggleWatched(key.Value.MovieId, key.Value.TranslatorId, key.Value.SeasonId, key.Value.EpisodeId);
        WatchedButton.IsChecked = now;
    }

    private void AddMeta(string label, string value)
    {
        var grid = new Grid { ColumnDefinitions = { new ColumnDefinition { Width = new GridLength(160) }, new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) } } };
        grid.Children.Add(new TextBlock
        {
            Text = label,
            Style = Application.Current.Resources["DetailsLabel"] as Style,
        });
        var val = new TextBlock { Text = value, Style = Application.Current.Resources["DetailsValue"] as Style };
        Grid.SetColumn(val, 1);
        grid.Children.Add(val);
        MetaPanel.Children.Add(grid);
    }

    private async void VoiceButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton button || button.Tag is not MovieVoiceActing voice || _details == null) return;

        foreach (var child in VoicesList.Children.OfType<ToggleButton>())
        {
            child.IsChecked = child == button;
        }

        _voice = voice;
        _season = null;
        _episode = null;

        // reload seasons for this voice if needed
        try
        {
            var numericId = ExtractNumericId(_details.Id);
            if (numericId != null && voice.Url == null && _details.Seasons == null)
            {
                _seasons = await RezkaService.Instance.Client.GetSeriesSeasonsAsync(numericId, voice, _details.Favs);
            }
            else
            {
                _seasons = _details.Seasons;
            }

            _season = _seasons?.FirstOrDefault();
            _episode = _season?.Episodes.FirstOrDefault();
            RenderSeasons();
        }
        catch (RezkaException)
        {
            _seasons = _details.Seasons;
            RenderSeasons();
        }

        UpdateWatchedButton();
    }

    private void RenderSeasons()
    {
        var hasSeasons = _seasons is { Count: > 0 };
        SeasonsPanel.Visibility = hasSeasons ? Visibility.Visible : Visibility.Collapsed;
        if (!hasSeasons) return;

        SeasonBox.SelectionChanged -= SeasonBox_SelectionChanged;
        SeasonBox.Items.Clear();
        foreach (var season in _seasons!)
        {
            SeasonBox.Items.Add(new ComboBoxItem
            {
                Content = $"{Loc.Get("Common.Season")} {season.Name}",
                Tag = season,
            });
        }

        var selectedIdx = 0;
        var i = 0;
        foreach (var season in _seasons)
        {
            if (season.SeasonId == _season?.SeasonId) selectedIdx = i;
            i++;
        }

        SeasonBox.SelectedIndex = selectedIdx;
        SeasonBox.SelectionChanged += SeasonBox_SelectionChanged;
        RenderEpisodes();
    }

    private void SeasonBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SeasonBox.SelectedItem is ComboBoxItem { Tag: MovieSeason season })
        {
            _season = season;
            _episode = season.Episodes.FirstOrDefault();
            RenderEpisodes();
            UpdateWatchedButton();
        }
    }

    private void RenderEpisodes()
    {
        EpisodesList.Items.Clear();
        if (_season == null) return;
        foreach (var episode in _season.Episodes)
        {
            var button = new ToggleButton
            {
                Content = episode.Name,
                Tag = episode,
                MinWidth = 44,
                IsChecked = episode.EpisodeId == _episode?.EpisodeId,
            };
            button.Click += EpisodeButton_Click;
            EpisodesList.Items.Add(button);
        }
    }

    private void EpisodeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton button || button.Tag is not MovieEpisode episode) return;
        _episode = episode;
        foreach (var item in EpisodesList.Items.OfType<ToggleButton>())
        {
            item.IsChecked = item == button;
        }

        UpdateWatchedButton();
    }

    private void WatchButton_Click(object sender, RoutedEventArgs e)
    {
        if (_details == null || _voice == null) return;

        try
        {
            // Player lives in a separate window: the main window layout
            // (custom title bar, drag rectangles) stays untouched.
            var playerWindow = new PlayerWindow();
            playerWindow.ShowPlayer(new PlayerLaunch(_details, _voice, _seasons, _season, _episode));
        }
        catch (Exception ex)
        {
            App.TryLog(ex);
        }
    }

    private async void BookmarkButton_Click(object sender, RoutedEventArgs e)
    {
        if (_details == null) return;

        if (!RezkaService.Instance.IsLoggedIn)
        {
            await ShowLoginAsync();
            return;
        }

        var numericId = ExtractNumericId(_details.Id);
        if (numericId == null) return;

        try
        {
            var categories = await RezkaService.Instance.Client.GetBookmarkCategoriesAsync();
            var menu = new MenuFlyout();
            foreach (var category in categories)
            {
                var item = new MenuFlyoutItem
                {
                    Text = $"{category.Name} ({category.Count})",
                    Tag = category.Id,
                };
                item.Click += async (_, _) =>
                {
                    try
                    {
                        await RezkaService.Instance.Client.AddToBookmarksAsync(numericId, category.Id);
                        BookmarkText.Text = Loc.Get("Common.RemoveFromBookmarks");
                    }
                    catch (RezkaException ex)
                    {
                        await ShowErrorAsync(RezkaService.Instance.ErrorText(ex));
                    }
                };
                menu.Items.Add(item);
            }

            menu.ShowAt(BookmarkButton, new Windows.Foundation.Point());
        }
        catch (RezkaException ex)
        {
            await ShowErrorAsync(RezkaService.Instance.ErrorText(ex));
        }
    }

    private async Task ShowLoginAsync()
    {
        var dialog = new LoginDialog { XamlRoot = Content.XamlRoot };
        await dialog.ShowAsync();
    }

    private async Task ShowErrorAsync(string message)
    {
        var dialog = new ContentDialog
        {
            Title = Loc.Get("Common.Error"),
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = Content.XamlRoot,
        };
        await dialog.ShowAsync();
    }

    // ---------- comments ----------

    private async void CommentsToggle_Click(object sender, RoutedEventArgs e)
    {
        if (CommentsToggle.IsChecked == true)
        {
            CommentsPanel.Visibility = Visibility.Visible;
            CommentsToggleText.Text = $"{Loc.Get("Comments.Hide")} ({_details?.CommentsCount})";
            if (!_commentsLoaded)
            {
                _commentsLoaded = true;
                await LoadCommentsAsync();
            }
        }
        else
        {
            CommentsPanel.Visibility = Visibility.Collapsed;
            CommentsToggleText.Text = $"{Loc.Get("Comments.Show")} ({_details?.CommentsCount})";
        }
    }

    private async Task LoadCommentsAsync()
    {
        if (_details == null) return;
        var numericId = ExtractNumericId(_details.Id);
        if (numericId == null) return;

        try
        {
            var comments = await RezkaService.Instance.Client.GetCommentsAsync(numericId, _commentsPage);
            foreach (var comment in comments)
            {
                CommentsList.Children.Add(BuildComment(comment, 0));
            }

            CommentsMoreButton.Visibility = comments.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (RezkaException ex)
        {
            await ShowErrorAsync(RezkaService.Instance.ErrorText(ex));
        }
    }

    private UIElement BuildComment(CommentItem comment, int depth)
    {
        var border = new Border
        {
            Background = Application.Current.Resources["CardBackgroundFillColorDefaultBrush"] as Microsoft.UI.Xaml.Media.Brush,
            BorderBrush = Application.Current.Resources["CardStrokeColorDefaultBrush"] as Microsoft.UI.Xaml.Media.Brush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(depth * 28, 4, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            MaxWidth = 900,
        };

        var stack = new StackPanel { Spacing = 4 };

        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        header.Children.Add(new TextBlock
        {
            Text = comment.Author,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        });
        if (comment.IsAdmin)
        {
            header.Children.Add(new Border
            {
                Background = Application.Current.Resources["AccentFillColorDefaultBrush"] as Microsoft.UI.Xaml.Media.Brush,
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 1, 6, 1),
                Child = new TextBlock
                {
                    Text = "★",
                    FontSize = 11,
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White),
                },
            });
        }

        header.Children.Add(new TextBlock
        {
            Text = comment.Date,
            FontSize = 11,
            Foreground = Application.Current.Resources["TextFillColorTertiaryBrush"] as Microsoft.UI.Xaml.Media.Brush,
        });
        stack.Children.Add(header);

        stack.Children.Add(new TextBlock
        {
            Text = comment.Text,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
        });

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var likeButton = new Button
        {
            Content = $"👍 {comment.Likes}",
            Tag = comment.Id,
            FontSize = 12,
            Padding = new Thickness(8, 3, 8, 3),
        };
        likeButton.Click += async (_, _) =>
        {
            try
            {
                await RezkaService.Instance.Client.ToggleCommentLikeAsync(comment.Id);
                likeButton.Content = $"👍 {comment.Likes + 1}";
                likeButton.IsEnabled = false;
            }
            catch (RezkaException)
            {
                // ignore like failures silently
            }
        };
        actions.Children.Add(likeButton);

        var replyButton = new HyperlinkButton
        {
            Content = Loc.Get("Comments.Reply"),
            FontSize = 12,
            Padding = new Thickness(0),
            Tag = comment.Id,
        };
        replyButton.Click += (_, _) =>
        {
            _replyTo = comment.Id;
            CommentInput.PlaceholderText = $"{Loc.Get("Comments.Reply")}: {comment.Author}";
            CommentInput.Focus(FocusState.Programmatic);
        };
        actions.Children.Add(replyButton);
        stack.Children.Add(actions);

        border.Child = stack;
        return border;
    }

    private async void CommentsMoreButton_Click(object sender, RoutedEventArgs e)
    {
        _commentsPage++;
        await LoadCommentsAsync();
    }

    private async void CommentSendButton_Click(object sender, RoutedEventArgs e)
    {
        if (_details == null || string.IsNullOrWhiteSpace(CommentInput.Text)) return;

        if (!RezkaService.Instance.IsLoggedIn)
        {
            await ShowLoginAsync();
            return;
        }

        var numericId = ExtractNumericId(_details.Id);
        if (numericId == null) return;

        try
        {
            CommentSendButton.IsEnabled = false;
            await RezkaService.Instance.Client.SendCommentAsync(numericId, CommentInput.Text.Trim(), _replyTo, _details.Adb);
            CommentInput.Text = "";
            _replyTo = null;
            CommentInput.PlaceholderText = Loc.Get("Comments.Placeholder");
            CommentsList.Children.Clear();
            _commentsPage = 1;
            await LoadCommentsAsync();
        }
        catch (RezkaException ex)
        {
            await ShowErrorAsync(RezkaService.Instance.ErrorText(ex));
        }
        finally
        {
            CommentSendButton.IsEnabled = true;
        }
    }

    private void RetryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_movie != null) _ = LoadAsync(_movie);
    }

    internal static string? ExtractNumericId(string cleanPath)
    {
        var seg = cleanPath.Split('/').LastOrDefault() ?? "";
        var first = seg.Split('-').FirstOrDefault() ?? "";
        return first.Length > 0 && first.All(char.IsDigit) ? first : null;
    }
}
