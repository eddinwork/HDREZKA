namespace HDREZKA.Core.Api;

public enum RezkaError
{
    Network,
    MirrorBanned,
    LoginRequired,
    AccessDenied,
    NotFound,
    InsecureMirror,
    Site,
    Parse
}

public sealed class RezkaException : Exception
{
    public RezkaError Kind { get; }

    public RezkaException(RezkaError kind, string message) : base(message)
    {
        Kind = kind;
    }

    public static RezkaException Parse(string what) => new(RezkaError.Parse, $"Failed to parse: {what}");
    public static RezkaException Site(string message) => new(RezkaError.Site, message);
    public static RezkaException LoginRequired() => new(RezkaError.LoginRequired, "Login required");
    public static RezkaException AccessDenied() => new(RezkaError.AccessDenied, "Access denied by site");
    public static RezkaException MirrorBanned() => new(RezkaError.MirrorBanned, "Mirror is unavailable");

    public static RezkaException NotFound() => new(RezkaError.NotFound, "Title not found on the site");

    /// <summary>Пароль нельзя отправлять по http: зеркало обязано быть https.</summary>
    public static RezkaException InsecureMirror() =>
        new(RezkaError.InsecureMirror, "Mirror must use HTTPS for sign-in");
}
