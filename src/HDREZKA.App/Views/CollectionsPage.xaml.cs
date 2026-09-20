using HDREZKA.App.Services;
using HDREZKA.Core.Api;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Navigation;

namespace HDREZKA.App.Views;

public sealed partial class CollectionsPage : Page
{
    private readonly List<MovieSimple> _items = new();
    private readonly List<CollectionSimple> _collections = new();
    private string _currentCollectionId = "";
    private int _page = 1;
    private bool _hasMore = true;
    private bool _loading;
    private bool _collectionsLoaded;

    public CollectionsPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ApplyLocalization();
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        SettingsService.Instance.PosterSizeChanged += OnPosterSizeChanged;

        if (!_collectionsLoaded)
        {
            LoadCollections();
        }
        else if (CollectionsPanel.Children.Count > 0 && _items.Count == 0)
        {
            // Select first collection by default
            if (CollectionsPanel.Children[0] is ToggleButton firstButton)
            {
                firstButton.IsChecked = true;
                SelectCollection(firstButton.Tag?.ToString() ?? "");
            }
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        SettingsService.Instance.PosterSizeChanged -= OnPosterSizeChanged;
    }

    private void OnPosterSizeChanged() => DispatcherQueue.TryEnqueue(RebuildFolderCards);

    private void RebuildFolderCards()
    {
        if (!_collectionsLoaded || _collections.Count == 0) return;
        var selected = _currentCollectionId;
        CollectionsPanel.Children.Clear();
        foreach (var col in _collections)
        {
            var button = BuildFolderCard(col);
            button.IsChecked = col.Id == selected;
            CollectionsPanel.Children.Add(button);
        }
    }

