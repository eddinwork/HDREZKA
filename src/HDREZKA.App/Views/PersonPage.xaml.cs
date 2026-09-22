using HDREZKA.App.Services;
using HDREZKA.Core.Api;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;

namespace HDREZKA.App.Views;

public sealed partial class PersonPage : Page
{
    private PersonSimple? _person;
    private PersonDetailed? _details;

    private sealed record FilmRow(string Title, string? Details, string? Poster, MovieSimple Source);

    public PersonPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ApplyLocalization();
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;

        if (e.Parameter is PersonSimple person && _person?.Id != person.Id)
        {
            _person = person;
            _ = LoadAsync(person);
        }
        else if (e.Parameter is PersonDetailed details && _details?.Id != details.Id)
        {
            _person = new PersonSimple(details.Id, details.Name, details.Photo);
            LoadDetails(details);
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
    }

    private void OnLanguageChanged() => DispatcherQueue.TryEnqueue(() =>
    {
        ApplyLocalization();
        if (_details != null) Render(_details);
    });

    private void ApplyLocalization()
    {
        RetryButton.Content = Loc.Get("Common.Retry");
    }

    internal static string RoleName(string roleId) => roleId switch
    {
        "akter" => Loc.Get("Person.RoleActor"),
        "aktrisa" => Loc.Get("Person.RoleActress"),
        "rezhisser" => Loc.Get("Person.RoleDirector"),
        "prodyuser" => Loc.Get("Person.RoleProducer"),
        "scenarist" => Loc.Get("Person.RoleWriter"),
        "operator" => Loc.Get("Person.RoleCameraman"),
        "montazher" => Loc.Get("Person.RoleEditor"),
        "hudozhnik" => Loc.Get("Person.RoleArtist"),
        "kompozitor" => Loc.Get("Person.RoleComposer"),
        _ => roleId,
    };

    private async Task LoadAsync(PersonSimple person)
    {
        MainPanel.Visibility = Visibility.Collapsed;
        LoadingPanel.Visibility = Visibility.Visible;
        ErrorPanel.Visibility = Visibility.Collapsed;

        try
        {
            var details = await RezkaService.Instance.Client.GetPersonAsync(person.Id);
            LoadDetails(details);
        }
        catch (RezkaException ex)
        {
            App.TryLog(new Exception($"[Person] failed id={person.Id}: {ex.Kind} {ex.Message}"));
            LoadingPanel.Visibility = Visibility.Collapsed;
            ErrorPanel.Visibility = Visibility.Visible;
            ErrorText.Text = RezkaService.Instance.ErrorText(ex);
        }
    }

    private void LoadDetails(PersonDetailed details)
    {
        _details = details;
        DispatcherQueue.TryEnqueue(() =>
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            ErrorPanel.Visibility = Visibility.Collapsed;
            MainPanel.Visibility = Visibility.Visible;
            Render(details);
        });
    }

    private void Render(PersonDetailed details)
    {
        NameText.Text = details.Name;
        OrigText.Text = details.OriginalName ?? "";
        if (!string.IsNullOrEmpty(details.Photo))
        {
            PersonPoster.Source = new BitmapImage(new Uri(details.Photo));
        }

        MetaPanel.Children.Clear();
        AddMetaRow(Loc.Get("Person.Career"), details.Career);
        AddMetaRow(Loc.Get("Person.Born"), details.BirthDate);
        AddMetaRow(Loc.Get("Person.BirthPlace"), details.BirthPlace);
        AddMetaRow(Loc.Get("Person.Died"), details.DeathDate);
        AddMetaRow(Loc.Get("Person.DeathPlace"), details.DeathPlace);
        AddMetaRow(Loc.Get("Person.Height"), details.Height);

        FilmographyPanel.Children.Clear();
        if (details.Filmography != null)
        {
            foreach (var group in details.Filmography)
            {
                var header = new TextBlock
                {
                    Text = RoleName(group.RoleId),
                    Style = Application.Current.Resources["SectionHeader"] as Style,
                };
                FilmographyPanel.Children.Add(header);

                var list = new ListView
                {
                    SelectionMode = ListViewSelectionMode.None,
                    IsItemClickEnabled = true,
                };
                list.ItemClick += (_, e) =>
                {
                    if (e.ClickedItem is FilmRow row)
                    {
                        Nav.Go<DetailsPage>(row.Source);
                    }
                };
                list.ItemTemplate = (DataTemplate)Resources["PersonFilmTemplate"];
                list.ItemsSource = group.Movies
                    .Select(m => new FilmRow(m.Name ?? "", m.Details, m.Poster, m))
                    .ToList();
                FilmographyPanel.Children.Add(list);
            }
        }
    }

    private void AddMetaRow(string label, string? value)
    {
        if (string.IsNullOrEmpty(value)) return;
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(160) },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
            },
        };
        grid.Children.Add(new TextBlock
        {
            Text = label,
            Style = Application.Current.Resources["DetailsLabel"] as Style,
        });
        var val = new TextBlock
        {
            Text = value,
            TextWrapping = TextWrapping.Wrap,
            Style = Application.Current.Resources["DetailsValue"] as Style,
        };
        Grid.SetColumn(val, 1);
        grid.Children.Add(val);
        MetaPanel.Children.Add(grid);
    }

    private void RetryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_person != null) _ = LoadAsync(_person);
    }
}
