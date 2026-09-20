using HDREZKA.App.Services;
using HDREZKA.Core.Api;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Navigation;

namespace HDREZKA.App.Views;

public sealed record CatalogLaunch(Section Section, ListFilter Filter);

public sealed partial class CatalogPage : Page
{
    private static readonly Section[] Sections =
    [
        Section.Films, Section.Series, Section.Cartoons, Section.Show, Section.Anime,
    ];

    private static readonly ListFilter[] Filters =
    [
        ListFilter.Latest, ListFilter.Popular, ListFilter.Soon, ListFilter.WatchingNow,
    ];

    private readonly List<MovieSimple> _items = new();
    private Section _section = Section.Films;
    private ListFilter _filter = ListFilter.Latest;
    private int _page = 1;
    private bool _hasMore = true;
    private bool _loading;

    public CatalogPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ApplyLocalization();
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;

        if (e.Parameter is CatalogLaunch launch)
        {
            _section = launch.Section;
            _filter = launch.Filter;
            // Fresh target state: drop stale items so the new section loads.
            _items.Clear();
            ItemsGrid.ItemsSource = null;
        }

        if (SectionPanel.Children.Count == 0)
        {
            foreach (var section in Sections)
            {
                var tag = section.ToString();
                var button = new ToggleButton
                {
                    Content = Loc.Get("Section." + tag),
                    Tag = tag,
                    MinWidth = 40,
                };
                button.Click += SectionButton_Click;
                SectionPanel.Children.Add(button);
            }
        }

        MarkSection(_section);
        BuildFilters();
        if (_items.Count == 0)
        {
            Reload();
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
    }

    private void OnLanguageChanged()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            ApplyLocalization();
            BuildFilters();
            MarkSection(_section);
        });
    }

    private void ApplyLocalization()
    {
        TitleText.Text = Loc.Get("Catalog.Title");
        MoreButton.Content = Loc.Get("Common.LoadMore");
        RetryButton.Content = Loc.Get("Common.Retry");
        StateText.Text = Loc.Get("Common.Loading");

        var i = 0;
        foreach (var section in Sections)
        {
            if (SectionPanel.Children[i] is ToggleButton button)
            {
                button.Content = Loc.Get("Section." + section);
            }

            i++;
        }
    }

    private void MarkSection(Section section)
    {
        var i = 0;
        foreach (var s in Sections)
        {
            if (SectionPanel.Children[i] is ToggleButton button)
            {
                button.IsChecked = s == section;
            }

            i++;
        }
    }

    private void BuildFilters()
    {
        FilterBox.SelectionChanged -= FilterBox_SelectionChanged;
        FilterBox.Items.Clear();
        foreach (var filter in Filters)
        {
            FilterBox.Items.Add(new ComboBoxItem
            {
                Content = Loc.Get("Filter." + filter),
                Tag = filter,
            });
        }

        FilterBox.SelectedIndex = Array.IndexOf(Filters, _filter);
        FilterBox.SelectionChanged += FilterBox_SelectionChanged;
    }

    private void SectionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton button || button.Tag is not string tag) return;
        if (!Enum.TryParse<Section>(tag, out var section) || section == _section) return;

        _section = section;
        MarkSection(section);
        Reload();
    }

    private void FilterBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FilterBox.SelectedItem is ComboBoxItem { Tag: ListFilter filter } && filter != _filter)
        {
            _filter = filter;
            Reload();
        }
    }

    private void Reload()
    {
        _page = 1;
        _items.Clear();
        ItemsGrid.ItemsSource = null;
        _hasMore = true;
        ShowState(true, Loc.Get("Common.Loading"));
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (_loading) return;
        _loading = true;
        MoreRing.IsActive = true;
        MorePanel.Visibility = Visibility.Visible;
        MoreButton.IsEnabled = false;

        try
        {
            var (_, items) = await RezkaService.Instance.Client.GetSectionListAsync(_section, _filter, 0, _page);
            _items.AddRange(items);
            ItemsGrid.ItemsSource = _items.ToList();
            _hasMore = items.Count > 0;
            ShowState(false, "");
        }
        catch (RezkaException ex)
        {
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

    private void RetryButton_Click(object sender, RoutedEventArgs e) => Reload();
}