    private void OnLanguageChanged()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            ApplyLocalization();
        });
    }

    private void ApplyLocalization()
    {
        TitleText.Text = Loc.Get("Nav.Collections");
        MoreButton.Content = Loc.Get("Common.LoadMore");
        RetryButton.Content = Loc.Get("Common.Retry");
        StateText.Text = Loc.Get("Common.Loading");
    }

    private async void LoadCollections()
    {
        LoadingRing.IsActive = true;
        StatePanel.Visibility = Visibility.Visible;
        StateText.Text = Loc.Get("Common.Loading");

        try
        {
            var collections = await RezkaService.Instance.Client.GetAllCollectionsAsync();
            _collections.Clear();
            _collections.AddRange(collections);

            App.TryLog(new Exception($"[Collections] folders={collections.Count} ids={string.Join(",", collections.Select(c => c.Id))}"));

            _collectionsLoaded = true;

            CollectionsPanel.Children.Clear();
            foreach (var col in collections)
            {
                CollectionsPanel.Children.Add(BuildFolderCard(col));
            }

            // Select first collection by default
            if (CollectionsPanel.Children.Count > 0 && CollectionsPanel.Children[0] is ToggleButton firstButton)
            {
                firstButton.IsChecked = true;
                SelectCollection(firstButton.Tag?.ToString() ?? "");
            }
            else
            {
                ShowState(true, Loc.Get("Common.Empty"), showRetry: true);
            }
        }
        catch (RezkaException ex)
        {
            App.TryLog(new Exception("[Collections] folders failed: " + ex.Message));
            ShowState(true, RezkaService.Instance.ErrorText(ex), showRetry: true);
        }
        catch (Exception ex)
        {
            App.TryLog(ex);
            ShowState(true, Loc.Get("Error.Network"), showRetry: true);
        }
    }

    private ToggleButton BuildFolderCard(CollectionSimple col)
    {
        var width = PosterMetrics.Base;
        var posterHeight = PosterMetrics.FolderHeight;

        var posterBorder = new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Width = width,
            Height = posterHeight,
            Clip = new Microsoft.UI.Xaml.Media.RectangleGeometry
            {
                Rect = new Windows.Foundation.Rect(0, 0, width, posterHeight),
            },
        };

        if (!string.IsNullOrEmpty(col.Poster) && Uri.TryCreate(col.Poster, UriKind.Absolute, out var uri))
        {
            posterBorder.Child = new Image
            {
                Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(uri),
                Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill,
                HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Center,
                VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center,
            };
        }
        else
        {
            // Text-only folders: centered folder glyph instead of an empty box.
            posterBorder.Child = new FontIcon
            {
                Glyph = "\uE8B7",
                FontSize = 40,
                Foreground = (Microsoft.UI.Xaml.Media.Brush?)Application.Current.Resources["TextFillColorTertiaryBrush"],
                HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Center,
                VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center,
            };
        }

        var title = new TextBlock
        {
            Text = col.Title,
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2,
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Margin = new Thickness(2, 6, 2, 0),
        };

        var card = new StackPanel { Width = width };
        card.Children.Add(posterBorder);
        card.Children.Add(title);
        if (col.Count > 0)
        {
            card.Children.Add(new TextBlock
            {
                Text = col.Count.ToString(),
                FontSize = 11,
                Margin = new Thickness(2, 2, 2, 0),
                Foreground = (Microsoft.UI.Xaml.Media.Brush?)Application.Current.Resources["TextFillColorSecondaryBrush"],
            });
        }

        var button = new ToggleButton
        {
            Content = card,
            Tag = col.Id,
            MinWidth = 40,
            Margin = new Thickness(0, 0, 8, 0),
            Padding = new Thickness(2),
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
        };
        button.Click += CollectionButton_Click;
        return button;
    }

    private void CollectionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton button || button.Tag is not string collectionId) return;
        if (collectionId == _currentCollectionId) return;

        foreach (var child in CollectionsPanel.Children)
        {
            if (child is ToggleButton btn && btn != button)
            {
                btn.IsChecked = false;
            }
        }

        button.IsChecked = true;
        SelectCollection(collectionId);
    }

    private void SelectCollection(string collectionId)
    {
        _currentCollectionId = collectionId;
        _page = 1;
        _items.Clear();
        ItemsGrid.ItemsSource = null;
        _hasMore = true;
        SubtitleText.Visibility = Visibility.Collapsed;
        ItemsScroll.ScrollToVerticalOffset(0);
        ShowState(true, Loc.Get("Common.Loading"));
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (_loading || string.IsNullOrEmpty(_currentCollectionId)) return;
        _loading = true;
        MoreRing.IsActive = true;
        MorePanel.Visibility = Visibility.Visible;
        MoreButton.IsEnabled = false;

        try
        {
            var (title, items) = await RezkaService.Instance.Client.GetCollectionsListAsync(_currentCollectionId, _page);
            _items.AddRange(items);
            ItemsGrid.ItemsSource = _items.ToList();
            _hasMore = items.Count > 0;
            App.TryLog(new Exception($"[Collections] items: id={_currentCollectionId} page={_page} got={items.Count} total={_items.Count}"));

            if (!string.IsNullOrWhiteSpace(title))
            {
                SubtitleText.Text = title;
                SubtitleText.Visibility = Visibility.Visible;
            }

            if (_items.Count == 0)
            {
                ShowState(true, Loc.Get("Common.Empty"));
            }
            else
            {
                ShowState(false, "");
            }
        }
        catch (RezkaException ex)
        {
            App.TryLog(new Exception($"[Collections] items failed: id={_currentCollectionId} page={_page} {ex.Message}"));
            ShowState(true, RezkaService.Instance.ErrorText(ex), showRetry: true);
        }
        finally
        {
            _loading = false;
            MoreRing.IsActive = false;
            MoreButton.IsEnabled = true;
            MorePanel.Visibility = _hasMore && _items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void ShowState(bool visible, string text, bool showRetry = false)
    {
        StatePanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        LoadingRing.IsActive = visible && !showRetry;
        StateText.Text = text;
        RetryButton.Visibility = showRetry ? Visibility.Visible : Visibility.Collapsed;
        if (showRetry && StatePanel.Visibility == Visibility.Collapsed)
        {
            StatePanel.Visibility = Visibility.Visible;
        }

        ItemsGrid.Visibility = _items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void MoreButton_Click(object sender, RoutedEventArgs e)
    {
        _page++;
        _ = LoadAsync();
    }

    private void ItemsScroll_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        if (e.IsIntermediate || !_hasMore || _loading || _items.Count == 0) return;

        var verticalOffset = ItemsScroll.VerticalOffset;
        var max = ItemsScroll.ScrollableHeight;
        if (max > 0 && verticalOffset / max >= 0.92)
        {
            _page++;
            _ = LoadAsync();
        }
    }

    private void RetryButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_collectionsLoaded || CollectionsPanel.Children.Count == 0)
        {
            // Collections themselves are missing: retry them, not the items.
            LoadCollections();
            return;
        }

        _page = 1;
        _items.Clear();
        ItemsGrid.ItemsSource = null;
        _hasMore = true;
        ShowState(true, Loc.Get("Common.Loading"));
        _ = LoadAsync();
    }
}