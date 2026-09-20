using HDREZKA.App.Services;
using HDREZKA.Core.Api;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Navigation;

namespace HDREZKA.App.Views;

public sealed partial class BookmarksPage : Page
{
    private readonly List<MovieSimple> _items = new();
    private List<BookmarkCategory> _categories = new();
    private BookmarkCategory? _category;
    private int _page = 1;
    private bool _hasMore = true;
    private bool _loading;

    public BookmarksPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ApplyLocalization();
        if (_categories.Count == 0)
        {
            _ = LoadCategoriesAsync();
        }
    }

    private void ApplyLocalization()
    {
        TitleText.Text = Loc.Get("Nav.Bookmarks");
        MoreButton.Content = Loc.Get("Common.LoadMore");
        ToolTipService.SetToolTip(AddCategoryButton, Loc.Get("Bookmarks.NewCategory"));
    }

    private async Task LoadCategoriesAsync(string? selectName = null)
    {
        ShowState(true, Loc.Get("Common.Loading"));
        try
        {
            _categories = (await RezkaService.Instance.Client.GetBookmarkCategoriesAsync()).ToList();
            CategoriesPanel.Children.Clear();
            foreach (var category in _categories)
            {
                var button = new ToggleButton
                {
                    Content = $"{category.Name} ({category.Count})",
                    Tag = category,
                };
                button.Click += CategoryButton_Click;

                var flyout = new MenuFlyout();
                var deleteItem = new MenuFlyoutItem
                {
                    Text = Loc.Get("Bookmarks.DeleteCategory"),
                    Tag = category,
                };
                deleteItem.Click += DeleteCategory_Click;
                flyout.Items.Add(deleteItem);
                button.ContextFlyout = flyout;

                CategoriesPanel.Children.Add(button);
            }

            _category = !string.IsNullOrEmpty(selectName)
                ? _categories.FirstOrDefault(c => c.Name == selectName) ?? _categories.FirstOrDefault()
                : _categories.FirstOrDefault();
            MarkCategory(_category);
            if (_category == null)
            {
                ShowState(false, Loc.Get("Common.Empty"));
                return;
            }

            await ReloadItemsAsync();
        }
        catch (RezkaException ex)
        {
            ShowState(false, RezkaService.Instance.ErrorText(ex));
        }
    }

    private void MarkCategory(BookmarkCategory? category)
    {
        foreach (var child in CategoriesPanel.Children.OfType<ToggleButton>())
        {
            child.IsChecked = child.Tag is BookmarkCategory c && category != null && c.Id == category.Id;
        }
    }

    private async void AddCategoryButton_Click(object sender, RoutedEventArgs e)
    {
        var nameBox = new TextBox
        {
            PlaceholderText = Loc.Get("Bookmarks.CategoryName"),
            MaxLength = 64,
        };
        var dialog = new ContentDialog
        {
            Title = Loc.Get("Bookmarks.NewCategory"),
            Content = nameBox,
            PrimaryButtonText = Loc.Get("Common.Create"),
            CloseButtonText = Loc.Get("Common.Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var name = nameBox.Text.Trim();
        if (name.Length == 0) return;

        try
        {
            await RezkaService.Instance.Client.CreateBookmarkCategoryAsync(name);
            await LoadCategoriesAsync(selectName: name);
        }
        catch (RezkaException ex)
        {
            await ShowErrorAsync(RezkaService.Instance.ErrorText(ex));
        }
    }

    private async void DeleteCategory_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as MenuFlyoutItem)?.Tag is not BookmarkCategory category) return;

        var confirm = new ContentDialog
        {
            Title = Loc.Get("Bookmarks.DeleteCategory"),
            Content = string.Format(Loc.Get("Bookmarks.DeleteConfirm"), category.Name),
            PrimaryButtonText = Loc.Get("Common.Delete"),
            CloseButtonText = Loc.Get("Common.Cancel"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot,
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

        try
        {
            await RezkaService.Instance.Client.DeleteBookmarkCategoryAsync(category.Id);
            if (_category?.Id == category.Id) _category = null;
            await LoadCategoriesAsync();
        }
        catch (RezkaException ex)
        {
            await ShowErrorAsync(RezkaService.Instance.ErrorText(ex));
        }
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

    private void CategoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton button || button.Tag is not BookmarkCategory category) return;
        MarkCategory(category);

        _category = category;
        _ = ReloadItemsAsync();
    }

    private async Task ReloadItemsAsync()
    {
        _page = 1;
        _items.Clear();
        ItemsGrid.ItemsSource = null;
        await LoadItemsAsync();
    }

    private async Task LoadItemsAsync()
    {
        if (_loading || _category == null) return;
        _loading = true;
        ShowState(true, Loc.Get("Common.Loading"));

        try
        {
            var (_, items) = await RezkaService.Instance.Client.GetBookmarksAsync(_category.Id, ListFilter.Latest, 0, _page);
            _items.AddRange(items);
            ItemsGrid.ItemsSource = _items.ToList();
            _hasMore = items.Count > 0;
            ShowState(_items.Count == 0 && !_hasMore, _items.Count == 0 ? Loc.Get("Common.Empty") : "");
            MoreButton.Visibility = _hasMore && _items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (RezkaException ex)
        {
            ShowState(true, RezkaService.Instance.ErrorText(ex));
        }
        finally
        {
            _loading = false;
        }
    }

    private void ShowState(bool visible, string text)
    {
        StatePanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        LoadingRing.IsActive = visible;
        StateText.Text = text;
    }

    private void MoreButton_Click(object sender, RoutedEventArgs e)
    {
        _page++;
        _ = LoadItemsAsync();
    }
}
