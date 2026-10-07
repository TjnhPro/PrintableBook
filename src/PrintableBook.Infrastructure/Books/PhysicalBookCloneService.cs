using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Books;
using PrintableBook.Core.Application.Brands;
using PrintableBook.Core.Application.Discovery;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Core.Domain.Books;
using PrintableBook.Core.Domain.Processing;

namespace PrintableBook.Infrastructure.Books;

public sealed class PhysicalBookCloneService(
    IBookWorkspaceFactory workspaceFactory,
    IBookWorkspaceStateStore stateStore,
    IInteriorShuffleStore shuffleStore) : IBookCloneService
{
    private static readonly string[] ExcludedRootDirectories = [".workspace", "Output"];
    private readonly Func<string, string, CancellationToken, ValueTask> copyFile = CopyFileAsync;

    internal PhysicalBookCloneService(
        IBookWorkspaceFactory workspaceFactory,
        IBookWorkspaceStateStore stateStore,
        IInteriorShuffleStore shuffleStore,
        Func<string, string, CancellationToken, ValueTask> copyFile)
        : this(workspaceFactory, stateStore, shuffleStore)
    {
        this.copyFile = copyFile;
    }

    public async ValueTask<BookCloneResult> CloneAsync(
        ApplicationPaths paths,
        DiscoveredBook sourceBook,
        SupportedLanguageOption language,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(sourceBook);
        ArgumentNullException.ThrowIfNull(language);

        if (!SupportedLanguageCatalog.TryGet(language.Code, out var canonicalLanguage))
        {
            throw new BookCloneException("book_clone_language_invalid", "The Book clone language is not supported.");
        }

        var sourcePath = Path.GetFullPath(sourceBook.Directory.Value);
        if (!Directory.Exists(sourcePath))
        {
            throw new BookCloneException("book_clone_source_not_found", "The source Book no longer exists.");
        }

        var destinationName = LanguageEditionNamingPolicy.CreateDestinationName(sourceBook.Name, canonicalLanguage);
        var destinationBookId = new BookId(destinationName);
        var sourcesPath = Path.GetFullPath(paths.SourcesDirectory.Value);
        var destinationPath = Path.Combine(sourcesPath, destinationName);
        if (DestinationExists(sourcesPath, destinationName))
        {
            throw new BookCloneException("book_clone_destination_exists", "The destination Book already exists.");
        }

        BookProcessingState sourceState;
        InteriorShuffleMap? sourceShuffle;
        try
        {
            sourceState = await stateStore.LoadAsync(sourceBook.Workspace, cancellationToken)
                ?? BookProcessingState.NotStarted(sourceBook.Id);
            sourceShuffle = await shuffleStore.LoadAsync(sourceBook.Workspace, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or NotSupportedException)
        {
            throw new BookCloneException("book_clone_state_invalid", "The source Book state is invalid.", exception);
        }

        BookProcessingState cloneState;
        InteriorShuffleMap? cloneShuffle;
        try
        {
            cloneState = CreateCloneState(sourceState, destinationBookId, canonicalLanguage, sourcePath, destinationPath);
            cloneShuffle = RemapShuffle(sourceShuffle, sourcePath, destinationPath);
        }
        catch (BookCloneException)
        {
            throw;
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or NotSupportedException)
        {
            throw new BookCloneException("book_clone_state_invalid", "The source Book state contains an invalid reference.", exception);
        }

        var applicationRoot = Path.GetFullPath(paths.Root.Value);
        var stagingPath = Path.Combine(applicationRoot, $".book-clone-{Guid.NewGuid():N}");
        var published = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(stagingPath);
            await CopyDirectoryAsync(sourcePath, stagingPath, isRoot: true, cancellationToken);

            var stagingWorkspace = await workspaceFactory.CreateAsync(
                destinationBookId,
                new DirectoryReference(stagingPath),
                cancellationToken);
            await stateStore.SaveAsync(stagingWorkspace, cloneState, cancellationToken);
            if (cloneShuffle is not null)
            {
                await shuffleStore.SaveAsync(stagingWorkspace, cloneShuffle, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (DestinationExists(sourcesPath, destinationName))
            {
                throw new BookCloneException("book_clone_destination_exists", "The destination Book already exists.");
            }

            try
            {
                Directory.Move(stagingPath, destinationPath);
                published = true;
            }
            catch (IOException exception) when (DestinationExists(sourcesPath, destinationName))
            {
                throw new BookCloneException("book_clone_destination_exists", "The destination Book already exists.", exception);
            }

            var destinationWorkspace = RemapWorkspace(stagingWorkspace, stagingPath, destinationPath);
            return new BookCloneResult(
                sourceBook,
                canonicalLanguage,
                new DiscoveredBook(
                    destinationName,
                    destinationBookId,
                    new DirectoryReference(destinationPath),
                    destinationWorkspace));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (BookCloneException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or InvalidDataException)
        {
            throw new BookCloneException("book_clone_failed", "The Book could not be cloned.", exception);
        }
        finally
        {
            if (!published && Directory.Exists(stagingPath))
            {
                try { Directory.Delete(stagingPath, recursive: true); }
                catch { /* Staging is outside sources, so discovery cannot publish a partial Book. */ }
            }
        }
    }

    private static BookProcessingState CreateCloneState(
        BookProcessingState source,
        BookId destinationBookId,
        SupportedLanguageOption language,
        string sourceRoot,
        string destinationRoot)
    {
        var selectedCover = string.IsNullOrWhiteSpace(source.SelectedCoverReference)
            ? null
            : RemapReference(source.SelectedCoverReference, sourceRoot, destinationRoot);
        var frameOverrides = source.InteriorFrameOverrides is null
            ? null
            : source.InteriorFrameOverrides.ToDictionary(
                entry => ValidateRelativeSourceKey(entry.Key, sourceRoot),
                entry => entry.Value,
                StringComparer.OrdinalIgnoreCase);
        var inactiveKeys = RemapRelativeSourceKeys(source.InactiveInteriorSourceKeys, sourceRoot);
        var introKeys = RemapRelativeSourceKeys(source.SelectedIntroInteriorSourceKeys, sourceRoot);

        return BookProcessingState.NotStarted(destinationBookId) with
        {
            SelectedCoverReference = selectedCover,
            InteriorFrameOverrides = frameOverrides,
            HasBackground = source.HasBackground,
            InactiveInteriorSourceKeys = inactiveKeys,
            HasIntro = source.HasIntro,
            SelectedIntroInteriorSourceKeys = introKeys,
            Metadata = source.Metadata,
            AssignedBrand = null,
            KeywordBuilder = null,
            LanguageCode = language.Code,
            IsCompleted = false
        };
    }

    private static IReadOnlyList<string>? RemapRelativeSourceKeys(
        IReadOnlyList<string>? keys,
        string sourceRoot)
    {
        if (keys is null) return null;
        return keys.Select(key => ValidateRelativeSourceKey(key, sourceRoot)).ToArray();
    }

    private static string ValidateRelativeSourceKey(string key, string sourceRoot)
    {
        var normalized = InteriorSourceKey.Normalize(key);
        var source = ResolveContainedSource(normalized, sourceRoot);
        if (!File.Exists(source))
        {
            throw new BookCloneException("book_clone_state_invalid", $"Source reference '{key}' no longer exists.");
        }

        return normalized;
    }

    private static InteriorShuffleMap? RemapShuffle(
        InteriorShuffleMap? source,
        string sourceRoot,
        string destinationRoot)
    {
        if (source is null) return null;
        if (source.Entries is null ||
            source.Entries.Select(entry => entry.OutputIndex).Distinct().Count() != source.Entries.Count ||
            !source.Entries.Select(entry => entry.OutputIndex).Order().SequenceEqual(Enumerable.Range(1, source.Entries.Count)))
        {
            throw new BookCloneException("book_clone_state_invalid", "The source Book shuffle order is invalid.");
        }

        var entries = source.Entries
            .Select(entry => new InteriorShuffleEntry(
                new FileReference(RemapReference(entry.Page?.Value, sourceRoot, destinationRoot)),
                entry.OutputIndex))
            .ToArray();
        if (entries.Select(entry => entry.Page.Value).Distinct(StringComparer.OrdinalIgnoreCase).Count() != entries.Length)
        {
            throw new BookCloneException("book_clone_state_invalid", "The source Book shuffle contains duplicate pages.");
        }

        return new InteriorShuffleMap(entries, source.Seed);
    }

    private static string RemapReference(string? reference, string sourceRoot, string destinationRoot)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            throw new BookCloneException("book_clone_state_invalid", "A source reference is missing.");
        }

        if (!Path.IsPathRooted(reference))
        {
            var normalized = InteriorSourceKey.Normalize(reference);
            _ = ResolveContainedSource(normalized, sourceRoot);
            return normalized;
        }

        var source = Path.GetFullPath(reference);
        var relative = GetContainedRelativePath(sourceRoot, source);
        EnsureCopiedSource(relative, source);
        return Path.GetFullPath(Path.Combine(destinationRoot, relative));
    }

    private static string ResolveContainedSource(string relative, string sourceRoot)
    {
        var source = Path.GetFullPath(Path.Combine(sourceRoot, relative));
        var containedRelative = GetContainedRelativePath(sourceRoot, source);
        EnsureCopiedSource(containedRelative, source);
        return source;
    }

    private static string GetContainedRelativePath(string root, string candidate)
    {
        var relative = Path.GetRelativePath(root, candidate);
        if (Path.IsPathRooted(relative) ||
            relative.Equals("..", StringComparison.Ordinal) ||
            relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new BookCloneException("book_clone_state_invalid", "A source reference escapes the Book directory.");
        }

        return relative;
    }

    private static void EnsureCopiedSource(string relative, string source)
    {
        var firstSegment = relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (firstSegment is null || ExcludedRootDirectories.Contains(firstSegment, StringComparer.OrdinalIgnoreCase) || !File.Exists(source))
        {
            throw new BookCloneException("book_clone_state_invalid", "A source reference does not identify copied Book content.");
        }
    }

    private async ValueTask CopyDirectoryAsync(
        string sourcePath,
        string destinationPath,
        bool isRoot,
        CancellationToken cancellationToken)
    {
        RejectReparsePoint(sourcePath);
        foreach (var directory in Directory.EnumerateDirectories(sourcePath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetFileName(directory);
            if (isRoot && ExcludedRootDirectories.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
            RejectReparsePoint(directory);
            var childDestination = Path.Combine(destinationPath, name);
            Directory.CreateDirectory(childDestination);
            await CopyDirectoryAsync(directory, childDestination, isRoot: false, cancellationToken);
        }

        foreach (var file in Directory.EnumerateFiles(sourcePath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            RejectReparsePoint(file);
            await copyFile(file, Path.Combine(destinationPath, Path.GetFileName(file)), cancellationToken);
        }
    }

    private static BookWorkspace RemapWorkspace(BookWorkspace workspace, string sourceRoot, string destinationRoot) => new(
        workspace.BookId,
        RemapDirectory(workspace.WorkingDirectory, sourceRoot, destinationRoot),
        RemapDirectory(workspace.ProcessedDirectory, sourceRoot, destinationRoot),
        RemapDirectory(workspace.TemporaryOutputDirectory, sourceRoot, destinationRoot));

    private static DirectoryReference RemapDirectory(DirectoryReference directory, string sourceRoot, string destinationRoot)
    {
        var relative = GetContainedRelativePath(sourceRoot, Path.GetFullPath(directory.Value));
        return new DirectoryReference(Path.GetFullPath(Path.Combine(destinationRoot, relative)));
    }

    private static bool DestinationExists(string sourcesPath, string destinationName)
    {
        if (!Directory.Exists(sourcesPath)) return false;
        return Directory.EnumerateDirectories(sourcesPath)
            .Select(Path.GetFileName)
            .Any(name => string.Equals(name, destinationName, StringComparison.OrdinalIgnoreCase));
    }

    private static void RejectReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new BookCloneException("book_clone_failed", "The Book contains an unsupported linked entry.");
        }
    }

    private static async ValueTask CopyFileAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        await using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var destination = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await source.CopyToAsync(destination, cancellationToken);
    }
}
