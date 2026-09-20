namespace HDREZKA.Core.Api;

public enum RezkaError
{
    Network,
    MirrorBanned,
    LoginRequired,
    AccessDenied,
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
}
