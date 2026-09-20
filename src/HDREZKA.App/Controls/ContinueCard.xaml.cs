using HDREZKA.App.Services;
using HDREZKA.App.Views;
using HDREZKA.Core.Api;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;

namespace HDREZKA.App.Controls;

public sealed partial class ContinueCard : UserControl
{
    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data), typeof(ContinueItem), typeof(ContinueCard), new PropertyMetadata(null, OnDataChanged));

    public ContinueItem? Data
    {
        get => (ContinueItem?)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public event EventHandler<ContinueItem>? DeleteRequested;

    public ContinueCard()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            ApplyPosterSize();
            SettingsService.Instance.PosterSizeChanged += OnPosterSizeChanged;
        };
        Unloaded += (_, _) => SettingsService.Instance.PosterSizeChanged -= OnPosterSizeChanged;
    }

    private void OnPosterSizeChanged() => DispatcherQueue.TryEnqueue(ApplyPosterSize);

    private void ApplyPosterSize()
    {
        var w = PosterMetrics.CardWidth;
        var h = PosterMetrics.CardHeight;
        CardLayer.Width = w;
        PosterHost.Width = w;
        PosterHost.Height = h;
        PosterClipRect.Rect = new Windows.Foundation.Rect(0, 0, w, h);
        ScaleTransform.CenterX = w / 2.0;
        ScaleTransform.CenterY = h / 2.0;
    }

    private static void OnDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((ContinueCard)d).Update();
    }

    private void Update()
    {
        var data = Data;
        if (data == null) return;

        TitleText.Text = data.Title;
        SubtitleText.Text = data.Subtitle;

        if (data.DurationSeconds > 1)
        {
            ProgressBar.Value = Math.Clamp(data.PositionSeconds / data.DurationSeconds, 0, 1);
            ProgressPanel.Visibility = Visibility.Visible;
        }
        else
        {
            ProgressPanel.Visibility = Visibility.Collapsed;
        }

        WatchedBadge.Visibility = data.IsWatched ? Visibility.Visible : Visibility.Collapsed;
        if (data.IsWatched)
        {
            WatchedText.Text = Loc.Get("Common.Watched");
        }

        InfoText.Text = data.Info ?? "";
        InfoText.Visibility = string.IsNullOrEmpty(data.Info) ? Visibility.Collapsed : Visibility.Visible;

        if (!string.IsNullOrEmpty(data.Poster) && Uri.TryCreate(data.Poster, UriKind.Absolute, out var uri))
        {
            PosterImage.Source = new BitmapImage(uri);
        }
    }

    // ---------- interactions ----------

    private void CardContent_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (Data == null) return;
        // Clicks on the delete button bubble up here: ignore them,
        // otherwise deleting an entry also navigates to details.
        if (IsDeleteButtonSource(e.OriginalSource)) return;
        Nav.Go<DetailsPage>(new MovieSimple(Id: Data.Id, Name: Data.Title, Poster: Data.Poster));
    }

    private bool IsDeleteButtonSource(object? source)
    {
        var current = source as DependencyObject;
        while (current != null)
        {
            if (ReferenceEquals(current, DeleteButton)) return true;
            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (Data != null)
        {
            DeleteRequested?.Invoke(this, Data);
        }
    }

    private void Card_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        AnimateScale(1.03);
        AnimateOpacity(HoverOverlay, 1);
        AnimateOpacity(DeleteButton, 1);
    }

    private void Card_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        AnimateScale(1.0);
        AnimateOpacity(HoverOverlay, 0);
        AnimateOpacity(DeleteButton, 0);
    }

    private void Card_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        AnimateScale(0.97, 90);
    }

    private void Card_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        AnimateScale(1.03, 120);
    }

    private void AnimateScale(double scale, int durationMs = 140)
    {
        var sb = new Storyboard();
        foreach (var prop in new[] { "ScaleX", "ScaleY" })
        {
            var anim = new DoubleAnimation
            {
                To = scale,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                EnableDependentAnimation = true,
            };
            Storyboard.SetTarget(anim, ScaleTransform);
            Storyboard.SetTargetProperty(anim, prop);
            sb.Children.Add(anim);
        }

        sb.Begin();
    }

    private void AnimateOpacity(UIElement el, double to, int durationMs = 140)
    {
        var sb = new Storyboard();
        var anim = new DoubleAnimation
        {
            To = to,
            Duration = TimeSpan.FromMilliseconds(durationMs),
        };
        Storyboard.SetTarget(anim, el);
        Storyboard.SetTargetProperty(anim, "Opacity");
        sb.Children.Add(anim);
        sb.Begin();
    }
}