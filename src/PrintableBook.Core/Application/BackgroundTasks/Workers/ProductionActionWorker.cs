using PrintableBook.Core.Application.Desktop;
using PrintableBook.Core.Application.Discovery;
using PrintableBook.Core.Application.Production;

namespace PrintableBook.Core.Application.BackgroundTasks.Workers;

public enum ProductionActionKind
{
    BuildCoverPdf = 0,
    ProcessInteriorCover = 1,
    ProcessBookOwner = 2
}

public sealed record ProductionActionRequest(
    string BookId,
    ProductionActionKind Action);

public sealed record ProductionActionResult(
    string BookId,
    ProductionActionKind Action,
    string OutputReference,
    DateTimeOffset CompletedAtUtc);

public sealed class ProductionActionWorker(
    IApplicationSnapshotProvider snapshotProvider,
    IProductionPageProcessingService pageProcessingService,
    IProductionCoverPdfService coverPdfService) : BackgroundTaskWorker<ProductionActionRequest, ProductionActionResult>
{
    public override BackgroundTaskKind Kind => BackgroundTaskKind.ProductionAction;

    protected override async ValueTask<ProductionActionResult> ExecuteTypedAsync(
        ProductionActionRequest request,
        IBackgroundTaskContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.BookId))
        {
            throw new BackgroundTaskFailureException("book_not_found", "The selected Book is unavailable.");
        }
        if (!Enum.IsDefined(request.Action))
        {
            throw new BackgroundTaskFailureException("production_action_invalid", "The requested Production action is not supported.");
        }

        context.Report("Validating", subject: request.BookId);
        var snapshot = await snapshotProvider.GetFreshAsync(cancellationToken);
        var book = snapshot.Discovery.Books.FirstOrDefault(candidate =>
            string.Equals(candidate.Id.Value, request.BookId, StringComparison.Ordinal));
        if (book is null)
        {
            throw new BackgroundTaskFailureException("book_not_found", "The selected Book is unavailable.");
        }

        try
        {
            return request.Action switch
            {
                ProductionActionKind.BuildCoverPdf => await BuildCoverAsync(book, request, context, cancellationToken),
                ProductionActionKind.ProcessInteriorCover => await ProcessPageAsync(book, request, ProductionAssetKind.InteriorCover, "Processing Interior Cover", context, snapshot, cancellationToken),
                ProductionActionKind.ProcessBookOwner => await ProcessPageAsync(book, request, ProductionAssetKind.BookOwner, "Processing Book Owner", context, snapshot, cancellationToken),
                _ => throw new BackgroundTaskFailureException("production_action_invalid", "The requested Production action is not supported.")
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (FileNotFoundException)
        {
            throw new BackgroundTaskFailureException("production_asset_missing", "Upload the required Production asset before running this action.");
        }
        catch (InvalidDataException exception) when (request.Action == ProductionActionKind.BuildCoverPdf)
        {
            throw new BackgroundTaskFailureException("production_cover_size_invalid", exception.Message);
        }
    }

    private async ValueTask<ProductionActionResult> BuildCoverAsync(
        DiscoveredBook book,
        ProductionActionRequest request,
        IBackgroundTaskContext context,
        CancellationToken cancellationToken)
    {
        context.Report("Validating PDF", subject: book.Id.Value);
        var result = await coverPdfService.BuildAsync(
            book.Workspace,
            new(Path.Combine(book.Directory.Value, "Output")),
            cancellationToken);
        context.Report("Publishing", detail: Path.GetFileName(result.CoverPdf.Value), subject: book.Id.Value);
        return new ProductionActionResult(book.Id.Value, request.Action, result.CoverPdf.Value, result.CompletedAtUtc);
    }

    private async ValueTask<ProductionActionResult> ProcessPageAsync(
        DiscoveredBook book,
        ProductionActionRequest request,
        ProductionAssetKind assetKind,
        string step,
        IBackgroundTaskContext context,
        ApplicationSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        context.Report(step, subject: book.Id.Value);
        var result = await pageProcessingService.ProcessAsync(
            book.Workspace,
            assetKind,
            snapshot.GlobalSettings,
            cancellationToken);
        return new ProductionActionResult(book.Id.Value, request.Action, result.FinalPage.Value, result.CompletedAtUtc);
    }
}
