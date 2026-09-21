using System.Text.Json;
using System.Text.Json.Serialization;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Production;

namespace PrintableBook.Infrastructure.Workspaces;

public sealed class JsonProductionWorkspaceStateStore(IFileSystem fileSystem) : IProductionWorkspaceStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public async ValueTask<ProductionWorkspaceState> LoadAsync(BookWorkspace workspace, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var file = ProductionWorkspacePaths.StateFile(workspace);
        if (!await fileSystem.FileExistsAsync(file, cancellationToken))
        {
            return ProductionWorkspaceState.Empty;
        }

        var content = await fileSystem.ReadTextAsync(file, cancellationToken);
        var state = JsonSerializer.Deserialize<ProductionWorkspaceState>(content, JsonOptions)
            ?? throw new InvalidDataException("The Production workspace state file is empty or invalid.");
        if (state.SchemaVersion != ProductionWorkspaceState.CurrentSchemaVersion)
        {
            return ProductionWorkspaceState.Empty;
        }

        return state with
        {
            Assets = state.Assets is null
                ? null
                : new Dictionary<string, ProductionAssetState>(state.Assets, StringComparer.OrdinalIgnoreCase),
            ProcessedPages = state.ProcessedPages is null
                ? null
                : new Dictionary<string, ProductionProcessedPageState>(state.ProcessedPages, StringComparer.OrdinalIgnoreCase)
        };
    }

    public ValueTask SaveAsync(BookWorkspace workspace, ProductionWorkspaceState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(state);
        return fileSystem.WriteTextAtomicallyAsync(
            ProductionWorkspacePaths.StateFile(workspace),
            JsonSerializer.Serialize(state with { SchemaVersion = ProductionWorkspaceState.CurrentSchemaVersion }, JsonOptions),
            cancellationToken);
    }
}
