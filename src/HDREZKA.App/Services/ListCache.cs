using System.Collections.Concurrent;

namespace HDREZKA.App.Services;

/// <summary>
/// Маленький TTL-кэш ответов для главной страницы: возврат на «Главную»
/// не должен заново тянуть 7 запросов. Параллельные запросы одного ключа
/// склеиваются в один.
/// </summary>
public sealed class ListCache
{
    public static ListCache Instance { get; } = new();

    private readonly ConcurrentDictionary<string, Entry> _entries = new();
    private readonly ConcurrentDictionary<string, Task<object>> _inFlight = new(StringComparer.Ordinal);

    private readonly record struct Entry(object Value, DateTime ExpiresAt);

    public async Task<T> GetOrAddAsync<T>(string key, TimeSpan ttl, Func<Task<T>> factory)
    {
        if (_entries.TryGetValue(key, out var entry) && entry.ExpiresAt > DateTime.UtcNow && entry.Value is T cached)
        {
            return cached;
        }

        var task = _inFlight.GetOrAdd(key, _ => FetchAsync(key, ttl, factory));
        try
        {
            return (T)await task.ConfigureAwait(false);
        }
        finally
        {
            _inFlight.TryRemove(key, out _);
        }
    }

    private async Task<object> FetchAsync<T>(string key, TimeSpan ttl, Func<Task<T>> factory)
    {
        var value = await factory().ConfigureAwait(false);
        if (value is not null)
        {
            _entries[key] = new Entry(value, DateTime.UtcNow + ttl);
        }

        return value!;
    }

    /// <summary>Сброс после смены зеркала: старые данные могли прийти с другого домена.</summary>
    public void Clear() => _entries.Clear();
}
