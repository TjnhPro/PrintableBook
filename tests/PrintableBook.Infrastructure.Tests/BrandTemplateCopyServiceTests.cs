using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Brands;
using PrintableBook.Core.Domain.Books;
using PrintableBook.Infrastructure.FileSystem;

namespace PrintableBook.Infrastructure.Tests;

public sealed class BrandTemplateCopyServiceTests : IAsyncLifetime
{
    private readonly string rootPath = Path.Combine(Path.GetTempPath(), $"PrintableBook.BrandTemplates.{Guid.NewGuid():N}");

    [Fact]
    public async Task CopyAsync_creates_templates_directory_and_copies_exactly_the_three_required_psd_files()
    {
        var (service, brand, workspace) = await CreateScenarioAsync("cover-v1", "app-v1", "owner-v1");

        var result = await service.CopyAsync(brand, workspace);

        Assert.Equal(Path.Combine(workspace.WorkingDirectory.Value, "templates"), result.DestinationDirectory.Value);
        Assert.Equal(["cover.psd", "app_plus.psd", "book_owner.psd"], result.CopiedFileNames);
        Assert.Equal("cover-v1", await File.ReadAllTextAsync(Path.Combine(result.DestinationDirectory.Value, "cover.psd")));
        Assert.Equal("app-v1", await File.ReadAllTextAsync(Path.Combine(result.DestinationDirectory.Value, "app_plus.psd")));
        Assert.Equal("owner-v1", await File.ReadAllTextAsync(Path.Combine(result.DestinationDirectory.Value, "book_owner.psd")));
        Assert.Equal(3, Directory.GetFiles(result.DestinationDirectory.Value).Length);
    }

    [Fact]
    public async Task CopyAsync_overwrites_existing_templates_when_invoked_again()
    {
        var (service, brand, workspace) = await CreateScenarioAsync("cover-v1", "app-v1", "owner-v1");
        await service.CopyAsync(brand, workspace);
        await File.WriteAllTextAsync(Path.Combine(brand.Value, "cover.psd"), "cover-v2");
        await File.WriteAllTextAsync(Path.Combine(brand.Value, "app_plus.psd"), "app-v2");
        await File.WriteAllTextAsync(Path.Combine(brand.Value, "book_owner.psd"), "owner-v2");

        await service.CopyAsync(brand, workspace);

        Assert.Equal("cover-v2", await File.ReadAllTextAsync(Path.Combine(workspace.WorkingDirectory.Value, "templates", "cover.psd")));
        Assert.Equal("app-v2", await File.ReadAllTextAsync(Path.Combine(workspace.WorkingDirectory.Value, "templates", "app_plus.psd")));
        Assert.Equal("owner-v2", await File.ReadAllTextAsync(Path.Combine(workspace.WorkingDirectory.Value, "templates", "book_owner.psd")));
    }

    [Fact]
    public async Task CopyAsync_does_not_create_a_partial_destination_when_a_required_source_is_missing()
    {
        var (service, brand, workspace) = await CreateScenarioAsync("cover-v1", "app-v1", "owner-v1");
        File.Delete(Path.Combine(brand.Value, "book_owner.psd"));

        await Assert.ThrowsAsync<FileNotFoundException>(() => service.CopyAsync(brand, workspace).AsTask());

        Assert.False(Directory.Exists(Path.Combine(workspace.WorkingDirectory.Value, "templates")));
    }

    private async Task<(BrandTemplateCopyService Service, DirectoryReference Brand, BookWorkspace Workspace)> CreateScenarioAsync(string cover, string appPlus, string bookOwner)
    {
        var brandPath = Path.Combine(rootPath, "brand");
        var workspacePath = Path.Combine(rootPath, "book", ".workspace");
        Directory.CreateDirectory(brandPath);
        await File.WriteAllTextAsync(Path.Combine(brandPath, "cover.psd"), cover);
        await File.WriteAllTextAsync(Path.Combine(brandPath, "app_plus.psd"), appPlus);
        await File.WriteAllTextAsync(Path.Combine(brandPath, "book_owner.psd"), bookOwner);
        var workspace = new BookWorkspace(new BookId("book"), new DirectoryReference(workspacePath), new DirectoryReference(Path.Combine(workspacePath, "processed")), new DirectoryReference(Path.Combine(workspacePath, "output-temp")));
        return (new BrandTemplateCopyService(new PhysicalFileSystem()), new DirectoryReference(brandPath), workspace);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        if (Directory.Exists(rootPath)) Directory.Delete(rootPath, recursive: true);
        return Task.CompletedTask;
    }
}
