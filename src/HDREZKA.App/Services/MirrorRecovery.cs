namespace HDREZKA.App.Services;

/// <summary>
/// Единая точка «спасения» после 403/сетевой ошибки: проверяет текущее
/// зеркало и переключается на рабочее. Страницы вызывают её не более одного
/// раза на неудачную загрузку, иначе получается бесконечный ретрай.
/// </summary>
public static class MirrorRecovery
{
    /// <param name="contentPath">
    /// Страница, на которой произошла ошибка (например, путь фильма): 403
    /// бывает избирательным, и проверки одной главной недостаточно.
    /// </param>
    /// <returns>true, если зеркало было автоматически сменено на рабочее.</returns>
    public static async Task<bool> TryRecoverAsync(string? contentPath = null, CancellationToken ct = default)
    {
        // Ограничиваем время: проверка нескольких зеркал на битом соединении
        // иначе держит страницу в состоянии загрузки слишком долго.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(25));

        try
        {
            var switched = await MirrorService.EnsureWorkingMirrorAsync(contentPath, timeout.Token).ConfigureAwait(false);
            return switched != null;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
