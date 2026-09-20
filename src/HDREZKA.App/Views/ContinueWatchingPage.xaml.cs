using HDREZKA.App.Controls;
using HDREZKA.App.Services;
using HDREZKA.Core.Api;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace HDREZKA.App.Views;

public sealed partial class ContinueWatchingPage : Page
{
    private readonly List<ContinueItem> _items = new();
    private readonly HashSet<string> _accountIds = new(StringComparer.OrdinalIgnoreCase);
    private bool _accountFaulted;

    public ContinueWatchingPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ApplyLocalization();
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        Render();
        LoadFromAccount();
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
        TitleText.Text = Loc.Get("Continue.Title");
        EmptyText.Text = Loc.Get("Continue.Empty");
        RetryButton.Content = Loc.Get("Common.Retry");
    }

    private void Render()
    {
        List.Items.Clear();
        _items.Clear();
        _accountIds.Clear();

        foreach (var position in PositionService.Instance.GetAll())
        {
            if (IsTestEntry(position.MovieId, position.Title)) continue;
            _items.Add(FromLocal(position));
        }

        RenderList();
    }

    private static bool IsTestEntry(string movieId, string? title) =>
        movieId == "99999"
        || (!string.IsNullOrEmpty(title) &&
            (title.Contains("test", StringComparison.OrdinalIgnoreCase)
             || title.Contains("тест", StringComparison.OrdinalIgnoreCase)));

    private static ContinueItem FromLocal(WatchPosition p)
    {
        var remainingMin = (int)Math.Max(1, (p.DurationSeconds - p.PositionSeconds) / 60);
        var subtitle = p.SeasonId != null && p.EpisodeId != null
            ? $"{Loc.Get("Common.Season")} {p.SeasonId} · {Loc.Get("Common.Episode")} {p.EpisodeId}"
            : p.UpdatedAt.ToString("dd.MM.yyyy");

        return new ContinueItem(
            Id: p.PagePath ?? p.MovieId,
            Title: p.Title ?? "",
            Poster: p.PosterUrl,
            Subtitle: subtitle,
            PositionSeconds: p.PositionSeconds,
            DurationSeconds: p.DurationSeconds,
            Info: Loc.Get("Continue.Left", remainingMin),
            UpdatedAt: p.UpdatedAt);
    }

    private async void LoadFromAccount()
    {
        var client = RezkaService.Instance.Client;
        if (!RezkaService.Instance.IsLoggedIn)
        {
            _accountFaulted = false;
            return;
        }

        LoadingRing.IsActive = true;
        try
        {
            var account = await client.GetContinueWatchingAsync();
            var positions = PositionService.Instance.GetAll()
                .GroupBy(p => p.MovieId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.UpdatedAt).First());

            foreach (var item in account)
            {
                if (IsTestEntry(ExtractNumericId(item.Id) ?? item.Id, item.Title))
                    continue;

                _accountIds.Add(item.Id);
                var numeric = ExtractNumericId(item.Id);
                WatchPosition? match = numeric != null && positions.TryGetValue(numeric, out var pos) ? pos : null;

                string subtitle = match != null && match.SeasonId != null && match.EpisodeId != null
                    ? $"{Loc.Get("Common.Season")} {match.SeasonId} · {Loc.Get("Common.Episode")} {match.EpisodeId}"
                    : item.Details;

                _items.Insert(0, new ContinueItem(
                    Id: item.Id,
                    Title: item.Title,
                    Poster: item.Poster,
                    Subtitle: subtitle,
                    PositionSeconds: match?.PositionSeconds ?? 0,
                    DurationSeconds: match?.DurationSeconds ?? 0,
                    Info: string.IsNullOrEmpty(item.Info) ? null : item.Info,
                    ActionLabel: item.ActionLabel,
                    DataId: item.DataId,
                    IsWatched: item.Watched,
                    UpdatedAt: match?.UpdatedAt));
            }

            // drop stale local duplicates that are now tracked by the account
            _items.RemoveAll(i => i.DataId == null && _accountIds.Contains(i.Id));
        }
        catch (RezkaException ex)
        {
            _accountFaulted = true;
            ShowError(RezkaService.Instance.ErrorText(ex));
        }
        finally
        {
            LoadingRing.IsActive = false;
            RenderList();
        }
    }

    private void RenderList()
    {
        List.Items.Clear();
        foreach (var item in _items.OrderByDescending(i => i.UpdatedAt ?? DateTime.MinValue))
        {
            List.Items.Add(BuildCard(item));
        }

        ErrorPanel.Visibility = _accountFaulted && _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyPanel.Visibility = !_accountFaulted && _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        List.Visibility = _items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private ContinueCard BuildCard(ContinueItem item)
    {
        var card = new ContinueCard
        {
            Data = item,
            Margin = new Thickness(0, 0, 18, 26),
            IsHitTestVisible = true,
        };
        card.DeleteRequested += OnDeleteRequested;
        return card;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorPanel.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RetryButton_Click(object sender, RoutedEventArgs e)
    {
        _accountFaulted = false;
        Render();
        LoadFromAccount();
    }

    private async void OnDeleteRequested(object? sender, ContinueItem item)
    {
        // Optimistic local removal: works offline and for local-only entries.
        _items.Remove(item);
        _accountIds.Remove(item.Id);
        PositionService.Instance.Remove(item.Id);
        RenderList();

        // Best-effort server removal for account entries.
        if (string.IsNullOrEmpty(item.DataId)) return;

        try
        {
            await RezkaService.Instance.Client.RemoveWatchedItemAsync(item.DataId);
        }
        catch (RezkaException ex)
        {
            var dialog = new ContentDialog
            {
                Title = Loc.Get("Common.Error"),
                Content = RezkaService.Instance.ErrorText(ex),
                CloseButtonText = "OK",
                XamlRoot = Content.XamlRoot,
            };
            await dialog.ShowAsync();
        }
    }

    internal static string? ExtractNumericId(string cleanPath)
    {
        var seg = cleanPath.Split('/').LastOrDefault() ?? "";
        var first = seg.Split('-').FirstOrDefault() ?? "";
        return first.Length > 0 && first.All(char.IsDigit) ? first : null;
    }
}