using PrintableBook.Core.Abstractions;

namespace PrintableBook.Core.Application.Brands;

public static class BrandTemplateFiles
{
    public const string Cover = "cover.psd";
    public const string AppPlus = "app_plus.psd";

    public static readonly IReadOnlyList<string> Required = [Cover, AppPlus];
}

public sealed record BrandTemplateCopyResult(
    DirectoryReference DestinationDirectory,
    IReadOnlyList<string> CopiedFileNames);

public interface IBrandTemplateCopyService
{
    ValueTask<BrandTemplateCopyResult> CopyAsync(
        DirectoryReference brandDirectory,
        BookWorkspace bookWorkspace,
        CancellationToken cancellationToken = default);
}

public sealed class BrandTemplateCopyService(IFileSystem fileSystem) : IBrandTemplateCopyService
{
    public async ValueTask<BrandTemplateCopyResult> CopyAsync(
        DirectoryReference brandDirectory,
        BookWorkspace bookWorkspace,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(brandDirectory);
        ArgumentNullException.ThrowIfNull(bookWorkspace);

        var sources = BrandTemplateFiles.Required
            .Select(fileName => new FileReference(Path.Combine(brandDirectory.Value, fileName)))
            .ToArray();

        foreach (var source in sources)
        {
            if (!await fileSystem.FileExistsAsync(source, cancellationToken))
            {
                throw new FileNotFoundException("A required Brand template is missing.", source.Value);
            }
        }

        var destinationDirectory = new DirectoryReference(Path.Combine(bookWorkspace.WorkingDirectory.Value, "templates"));
        await fileSystem.CreateDirectoryAsync(destinationDirectory, cancellationToken);

        foreach (var source in sources)
        {
            var destination = new FileReference(Path.Combine(destinationDirectory.Value, Path.GetFileName(source.Value)));
            await fileSystem.CopyFileAsync(source, destination, overwrite: true, cancellationToken);
        }

        return new BrandTemplateCopyResult(destinationDirectory, BrandTemplateFiles.Required);
    }
}
