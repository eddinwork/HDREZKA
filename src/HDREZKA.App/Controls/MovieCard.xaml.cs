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

public sealed partial class MovieCard : UserControl
{
    public static readonly DependencyProperty MovieProperty = DependencyProperty.Register(
        nameof(Movie), typeof(MovieSimple), typeof(MovieCard), new PropertyMetadata(null, OnMovieChanged));

    public MovieSimple? Movie
    {
        get => (MovieSimple?)GetValue(MovieProperty);
        set => SetValue(MovieProperty, value);
    }

    public MovieCard()
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
        OuterGrid.Width = w + 4;
        PosterGrid.Width = w;
        PosterGrid.Height = h;
    }

    private static void OnMovieChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MovieCard card)
        {
            card.Update();
        }
    }

    private void Update()
    {
        if (Movie == null) return;

        TitleText.Text = Movie.Name ?? "";
        DetailsText.Text = Movie.Details ?? "";

        if (!string.IsNullOrEmpty(Movie.Poster))
        {
            PosterImage.Source = new BitmapImage(new Uri(Movie.Poster));
        }

        RatingText.Text = Movie.Rating?.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) ?? "";
        (RatingText.Parent as FrameworkElement)!.Visibility = Movie.Rating != null ? Visibility.Visible : Visibility.Collapsed;

        if (Movie.Info == MediaInfoType.Series && Movie.Season is { } s && Movie.Episode is { } e)
        {
            InfoBadge.Visibility = Visibility.Visible;
            InfoText.Text = Loc.Get("Info.Series", s, e);
        }
        else if (Movie.Info == MediaInfoType.Completed)
        {
            InfoBadge.Visibility = Visibility.Visible;
            InfoText.Text = Loc.Get("Info.Completed");
        }
        else if (Movie.Info == MediaInfoType.Wait)
        {
            InfoBadge.Visibility = Visibility.Visible;
            InfoText.Text = Loc.Get("Info.Wait");
        }
        else
        {
            InfoBadge.Visibility = Visibility.Collapsed;
        }
    }

    private void CardButton_Click(object sender, RoutedEventArgs e)
    {
        if (Movie == null) return;
        Nav.Go<DetailsPage>(Movie);
    }

    private void CardButton_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        AnimateScale(1.05f);
        AnimateOpacity(HoverOverlay, 1);
    }

    private void CardButton_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        AnimateScale(1.0f);
        AnimateOpacity(HoverOverlay, 0);
    }

    private void CardButton_PointerPressed(object sender, PointerRoutedEventArgs e) => AnimateScale(0.97f);

    private void CardButton_PointerReleased(object sender, PointerRoutedEventArgs e) => AnimateScale(1.0f);

    private void AnimateScale(float to)
    {
        var transform = new ScaleTransform
        {
            CenterX = OuterGrid.Width / 2,
            CenterY = PosterGrid.Height / 2,
        };
        CardButton.RenderTransform = transform;
        var storyboard = new Storyboard();
        foreach (var property in new[] { "ScaleX", "ScaleY" })
        {
            var animation = new DoubleAnimation
            {
                To = to,
                Duration = TimeSpan.FromMilliseconds(140),
                EnableDependentAnimation = true,
            };
            Storyboard.SetTarget(animation, transform);
            Storyboard.SetTargetProperty(animation, property);
            storyboard.Children.Add(animation);
        }

        storyboard.Begin();
    }

    private static void AnimateOpacity(UIElement element, double to)
    {
        var animation = new DoubleAnimation
        {
            To = to,
            Duration = TimeSpan.FromMilliseconds(140),
        };
        Storyboard.SetTarget(animation, element);
        Storyboard.SetTargetProperty(animation, "Opacity");
        var storyboard = new Storyboard { Children = { animation } };
        storyboard.Begin();
    }
}
