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
            if (record.SchemaVersion != BrandValidationRecord.CurrentSchemaVersion ||
                record.AssetFingerprintFormatVersion != BrandFingerprintCalculator.AssetFingerprintFormatVersion)
            {
                return NeedsValidation(record, "brand_validation_record_outdated");
            }
            if (record.RequiresValidation) return NeedsValidation(record, "brand_validation_required");

            var definition = BrandValidationDefinition.CreateCurrent(settings);
            var definitionSignature = fingerprintCalculator.CalculateDefinitionSignature(definition);
            if (record.DefinitionChangedAtUtc != definition.DefinitionChangedAtUtc ||
                !string.Equals(record.DefinitionSignature, definitionSignature, StringComparison.Ordinal))
            {
                return NeedsValidation(record, "brand_definition_changed");
            }

            var resolved = await resolver.ResolveAsync(brandDirectory, definition, cancellationToken);
            var current = await fingerprintCalculator.CaptureAssetFingerprintAsync(brandDirectory, resolved, cancellationToken);
            if (!string.Equals(record.Fingerprint, current.Value, StringComparison.Ordinal))
            {
                return NeedsValidation(record, "brand_fingerprint_changed");
            }
            if (!TryValidateFacts(record.Assets, current.ExistingRelativePaths, out var facts))
            {
                return NeedsValidation(record, "brand_validation_record_invalid");
            }

            return new(
                BrandValidationStatus.Validated,
                record.ValidatedAtUtc,
                record.Fingerprint,
                ValidatedAssets: facts);
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
        var definitionSignature = fingerprintCalculator.CalculateDefinitionSignature(definition);
        var previous = await TryLoadPreviousAsync(brandDirectory, cancellationToken);
        var resolved = await resolver.ResolveAsync(brandDirectory, definition, cancellationToken);
        var before = await fingerprintCalculator.CaptureAssetFingerprintAsync(brandDirectory, resolved, cancellationToken);
        var failures = new List<BrandValidationFailure>();
        var facts = new List<BrandValidationAssetFact>();

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
                            if (!exists)
                            {
                                failures.Add(new(
                                    BrandValidationTargetResolver.NormalizeRelativePath(brandDirectory, file),
                                    "exists",
                                    "brand_asset_missing",
                                    "Required Brand asset is missing."));
                            }
                            break;
                        case BrandImageDimensionsRule dimensionRule when exists:
                            try
                            {
                                var size = await imageInspector.GetSizeAsync(file, cancellationToken);
                                if (!dimensionRule.AllowedSizes.Contains(size))
                                {
                                    failures.Add(new(
                                        BrandValidationTargetResolver.NormalizeRelativePath(brandDirectory, file),
                                        "dimensions",
                                        "brand_image_dimensions_invalid",
                                        $"Image is {DescribeSize(size)}. Required size: {DescribeAllowedSizes(dimensionRule.AllowedSizes)}."));
                                }
                                else
                                {
                                    facts.Add(new(
                                        BrandValidationTargetResolver.NormalizeRelativePath(brandDirectory, file),
                                        size));
                                }
                            }
                            catch (OperationCanceledException)
                            {
                                throw;
                            }
                            catch (Exception)
                            {
                                failures.Add(new(
                                    BrandValidationTargetResolver.NormalizeRelativePath(brandDirectory, file),
                                    "dimensions",
                                    "brand_image_unreadable",
                                    "Brand image could not be read."));
                            }
                            break;
                    }
                }
            }
        }

        if (failures.Count > 0)
        {
            await MarkPreviousRequiresValidationAsync(brandDirectory, previous, cancellationToken);
            return new(new(previous is null ? BrandValidationStatus.NotValidated : BrandValidationStatus.NeedsValidation), failures);
        }

        var afterResolved = await resolver.ResolveAsync(brandDirectory, definition, cancellationToken);
        var after = await fingerprintCalculator.CaptureAssetFingerprintAsync(brandDirectory, afterResolved, cancellationToken);
        if (!string.Equals(before.Value, after.Value, StringComparison.Ordinal))
        {
            await MarkPreviousRequiresValidationAsync(brandDirectory, previous, cancellationToken);
            return new(
                new(previous is null ? BrandValidationStatus.NotValidated : BrandValidationStatus.NeedsValidation),
                [new("Brand", "stability", "brand_changed_during_validation", "Brand files changed during validation. Retry after file edits are complete.")]);
        }

        var orderedFacts = facts.OrderBy(fact => fact.RelativePath, StringComparer.Ordinal).ToArray();
        if (!TryValidateFacts(orderedFacts, after.ExistingRelativePaths, out _))
        {
            await MarkPreviousRequiresValidationAsync(brandDirectory, previous, cancellationToken);
            return new(
                new(previous is null ? BrandValidationStatus.NotValidated : BrandValidationStatus.NeedsValidation),
                [new("Brand", "certificate", "brand_validation_record_invalid", "Validated Brand facts did not match the tracked asset set.")]);
        }

        var record = new BrandValidationRecord(
            BrandValidationRecord.CurrentSchemaVersion,
            BrandFingerprintCalculator.AssetFingerprintFormatVersion,
            definition.DefinitionChangedAtUtc,
            definitionSignature,
            after.Value,
            DateTimeOffset.UtcNow,
            RequiresValidation: false,
            orderedFacts);
        await stateStore.SaveAsync(brandDirectory, record, cancellationToken);
        return new(
            new BrandValidationState(
                BrandValidationStatus.Validated,
                record.ValidatedAtUtc,
                record.Fingerprint,
                ValidatedAssets: orderedFacts),
            []);
    }

    private async ValueTask<BrandValidationRecord?> TryLoadPreviousAsync(DirectoryReference brandDirectory, CancellationToken cancellationToken)
    {
        try
        {
            return await stateStore.LoadAsync(brandDirectory, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async ValueTask MarkPreviousRequiresValidationAsync(
        DirectoryReference brandDirectory,
        BrandValidationRecord? previous,
        CancellationToken cancellationToken)
    {
        if (previous is not null)
        {
            await stateStore.SaveAsync(brandDirectory, previous with { RequiresValidation = true }, cancellationToken);
        }
    }

    private static BrandValidationState NeedsValidation(BrandValidationRecord record, string reasonCode) =>
        new(BrandValidationStatus.NeedsValidation, record.ValidatedAtUtc, record.Fingerprint, reasonCode);

    private static bool TryValidateFacts(
        IReadOnlyList<BrandValidationAssetFact>? facts,
        IReadOnlyList<string> expectedPaths,
        out IReadOnlyList<BrandValidationAssetFact> validatedFacts)
    {
        validatedFacts = [];
        if (facts is null || facts.Count != expectedPaths.Count) return false;

        var expected = new HashSet<string>(expectedPaths, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var normalized = new List<BrandValidationAssetFact>(facts.Count);
        foreach (var fact in facts)
        {
            if (fact is null || fact.Width <= 0 || fact.Height <= 0) return false;

            string relativePath;
            try
            {
                relativePath = BrandValidationTargetResolver.NormalizeRelativePath(fact.RelativePath);
            }
            catch (ArgumentException)
            {
                return false;
            }
            if (!string.Equals(relativePath, fact.RelativePath, StringComparison.Ordinal) ||
                !expected.Contains(relativePath) ||
                !seen.Add(relativePath))
            {
                return false;
            }
            normalized.Add(new(relativePath, fact.Size));
        }

        if (!seen.SetEquals(expected)) return false;
        validatedFacts = normalized.OrderBy(fact => fact.RelativePath, StringComparer.Ordinal).ToArray();
        return true;
    }

    private static string DescribeAllowedSizes(IReadOnlyList<ImageSize> sizes) => string.Join(
        ", ",
        sizes.OrderBy(size => size.Width).ThenBy(size => size.Height).Select(DescribeSize));

    private static string DescribeSize(ImageSize size) => $"{size.Width} × {size.Height} px";
}
