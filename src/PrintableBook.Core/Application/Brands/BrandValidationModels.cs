using System.Text.Json.Serialization;
using PrintableBook.Core.Abstractions;

namespace PrintableBook.Core.Application.Brands;

public enum BrandValidationStatus
{
    NotValidated,
    Validated,
    NeedsValidation
}

[method: JsonConstructor]
public sealed record BrandValidationAssetFact(
    string RelativePath,
    int Width,
    int Height)
{
    public BrandValidationAssetFact(string relativePath, ImageSize size)
        : this(relativePath, size.Width, size.Height)
    {
    }

    [JsonIgnore]
    public ImageSize Size => new(Width, Height);
}

public sealed record BrandValidationRecord(
    int SchemaVersion,
    int AssetFingerprintFormatVersion,
    DateTimeOffset DefinitionChangedAtUtc,
    string? DefinitionSignature,
    string Fingerprint,
    DateTimeOffset ValidatedAtUtc,
    bool RequiresValidation,
    IReadOnlyList<BrandValidationAssetFact>? Assets)
{
    public const int CurrentSchemaVersion = 2;
}

public sealed record BrandValidationState(
    BrandValidationStatus Status,
    DateTimeOffset? ValidatedAtUtc = null,
    string? Fingerprint = null,
    string? ReasonCode = null,
    IReadOnlyList<BrandValidationAssetFact>? ValidatedAssets = null);

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
