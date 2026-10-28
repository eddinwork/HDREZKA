using System.Text;

namespace HDREZKA.App.Services;

/// <summary>
/// Логи с уровнями. Диагностические сообщения (навигация, плеер, зеркала)
/// идут в app.log, настоящие ошибки — в crash.log (через App.TryLog).
/// Раньше вся диагностика писалась в crash.log как исключения и вытесняла
/// оттуда реальные падения.
/// </summary>
public static class Log
{
    private static readonly object Gate = new();
    private const long MaxBytes = 1024 * 1024;

    private static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HDREZKA");

    public static string InfoPath => Path.Combine(Dir, "app.log");

    public static void Info(string message) => Append("INFO", message);

    public static void Warn(string message) => Append("WARN", message);

    public static void Error(string message, Exception? ex = null)
    {
        Append("ERROR", message);
        if (ex != null) App.TryLog(ex);
    }

    private static void Append(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Dir);
                var line = new StringBuilder()
                    .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                    .Append(" [").Append(level).Append("] ")
                    .AppendLine(message)
                    .ToString();
                File.AppendAllText(InfoPath, line);
                Rotate();
            }
        }
        catch
        {
            // логирование не должно ронять приложение
        }
    }

    private static void Rotate()
    {
        try
        {
            var info = new FileInfo(InfoPath);
            if (!info.Exists || info.Length <= MaxBytes) return;
            var lines = File.ReadAllLines(InfoPath);
            File.WriteAllLines(InfoPath, lines.Skip(Math.Max(0, lines.Length - 2000)));
        }
        catch
        {
        }
    }
}
