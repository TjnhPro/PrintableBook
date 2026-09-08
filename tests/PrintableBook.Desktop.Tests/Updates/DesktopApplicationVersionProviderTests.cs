using System.Reflection;
using PrintableBook.Desktop;
using PrintableBook.Desktop.Updates;

namespace PrintableBook.Desktop.Tests.Updates;

public sealed class DesktopApplicationVersionProviderTests
{
    [Fact]
    public void CurrentVersionUsesDesktopAssemblyInformationalVersion()
    {
        var assembly = typeof(App).Assembly;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        Assert.False(string.IsNullOrWhiteSpace(informationalVersion));

        var provider = new DesktopApplicationVersionProvider();

        Assert.Equal(
            Version.Parse(informationalVersion!),
            provider.CurrentVersion);
    }

    [Fact]
    public void CurrentVersionIsTheExpectedThreePartStableVersionShape()
    {
        var provider = new DesktopApplicationVersionProvider();

        Assert.True(provider.CurrentVersion.Major >= 0);
        Assert.True(provider.CurrentVersion.Minor >= 0);
        Assert.True(provider.CurrentVersion.Build >= 0);
        Assert.Equal(-1, provider.CurrentVersion.Revision);
    }
}
