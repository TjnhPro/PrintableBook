using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Brands;
using PrintableBook.Core.Application.Desktop;

namespace PrintableBook.Core.Tests.Application.Brands;

public sealed class BrandValidationServiceTests
{
    private static readonly DirectoryReference BrandDirectory = new("C:\\brands\\demo");

    [Fact]
    public async Task CheckState_without_a_record_returns_not_validated_without_metadata_or_image_inspection()
    {
        var files = FileSystem.ValidBrand();
        var images = new Images();
        var service = CreateService(new StateStore(), files, images);

        var state = await service.CheckStateAsync(BrandDirectory, GlobalSettings.Default);

        Assert.Equal(BrandValidationStatus.NotValidated, state.Status);
        Assert.Equal(0, files.MetadataReads);
        Assert.Equal(0, images.SizeReads);
    }

    [Fact]
    public async Task Validate_then_check_state_certifies_a_brand_without_a_second_image_inspection()
    {
        var store = new StateStore();
        var files = FileSystem.ValidBrand();
        var images = new Images();
        var service = CreateService(store, files, images);

        var validated = await service.ValidateAsync(BrandDirectory, GlobalSettings.Default);
        var imageReadsAfterValidate = images.SizeReads;
        var checkedState = await service.CheckStateAsync(BrandDirectory, GlobalSettings.Default);

        Assert.True(validated.IsSuccess, string.Join("; ", validated.Failures.Select(failure => $"{failure.Target}:{failure.Code}")));
        Assert.Equal(BrandValidationStatus.Validated, checkedState.Status);
        Assert.NotNull(store.Record);
        Assert.Equal(imageReadsAfterValidate, images.SizeReads);
        Assert.True(files.MetadataReads > 0);
        Assert.Equal(0, files.ContentReads);
    }

    [Fact]
    public async Task CheckState_detects_tracked_metadata_or_relevant_settings_changes_without_opening_images()
    {
        var store = new StateStore();
        var files = FileSystem.ValidBrand();
        var images = new Images();
        var service = CreateService(store, files, images);
        await service.ValidateAsync(BrandDirectory, GlobalSettings.Default);
        var imageReadsAfterValidate = images.SizeReads;

        files.Touch("frame.png", 99);
        var changedFile = await service.CheckStateAsync(BrandDirectory, GlobalSettings.Default);
        var changedSettings = await service.CheckStateAsync(BrandDirectory, GlobalSettings.Default with { ArtworkMaximumSide = 2048 });

        Assert.Equal(BrandValidationStatus.NeedsValidation, changedFile.Status);
        Assert.Equal("brand_fingerprint_changed", changedFile.ReasonCode);
        Assert.Equal(BrandValidationStatus.NeedsValidation, changedSettings.Status);
        Assert.Equal(imageReadsAfterValidate, images.SizeReads);
    }

    [Fact]
    public async Task Failed_revalidation_collects_all_failures_and_marks_the_previous_record_stale()
    {
        var store = new StateStore();
        var files = FileSystem.ValidBrand();
        var service = CreateService(store, files, new Images());
        await service.ValidateAsync(BrandDirectory, GlobalSettings.Default);
        var previous = store.Record!;

        files.Remove("IntroTemplate/intro.png");
        files.Remove("frame.png");
        files.Remove("background.png");
        var result = await service.ValidateAsync(BrandDirectory, GlobalSettings.Default);

        Assert.False(result.IsSuccess);
        Assert.Equal(BrandValidationStatus.NeedsValidation, result.State.Status);
        Assert.Equal(3, result.Failures.Count);
        Assert.True(store.Record!.RequiresValidation);
        Assert.Equal(previous.Fingerprint, store.Record.Fingerprint);
        Assert.Equal(previous.ValidatedAtUtc, store.Record.ValidatedAtUtc);
    }

    [Fact]
    public async Task Explicitly_invalid_or_old_records_exit_before_metadata_scanning()
    {
        var files = FileSystem.ValidBrand();
        var definition = BrandValidationDefinition.CreateCurrent(GlobalSettings.Default);
        var stale = new StateStore
        {
            Record = new BrandValidationRecord(definition.DefinitionChangedAtUtc.AddSeconds(-1), "sha256:old", DateTimeOffset.UtcNow, false)
        };
        var service = CreateService(stale, files, new Images());

        var state = await service.CheckStateAsync(BrandDirectory, GlobalSettings.Default);

        Assert.Equal(BrandValidationStatus.NeedsValidation, state.Status);
        Assert.Equal("brand_definition_changed", state.ReasonCode);
        Assert.Equal(0, files.MetadataReads);
    }

    private static BrandValidationService CreateService(StateStore store, FileSystem files, Images images)
    {
        var resolver = new BrandValidationTargetResolver(files);
        return new BrandValidationService(store, resolver, new BrandFingerprintCalculator(files, resolver), files, images);
    }

