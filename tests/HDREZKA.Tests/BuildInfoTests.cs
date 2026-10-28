using HDREZKA.Core.Api;
using Xunit;

namespace HDREZKA.Tests;

public class BuildInfoTests
{
    [Fact]
    public void Version_ComesFromBuildProps_AndLooksLikeSemver()
    {
        Assert.False(string.IsNullOrWhiteSpace(BuildInfo.Version));
        Assert.Matches(@"^\d+\.\d+\.\d+", BuildInfo.Version);
    }

    [Fact]
    public void UserAgent_ContainsVersion()
    {
        Assert.Contains(BuildInfo.Version, BuildInfo.UserAgent, StringComparison.Ordinal);
    }
}
