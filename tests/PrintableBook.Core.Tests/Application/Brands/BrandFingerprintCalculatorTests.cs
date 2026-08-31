using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Brands;
using PrintableBook.Core.Application.Desktop;

namespace PrintableBook.Core.Tests.Application.Brands;

public sealed class BrandFingerprintCalculatorTests
{
    private static readonly DirectoryReference BrandDirectory = new("C:\\brands\\demo");

    [Fact]
    public async Task Resolver_recursively_tracks_supported_intro_images_and_ignores_other_files()
    {
        var files = new MetadataFileSystem(
            ("C:\\brands\\demo\\IntroTemplate\\one.PNG", 1),
            ("C:\\brands\\demo\\IntroTemplate\\nested\\two.jpeg", 2),
            ("C:\\brands\\demo\\IntroTemplate\\skip.txt", 3));
        var resolver = new BrandValidationTargetResolver(files);

        var intro = (await resolver.ResolveAsync(BrandDirectory, BrandValidationDefinition.CreateCurrent(GlobalSettings.Default)))
            .Single(entry => entry.Entry.Key == "intro");

        Assert.Equal(["introtemplate/nested/two.jpeg", "introtemplate/one.png"], intro.Files.Select(file => BrandValidationTargetResolver.NormalizeRelativePath(BrandDirectory, file)));
    }

    [Fact]
    public async Task Same_metadata_and_context_produces_same_fingerprint_regardless_of_enumeration_order()
    {
        var first = new MetadataFileSystem(
            ("C:\\brands\\demo\\frame.png", 10),
            ("C:\\brands\\demo\\background.png", 11),
            ("C:\\brands\\demo\\IntroTemplate\\a.png", 12));
        var second = new MetadataFileSystem(
            ("C:\\brands\\demo\\IntroTemplate\\a.png", 12),
            ("C:\\brands\\demo\\background.png", 11),
            ("C:\\brands\\demo\\frame.png", 10));

        Assert.Equal(await CalculateAsync(first), await CalculateAsync(second));
    }

    [Fact]
    public async Task Fingerprint_changes_for_tracked_metadata_and_relevant_dimension_context_but_not_untracked_files()
    {
        var baseline = new MetadataFileSystem(
            ("C:\\brands\\demo\\frame.png", 10),
            ("C:\\brands\\demo\\background.png", 11),
            ("C:\\brands\\demo\\IntroTemplate\\a.png", 12));
        var changed = new MetadataFileSystem(
            ("C:\\brands\\demo\\frame.png", 99),
            ("C:\\brands\\demo\\background.png", 11),
            ("C:\\brands\\demo\\IntroTemplate\\a.png", 12),
            ("C:\\brands\\demo\\AppPlus\\ignored.png", 500));
        var excludedOnly = new MetadataFileSystem(
            ("C:\\brands\\demo\\frame.png", 10),
            ("C:\\brands\\demo\\background.png", 11),
            ("C:\\brands\\demo\\IntroTemplate\\a.png", 12),
            ("C:\\brands\\demo\\AppPlus\\ignored.png", 500),
            ("C:\\brands\\demo\\BackCover.psd", 501),
            ("C:\\brands\\demo\\brand.json", 502),
            ("C:\\brands\\demo\\brand.validation.json", 503),
            ("C:\\brands\\demo\\untracked.png", 504));

        Assert.NotEqual(await CalculateAsync(baseline), await CalculateAsync(changed));
        Assert.Equal(await CalculateAsync(baseline), await CalculateAsync(excludedOnly));
        Assert.NotEqual(await CalculateAsync(baseline), await CalculateAsync(baseline, GlobalSettings.Default with { ArtworkMaximumSide = 2000 }));
    }

    private static ValueTask<string> CalculateAsync(MetadataFileSystem files, GlobalSettings? settings = null)
    {
        var resolver = new BrandValidationTargetResolver(files);
        return new BrandFingerprintCalculator(files, resolver).CalculateAsync(BrandDirectory, BrandValidationDefinition.CreateCurrent(settings ?? GlobalSettings.Default));
    }

    private sealed class MetadataFileSystem(params (string Path, long Length)[] seeded) : IFileSystem
    {
        private readonly Dictionary<string, FileMetadata> metadata = seeded.ToDictionary(
            value => value.Path,
            value => new FileMetadata(value.Length, new DateTimeOffset(2026, 8, 31, 4, 32, 0, TimeSpan.Zero)),
            StringComparer.OrdinalIgnoreCase);

        public ValueTask<bool> FileExistsAsync(FileReference file, CancellationToken cancellationToken = default) => ValueTask.FromResult(metadata.ContainsKey(file.Value));
        public ValueTask<FileMetadata?> GetFileMetadataAsync(FileReference file, CancellationToken cancellationToken = default) => ValueTask.FromResult(metadata.TryGetValue(file.Value, out var value) ? (FileMetadata?)value : null);
        public ValueTask<bool> DirectoryExistsAsync(DirectoryReference directory, CancellationToken cancellationToken = default) => ValueTask.FromResult(metadata.Keys.Any(path => path.StartsWith(directory.Value + "\\", StringComparison.OrdinalIgnoreCase)));
        public ValueTask CreateDirectoryAsync(DirectoryReference directory, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public async IAsyncEnumerable<DirectoryReference> EnumerateDirectoriesAsync(DirectoryReference directory, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var prefix = directory.Value + "\\";
            foreach (var child in metadata.Keys
                         .Select(Path.GetDirectoryName)
                         .Where(path => path is not null && path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                         .Select(path => prefix + path![prefix.Length..].Split('\\')[0])
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                yield return new DirectoryReference(child);
                await Task.Yield();
            }
        }
        public async IAsyncEnumerable<FileReference> EnumerateFilesAsync(DirectoryReference directory, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var path in metadata.Keys.Where(path => string.Equals(Path.GetDirectoryName(path), directory.Value, StringComparison.OrdinalIgnoreCase)))
            {
                yield return new FileReference(path);
                await Task.Yield();
            }
        }
        public ValueTask<string> ReadTextAsync(FileReference file, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Fingerprint must not read file content.");
        public ValueTask WriteTextAtomicallyAsync(FileReference file, string content, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask CopyFileAsync(FileReference source, FileReference destination, bool overwrite, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask MoveFileAsync(FileReference source, FileReference destination, bool overwrite, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask DeleteFileAsync(FileReference file, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask DeleteDirectoryAsync(DirectoryReference directory, bool recursive, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