    private sealed class StateStore : IBrandValidationStateStore
    {
        public BrandValidationRecord? Record { get; set; }
        public ValueTask<BrandValidationRecord?> LoadAsync(DirectoryReference brandDirectory, CancellationToken cancellationToken = default) => ValueTask.FromResult(Record);
        public ValueTask SaveAsync(DirectoryReference brandDirectory, BrandValidationRecord record, CancellationToken cancellationToken = default)
        {
            Record = record;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class Images : IImageInspector
    {
        public int SizeReads { get; private set; }
        public ValueTask<ImageSize> GetSizeAsync(FileReference image, CancellationToken cancellationToken = default)
        {
            SizeReads++;
            return ValueTask.FromResult(image.Value.EndsWith("frame.png", StringComparison.OrdinalIgnoreCase)
                ? new ImageSize(GlobalSettings.Default.ArtworkMaximumSide, GlobalSettings.Default.ArtworkMaximumSide)
                : image.Value.EndsWith("background.png", StringComparison.OrdinalIgnoreCase)
                    ? new ImageSize(GlobalSettings.Default.FinalPageWidth, GlobalSettings.Default.FinalPageHeight)
                    : new ImageSize(1024, 1024));
        }
        public ValueTask<ImageInfo> GetInfoAsync(FileReference image, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FileSystem : IFileSystem
    {
        private readonly Dictionary<string, FileMetadata> metadata = new(StringComparer.OrdinalIgnoreCase);
        public int MetadataReads { get; private set; }
        public int ContentReads { get; private set; }

        public static FileSystem ValidBrand()
        {
            var result = new FileSystem();
            result.Add("IntroTemplate/intro.png", 10);
            result.Add("frame.png", 20);
            result.Add("background.png", 30);
            return result;
        }

        public void Touch(string relativePath, long length) => metadata[Full(relativePath)] = new FileMetadata(length, DateTimeOffset.UtcNow.AddTicks(length));
        public void Remove(string relativePath) => metadata.Remove(Full(relativePath));
        public ValueTask<bool> FileExistsAsync(FileReference file, CancellationToken cancellationToken = default) => ValueTask.FromResult(metadata.ContainsKey(file.Value));
        public ValueTask<FileMetadata?> GetFileMetadataAsync(FileReference file, CancellationToken cancellationToken = default)
        {
            MetadataReads++;
            return ValueTask.FromResult(metadata.TryGetValue(file.Value, out var value) ? (FileMetadata?)value : null);
        }
        public ValueTask<bool> DirectoryExistsAsync(DirectoryReference directory, CancellationToken cancellationToken = default) => ValueTask.FromResult(metadata.Keys.Any(path => Normalize(path).StartsWith(Normalize(directory.Value) + "/", StringComparison.OrdinalIgnoreCase)));
        public ValueTask CreateDirectoryAsync(DirectoryReference directory, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public async IAsyncEnumerable<DirectoryReference> EnumerateDirectoriesAsync(DirectoryReference directory, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var prefix = Normalize(directory.Value) + "/";
            foreach (var child in metadata.Keys.Select(Path.GetDirectoryName).Where(path => path is not null && Normalize(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).Select(path => prefix + Normalize(path!)[prefix.Length..].Split('/')[0]).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                yield return new DirectoryReference(child);
                await Task.Yield();
            }
        }
        public async IAsyncEnumerable<FileReference> EnumerateFilesAsync(DirectoryReference directory, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var path in metadata.Keys.Where(path => string.Equals(Normalize(Path.GetDirectoryName(path)!), Normalize(directory.Value), StringComparison.OrdinalIgnoreCase)))
            {
                yield return new FileReference(path);
                await Task.Yield();
            }
        }
        public ValueTask<string> ReadTextAsync(FileReference file, CancellationToken cancellationToken = default)
        {
            ContentReads++;
            throw new InvalidOperationException("Fast validation must not read asset content.");
        }
        public ValueTask WriteTextAtomicallyAsync(FileReference file, string content, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask CopyFileAsync(FileReference source, FileReference destination, bool overwrite, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask MoveFileAsync(FileReference source, FileReference destination, bool overwrite, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask DeleteFileAsync(FileReference file, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask DeleteDirectoryAsync(DirectoryReference directory, bool recursive, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        private void Add(string relativePath, long length) => metadata.Add(Full(relativePath), new FileMetadata(length, new DateTimeOffset(2026, 8, 31, 4, 32, 0, TimeSpan.Zero)));
        private static string Full(string relativePath) => Path.Combine(BrandDirectory.Value, relativePath);
        private static string Normalize(string path) => path.Replace('\\', '/').TrimEnd('/');
    }
}
