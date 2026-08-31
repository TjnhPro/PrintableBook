namespace PrintableBook.Core.Application.Brands;

public enum BrandValidationStatus
{
    NotValidated,
    Validated,
    NeedsValidation
}

public sealed record BrandValidationRecord(
    DateTimeOffset DefinitionChangedAtUtc,
    string Fingerprint,
    DateTimeOffset ValidatedAtUtc,
    bool RequiresValidation);

public sealed record BrandValidationState(
    BrandValidationStatus Status,
    DateTimeOffset? ValidatedAtUtc = null,
    string? Fingerprint = null,
    string? ReasonCode = null);

public sealed record BrandValidationFailure(
    string Target,
    string Rule,
    string Code,
    string Message);

public sealed record BrandValidationResult(
    BrandValidationState State,
    IReadOnlyList<BrandValidationFailure> Failures)
{
    public bool IsSuccess => State.Status == BrandValidationStatus.Validated
        && Failures.Count == 0;
}
