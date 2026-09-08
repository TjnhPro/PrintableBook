using Microsoft.Extensions.DependencyInjection;
using PrintableBook.Core.Application.Updates;
using PrintableBook.Infrastructure.DependencyInjection;
using PrintableBook.Infrastructure.Updates;

namespace PrintableBook.Infrastructure.Tests.Updates;

public sealed class UpdateDependencyInjectionTests
{
    [Fact]
    public void AddPrintableBookInfrastructureRegistersGitHubUpdateFeed()
    {
        var services = new ServiceCollection();

        services.AddPrintableBookInfrastructure();

        using var provider = services.BuildServiceProvider();
        var feed = provider.GetRequiredService<IUpdateFeed>();

        Assert.IsType<GitHubReleaseUpdateFeed>(feed);
    }
}
