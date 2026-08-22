using System.Text.Json;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Processing;

namespace PrintableBook.Infrastructure.Workspaces;

public sealed class JsonInteriorShuffleStore(IFileSystem fileSystem) : IInteriorShuffleStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async ValueTask<InteriorShuffleMap?> LoadAsync(BookWorkspace workspace, CancellationToken cancellationToken = default)
    {
        var shuffleFile = ShuffleFile(workspace);
        if (!await fileSystem.FileExistsAsync(shuffleFile, cancellationToken))
        {
            return null;
        }

        var content = await fileSystem.ReadTextAsync(shuffleFile, cancellationToken);
        return JsonSerializer.Deserialize<InteriorShuffleMap>(content, JsonOptions)
            ?? throw new InvalidDataException("The interior shuffle file is empty or invalid.");
    }

    public ValueTask SaveAsync(BookWorkspace workspace, InteriorShuffleMap shuffleMap, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shuffleMap);
        return fileSystem.WriteTextAtomicallyAsync(
            ShuffleFile(workspace),
            JsonSerializer.Serialize(shuffleMap, JsonOptions),
            cancellationToken);
    }

    private static FileReference ShuffleFile(BookWorkspace workspace) =>
        new(Path.Combine(workspace.WorkingDirectory.Value, "state", "interior-shuffle.json"));
}
