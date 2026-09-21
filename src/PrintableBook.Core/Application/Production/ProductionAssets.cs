using PrintableBook.Core.Abstractions;

namespace PrintableBook.Core.Application.Production;

public enum ProductionAssetKind
{
    FinalCover = 0,
    InteriorCover = 1,
    BookOwner = 2
}

public sealed record ProductionAssetDefinition(
    ProductionAssetKind Kind,
    string FileName,
    ImageSize? RequiredSize,
    string StablePageId,
    string CacheDirectoryName,
    string? ProcessedFileName);

public static class ProductionAssets
{
    public static readonly IReadOnlyList<ProductionAssetDefinition> All =
    [
        new(ProductionAssetKind.FinalCover, "final_cover.png", new ImageSize(5242, 2626), "production-final-cover", "production-final-cover", null),
        new(ProductionAssetKind.InteriorCover, "interior_cover.png", null, "production-interior-cover", "production-interior-cover", "interior-cover.png"),
        new(ProductionAssetKind.BookOwner, "interior_book_owner.png", null, "production-book-owner", "production-book-owner", "interior-book-owner.png")
    ];

    public static ProductionAssetDefinition Get(ProductionAssetKind kind) =>
        All.SingleOrDefault(asset => asset.Kind == kind)
        ?? throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported Production asset kind.");
}

public static class ProductionWorkspacePaths
{
    public static DirectoryReference GeneratorDirectory(BookWorkspace workspace) =>
        ChildDirectory(workspace, "generator");

    public static DirectoryReference TemplatesDirectory(BookWorkspace workspace) =>
        ChildDirectory(workspace, "templates");

    public static DirectoryReference ProductionDirectory(BookWorkspace workspace) =>
        ChildDirectory(workspace, "production");

    public static DirectoryReference ProcessedProductionDirectory(BookWorkspace workspace) =>
        new(Path.Combine(workspace.ProcessedDirectory.Value, "production"));

    public static DirectoryReference CacheDirectory(BookWorkspace workspace, ProductionAssetKind kind) =>
        new(Path.Combine(workspace.WorkingDirectory.Value, "cache", ProductionAssets.Get(kind).CacheDirectoryName));

    public static FileReference SourceFile(BookWorkspace workspace, ProductionAssetKind kind) =>
        new(Path.Combine(ProductionDirectory(workspace).Value, ProductionAssets.Get(kind).FileName));

    public static FileReference ProcessedFile(BookWorkspace workspace, ProductionAssetKind kind)
    {
        var fileName = ProductionAssets.Get(kind).ProcessedFileName;
        return fileName is null
            ? throw new ArgumentException("This Production asset does not have a processed page.", nameof(kind))
            : new FileReference(Path.Combine(ProcessedProductionDirectory(workspace).Value, fileName));
    }

    public static FileReference StateFile(BookWorkspace workspace) =>
        new(Path.Combine(workspace.WorkingDirectory.Value, "state", "production.json"));

    private static DirectoryReference ChildDirectory(BookWorkspace workspace, string name) =>
        new(Path.Combine(workspace.WorkingDirectory.Value, name));
}
