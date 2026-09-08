namespace PrintableBook.Core.Application.Updates;

public sealed record UpdatePreparationProgress(
    UpdatePreparationStage Stage,
    long BytesReceived = 0,
    long? TotalBytes = null);
