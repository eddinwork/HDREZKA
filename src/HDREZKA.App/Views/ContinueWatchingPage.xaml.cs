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
        BuildSortBox();
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
        BuildSortBox();
    }

    private void BuildSortBox()
    {
        SortBox.SelectionChanged -= SortBox_SelectionChanged;
        SortBox.Items.Clear();
        SortBox.Items.Add(new ComboBoxItem { Content = Loc.Get("Continue.SortNewest"), Tag = false });
        SortBox.Items.Add(new ComboBoxItem { Content = Loc.Get("Continue.SortOldest"), Tag = true });
        SortBox.SelectedIndex = SettingsService.Instance.ContinueOldestFirst ? 1 : 0;
        SortBox.SelectionChanged += SortBox_SelectionChanged;
    }

    private void SortBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SortBox.SelectedItem is ComboBoxItem { Tag: bool oldest })
        {
            SettingsService.Instance.ContinueOldestFirst = oldest;
            SettingsService.Instance.Save();
            RenderList();
        }
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

    internal static bool IsLocalTestEntry(string movieId, string? title) => IsTestEntry(movieId, title);

    /// <summary>
    /// Server "continue" dates look like "12.09.2026", sometimes with time
    /// or relative words. Unparseable → null (caller falls back).
    /// </summary>
    internal static DateTime? ParseAccountDate(string date)
    {
        if (string.IsNullOrWhiteSpace(date)) return null;
        var s = date.Trim();
        var today = DateTime.Today;

        if (s.StartsWith("сегодня", StringComparison.OrdinalIgnoreCase)
            || s.StartsWith("today", StringComparison.OrdinalIgnoreCase)
            || s.StartsWith("сьогодні", StringComparison.OrdinalIgnoreCase))
        {
            return today;
        }

        if (s.StartsWith("вчера", StringComparison.OrdinalIgnoreCase)
            || s.StartsWith("yesterday", StringComparison.OrdinalIgnoreCase)
            || s.StartsWith("вчора", StringComparison.OrdinalIgnoreCase))
        {
            return today.AddDays(-1);
        }

        string[] formats =
        [
            "dd.MM.yyyy HH:mm", "dd.MM.yyyy H:mm", "dd.MM.yyyy",
            "dd-MM-yyyy HH:mm", "dd-MM-yyyy",
            "yyyy-MM-dd HH:mm", "yyyy-MM-dd",
            "dd/MM/yyyy",
        ];
        if (DateTime.TryParseExact(s, formats,
                System.Globalization.CultureInfo.GetCultureInfo("ru-RU"),
                System.Globalization.DateTimeStyles.None, out var exact))
        {
            return exact;
        }

        if (DateTime.TryParse(s, out var generic)) return generic;
        return null;
    }

    internal static ContinueItem FromLocal(WatchPosition p)
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
            UpdatedAt: p.UpdatedAt,
            IsWatched: PositionService.Instance.IsWatched(p.MovieId, p.TranslatorId, p.SeasonId, p.EpisodeId),
            MovieKey: p.MovieId,
            TranslatorKey: p.TranslatorId,
            SeasonKey: p.SeasonId,
            EpisodeKey: p.EpisodeId);
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

                // Server date first (site order is newest-first), local match
                // refines it — take the freshest of the two.
                var serverDate = ParseAccountDate(item.Date);
                DateTime? updated = match?.UpdatedAt;
                if (serverDate != null && (updated == null || serverDate > updated))
                {
                    updated = serverDate;
                }

                // Append (not prepend): OrderBy is stable, so entries with
                // equal/unparseable dates keep the server newest-first order.
                _items.Add(new ContinueItem(
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
                    UpdatedAt: updated,
                    MovieKey: match?.MovieId,
                    TranslatorKey: match?.TranslatorId,
                    SeasonKey: match?.SeasonId,
                    EpisodeKey: match?.EpisodeId));
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
        var ordered = SettingsService.Instance.ContinueOldestFirst
            ? _items.OrderBy(i => i.UpdatedAt ?? DateTime.MinValue)
            : _items.OrderByDescending(i => i.UpdatedAt ?? DateTime.MinValue);
        foreach (var item in ordered)
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
        card.WatchedToggled += OnWatchedToggled;
        return card;
    }

    /// <summary>
    /// Flips the watched checkmark. Account entries toggle the server flag,
    /// local entries toggle the offline key. Returns the updated item,
    /// or null on error / missing key. Must be called on the UI thread.
    /// </summary>
    internal static async Task<ContinueItem?> ToggleWatchedAsync(ContinueItem item, Microsoft.UI.Xaml.XamlRoot? xamlRoot)
    {
        if (!string.IsNullOrEmpty(item.DataId))
        {
            try
            {
                await RezkaService.Instance.Client.MarkWatchedItemAsync(item.DataId);
                return item with { IsWatched = !item.IsWatched };
            }
            catch (RezkaException ex)
            {
                if (xamlRoot != null)
                {
                    var dialog = new ContentDialog
                    {
                        Title = Loc.Get("Common.Error"),
                        Content = RezkaService.Instance.ErrorText(ex),
                        CloseButtonText = "OK",
                        XamlRoot = xamlRoot,
                    };
                    await dialog.ShowAsync();
                }

                return null;
            }
        }

        if (item.MovieKey == null || item.TranslatorKey == null) return null;
        var now = PositionService.Instance.ToggleWatched(item.MovieKey, item.TranslatorKey, item.SeasonKey, item.EpisodeKey);
        return item with { IsWatched = now };
    }

    private async void OnWatchedToggled(object? sender, ContinueItem item)
    {
        var updated = await ToggleWatchedAsync(item, Content.XamlRoot);
        if (updated == null) return;
        var idx = _items.IndexOf(item);
        if (idx >= 0) _items[idx] = updated;
        RenderList();
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