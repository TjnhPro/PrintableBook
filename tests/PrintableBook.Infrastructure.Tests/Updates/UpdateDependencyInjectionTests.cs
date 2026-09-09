using Microsoft.Extensions.DependencyInjection;
using PrintableBook.Core.Application.Updates;
using PrintableBook.Core.DependencyInjection;
using PrintableBook.Infrastructure.DependencyInjection;
using PrintableBook.Infrastructure.Updates;

namespace PrintableBook.Infrastructure.Tests.Updates;

public sealed class UpdateDependencyInjectionTests
{
    [Fact]
    public void AddPrintableBookInfrastructureRegistersUpdateServices()
    {
        var services = new ServiceCollection().AddPrintableBookCore();
        services.AddSingleton<IApplicationVersionProvider>(new StubVersionProvider());

        services.AddPrintableBookInfrastructure();

        using var provider = services.BuildServiceProvider();
        var feed = provider.GetRequiredService<IUpdateFeed>();
        var preparation = provider.GetRequiredService<IUpdatePreparationService>();
        var keyProvider = provider.GetRequiredService<IUpdateManifestPublicKeyProvider>();
        var manifestClient = provider.GetRequiredService<SignedReleaseManifestClient>();
        var updateService = provider.GetRequiredService<IUpdateService>();
        var factory = provider.GetRequiredService<IHttpClientFactory>();
        using var downloadClient = factory.CreateClient(HttpUpdateAssetDownloader.HttpClientName);

        Assert.IsType<GitHubReleaseUpdateFeed>(feed);
        Assert.IsType<UpdatePreparationService>(preparation);
        Assert.IsType<ProductionUpdateManifestPublicKeyProvider>(keyProvider);
        Assert.NotNull(manifestClient);
        Assert.NotNull(updateService);
        Assert.Equal(TimeSpan.FromMinutes(5), downloadClient.Timeout);
    }

    private sealed class StubVersionProvider : IApplicationVersionProvider
    {
        public Version CurrentVersion => new(0, 1, 1);
    }
}
