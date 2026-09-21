using HDREZKA.App.Services;
using HDREZKA.Core.Api;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace HDREZKA.App.Views;

public sealed partial class UpdatesPage : Page
{
    private sealed record UpdateRow(
        string Title,
        string Text,
        string Date,
        string? Poster,
        StoredUpdate Source);

    public UpdatesPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ApplyLocalization();
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        Render();
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
            Render();
        });
    }

    private void ApplyLocalization()
    {
        TitleText.Text = Loc.Get("Updates.Title");
        ClearButton.Content = Loc.Get("Updates.Clear");
        EmptyText.Text = Loc.Get("Updates.Empty");
    }

    private void Render()
    {
        var rows = TrackedSeriesService.GetUpdates()
            .Select(u => new UpdateRow(
                u.Title,
                u.Text,
                u.Date.ToString("dd.MM.yyyy HH:mm"),
                u.Poster,
                u))
            .ToList();

        List.ItemsSource = rows;
        List.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyPanel.Visibility = rows.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        ClearButton.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void List_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not UpdateRow row) return;
        // Consumed: open details, drop the entry (badge updates via event).
        TrackedSeriesService.RemoveUpdate(row.Source);
        Render();
        Nav.Go<DetailsPage>(new MovieSimple(Id: row.Source.PagePath, Name: row.Source.Title, Poster: row.Source.Poster));
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        TrackedSeriesService.ClearUpdates();
        Render();
    }
}
