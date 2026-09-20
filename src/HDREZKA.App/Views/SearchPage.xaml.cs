using HDREZKA.App.Services;
using HDREZKA.Core.Api;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace HDREZKA.App.Views;

public sealed partial class SearchPage : Page
{
    private string _query = "";
    private readonly List<MovieSimple> _items = new();
    private int _page = 1;
    private bool _hasMore = true;
    private bool _loading;

    public SearchPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ApplyLocalization();

        var query = e.Parameter as string ?? "";
        if (query == _query) return;

        _query = query;
        _page = 1;
        _items.Clear();
        QueryText.Text = $"\"{_query}\"";
        MoreButton.Visibility = Visibility.Collapsed;
        _ = LoadAsync();
    }

    private void ApplyLocalization()
    {
        MoreButton.Content = Loc.Get("Common.LoadMore");
        RetryButton.Content = Loc.Get("Common.Retry");
    }

    private async Task LoadAsync()
    {
        if (_loading || _query.Length < 2) return;
        _loading = true;
        LoadingRing.IsActive = true;
        StatePanel.Visibility = Visibility.Visible;
        StateText.Text = Loc.Get("Common.Loading");
        RetryButton.Visibility = Visibility.Collapsed;

        try
        {
            var (_, items) = await RezkaService.Instance.Client.SearchAsync(_query, _page);
            _items.AddRange(items);
            ItemsGrid.ItemsSource = _items.ToList();
            _hasMore = items.Count > 0;

            if (_items.Count == 0)
            {
                StateText.Text = Loc.Get("Search.Nothing");
                LoadingRing.IsActive = false;
            }
            else
            {
                StatePanel.Visibility = Visibility.Collapsed;
            }

            MoreButton.Visibility = _hasMore && _items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (RezkaException ex)
        {
            StateText.Text = RezkaService.Instance.ErrorText(ex);
            LoadingRing.IsActive = false;
            RetryButton.Visibility = Visibility.Visible;
        }
        finally
        {
            _loading = false;
        }
    }

    private void MoreButton_Click(object sender, RoutedEventArgs e)
    {
        _page++;
        _ = LoadAsync();
    }

    private void RetryButton_Click(object sender, RoutedEventArgs e)
    {
        _page = 1;
        _items.Clear();
        _ = LoadAsync();
    }
}
