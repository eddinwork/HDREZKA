using System.Text.Json;
using HDREZKA.Core.Api;

namespace HDREZKA.App.Services;

public sealed record LocalBookmarkItem(
    string Id,
    string Title,
    string? OriginalTitle,
    string? PosterUrl,
    string? Year,
    string? Details,
    string CategoryId,
    DateTime AddedAt);

public sealed class LocalBookmarkService
{
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HDREZKA");

    private static readonly string FilePath = Path.Combine(Dir, "local_bookmarks.json");

    public static LocalBookmarkService Instance { get; } = new();

    private List<LocalBookmarkItem> _items = new();
    private List<string> _customCategories = new();

    private static readonly string[] DefaultCategories = { "Буду смотреть", "Избранное", "Просмотрено" };

    private LocalBookmarkService()
    {
        Load();
    }

    public IReadOnlyList<BookmarkCategory> GetCategories()
    {
        var allCatNames = DefaultCategories.Concat(_customCategories).Distinct().ToList();
        var result = new List<BookmarkCategory>();
        for (int i = 0; i < allCatNames.Count; i++)
        {
            var name = allCatNames[i];
            var count = _items.Count(item => item.CategoryId == name);
            result.Add(new BookmarkCategory(i + 1, name, count));
        }
        return result;
    }

    public BookmarkCategory? GetCategoryById(int id)
    {
        return GetCategories().FirstOrDefault(c => c.Id == id);
    }

    public void AddCategory(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        name = name.Trim();
        if (!DefaultCategories.Contains(name) && !_customCategories.Contains(name))
        {
            _customCategories.Add(name);
            Persist();
        }
    }

    public void DeleteCategory(int id)
    {
        var cat = GetCategoryById(id);
        if (cat != null)
        {
            DeleteCategory(cat.Name);
        }
    }

    public void DeleteCategory(string name)
    {
        _customCategories.Remove(name);
        _items.RemoveAll(item => item.CategoryId == name);
        Persist();
    }

    public IReadOnlyList<MovieSimple> GetItems(int categoryId)
    {
        var cat = GetCategoryById(categoryId);
        if (cat == null) return Array.Empty<MovieSimple>();
        return GetItems(cat.Name);
    }

    public IReadOnlyList<MovieSimple> GetItems(string categoryName)
    {
        return _items
            .Where(i => i.CategoryId == categoryName)
            .OrderByDescending(i => i.AddedAt)
            .Select(i => new MovieSimple(
                Id: i.Id,
                Name: i.Title,
                Details: i.Details ?? i.Year,
                Poster: i.PosterUrl))
            .ToList();
    }

    public bool IsBookmarked(string id)
    {
        return _items.Any(i => i.Id == id);
    }

    public void AddBookmark(MovieDetailed details, int categoryId)
    {
        var cat = GetCategoryById(categoryId);
        var catName = cat?.Name ?? DefaultCategories[0];
        AddBookmark(details, catName);
    }

    public void AddBookmark(MovieDetailed details, string categoryName)
    {
        _items.RemoveAll(i => i.Id == details.Id);
        _items.Add(new LocalBookmarkItem(
            Id: details.Id,
            Title: details.Name,
            OriginalTitle: details.OriginalName,
            PosterUrl: details.Poster,
            Year: details.Year,
            Details: details.ReleaseDate ?? details.Year,
            CategoryId: categoryName,
            AddedAt: DateTime.UtcNow));
        Persist();
    }

    public void AddBookmark(MovieSimple movie, int categoryId)
    {
        var cat = GetCategoryById(categoryId);
        var catName = cat?.Name ?? DefaultCategories[0];
        AddBookmark(movie, catName);
    }

    public void AddBookmark(MovieSimple movie, string categoryName)
    {
        _items.RemoveAll(i => i.Id == movie.Id);
        _items.Add(new LocalBookmarkItem(
            Id: movie.Id,
            Title: movie.Name ?? "",
            OriginalTitle: null,
            PosterUrl: movie.Poster,
            Year: null,
            Details: movie.Details,
            CategoryId: categoryName,
            AddedAt: DateTime.UtcNow));
        Persist();
    }

    public void RemoveBookmark(string id)
    {
        _items.RemoveAll(i => i.Id == id);
        Persist();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var json = File.ReadAllText(FilePath);
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("Categories", out var cats))
            {
                _customCategories = JsonSerializer.Deserialize<List<string>>(cats.GetRawText()) ?? new();
            }
            if (doc.RootElement.TryGetProperty("Items", out var items))
            {
                _items = JsonSerializer.Deserialize<List<LocalBookmarkItem>>(items.GetRawText()) ?? new();
            }
        }
        catch
        {
            _items = new();
            _customCategories = new();
        }
    }

    private void Persist()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var obj = new
            {
                Categories = _customCategories,
                Items = _items
            };
            File.WriteAllText(FilePath, JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // best effort
        }
    }
}
