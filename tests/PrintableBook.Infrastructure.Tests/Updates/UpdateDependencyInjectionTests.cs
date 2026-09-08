using Microsoft.Extensions.DependencyInjection;
using PrintableBook.Core.Application.Updates;
using PrintableBook.Infrastructure.DependencyInjection;
using PrintableBook.Infrastructure.Updates;

namespace PrintableBook.Infrastructure.Tests.Updates;

public sealed class UpdateDependencyInjectionTests
{
    [Fact]
    public void AddPrintableBookInfrastructureRegistersUpdateServices()
    {
        var services = new ServiceCollection();

        services.AddPrintableBookInfrastructure();

        using var provider = services.BuildServiceProvider();
        var feed = provider.GetRequiredService<IUpdateFeed>();
        var preparation = provider.GetRequiredService<IUpdatePreparationService>();
        var factory = provider.GetRequiredService<IHttpClientFactory>();
        using var downloadClient = factory.CreateClient(HttpUpdateAssetDownloader.HttpClientName);

        Assert.IsType<GitHubReleaseUpdateFeed>(feed);
        Assert.IsType<UpdatePreparationService>(preparation);
        Assert.Equal(TimeSpan.FromMinutes(5), downloadClient.Timeout);
    }
}
