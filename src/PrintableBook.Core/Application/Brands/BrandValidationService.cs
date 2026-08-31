using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Desktop;

namespace PrintableBook.Core.Application.Brands;

public sealed class BrandValidationService(
    IBrandValidationStateStore stateStore,
    BrandValidationTargetResolver resolver,
    BrandFingerprintCalculator fingerprintCalculator,
    IFileSystem fileSystem,
    IImageInspector imageInspector) : IBrandValidationService
{
    public async ValueTask<BrandValidationState> CheckStateAsync(DirectoryReference brandDirectory, GlobalSettings settings, CancellationToken cancellationToken = default)
    {
        try
        {
            var record = await stateStore.LoadAsync(brandDirectory, cancellationToken);
            if (record is null) return new(BrandValidationStatus.NotValidated);
            if (record.RequiresValidation) return new(BrandValidationStatus.NeedsValidation, record.ValidatedAtUtc, record.Fingerprint, "brand_validation_required");
            var definition = BrandValidationDefinition.CreateCurrent(settings);
            if (record.DefinitionChangedAtUtc != definition.DefinitionChangedAtUtc) return new(BrandValidationStatus.NeedsValidation, record.ValidatedAtUtc, record.Fingerprint, "brand_definition_changed");
            var fingerprint = await fingerprintCalculator.CalculateAsync(brandDirectory, definition, cancellationToken);
            return string.Equals(record.Fingerprint, fingerprint, StringComparison.Ordinal)
                ? new(BrandValidationStatus.Validated, record.ValidatedAtUtc, record.Fingerprint)
                : new(BrandValidationStatus.NeedsValidation, record.ValidatedAtUtc, record.Fingerprint, "brand_fingerprint_changed");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return new(BrandValidationStatus.NeedsValidation, ReasonCode: "brand_validation_state_unavailable");
        }
    }

    public async ValueTask<BrandValidationResult> ValidateAsync(DirectoryReference brandDirectory, GlobalSettings settings, CancellationToken cancellationToken = default)
    {
        var definition = BrandValidationDefinition.CreateCurrent(settings);
        var failures = new List<BrandValidationFailure>();
        var resolved = await resolver.ResolveAsync(brandDirectory, definition, cancellationToken);
        foreach (var entry in resolved)
        {
            if (entry.Entry.Target is BrandValidationDirectoryFilesTarget directory && entry.Files.Count < directory.MinimumFileCount)
            {
                failures.Add(new(entry.Entry.Target.RelativePath, "exists", "brand_intro_empty", "IntroTemplate must contain at least one supported image."));
                continue;
            }
            foreach (var file in entry.Files)
            {
                var exists = true;
                foreach (var rule in entry.Entry.Rules)
                {
                    switch (rule)
                    {
                        case BrandFileExistsRule:
                            exists = await fileSystem.FileExistsAsync(file, cancellationToken);
                            if (!exists) failures.Add(new(BrandValidationTargetResolver.NormalizeRelativePath(brandDirectory, file), "exists", "brand_asset_missing", "Required Brand asset is missing."));
                            break;
                        case BrandImageDimensionsRule dimensionRule when exists:
                            try
                            {
                                var size = await imageInspector.GetSizeAsync(file, cancellationToken);
                                if (!dimensionRule.AllowedSizes.Contains(size)) failures.Add(new(BrandValidationTargetResolver.NormalizeRelativePath(brandDirectory, file), "dimensions", "brand_image_dimensions_invalid", "Brand image dimensions do not match the required size."));
                            }
                            catch (OperationCanceledException)
                            {
                                throw;
                            }
                            catch (Exception)
                            {
                                failures.Add(new(BrandValidationTargetResolver.NormalizeRelativePath(brandDirectory, file), "dimensions", "brand_image_unreadable", "Brand image could not be read."));
                            }
                            break;
                    }
                }
            }
        }
        if (failures.Count > 0)
        {
            var previous = await stateStore.LoadAsync(brandDirectory, cancellationToken);
            if (previous is not null) await stateStore.SaveAsync(brandDirectory, previous with { RequiresValidation = true }, cancellationToken);
            return new(new(previous is null ? BrandValidationStatus.NotValidated : BrandValidationStatus.NeedsValidation), failures);
        }
        var fingerprint = await fingerprintCalculator.CalculateAsync(brandDirectory, definition, cancellationToken);
        var record = new BrandValidationRecord(definition.DefinitionChangedAtUtc, fingerprint, DateTimeOffset.UtcNow, false);
        await stateStore.SaveAsync(brandDirectory, record, cancellationToken);
        return new(new(BrandValidationStatus.Validated, record.ValidatedAtUtc, fingerprint), []);
    }
}
