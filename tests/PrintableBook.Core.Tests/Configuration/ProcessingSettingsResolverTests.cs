using PrintableBook.Core.Configuration;

namespace PrintableBook.Core.Tests.Configuration;

public sealed class ProcessingSettingsResolverTests
{
    [Fact]
    public async Task ResolveAsync_applies_later_sources_as_runtime_overrides_and_copies_the_snapshot()
    {
        var first = new DictionaryProcessingSettingsSource(new Dictionary<string, string?>
        {
            ["output.format"] = "pdf",
            ["shared"] = "brand"
        });
        var runtime = new DictionaryProcessingSettingsSource(new Dictionary<string, string?>
        {
            ["shared"] = "runtime"
        });
        var resolver = new ProcessingSettingsResolver([first, runtime]);

        var settings = await resolver.ResolveAsync();
        first.MutableValues["output.format"] = "changed-after-resolution";

        Assert.Equal("pdf", settings["output.format"]);
        Assert.Equal("runtime", settings["shared"]);
    }

    [Fact]
    public async Task ResolveAsync_rejects_blank_setting_keys_from_a_source()
    {
        var resolver = new ProcessingSettingsResolver(
        [
            new DictionaryProcessingSettingsSource(new Dictionary<string, string?>
            {
                [" "] = "unsafe"
            })
        ]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => resolver.ResolveAsync().AsTask());
    }

    private sealed class DictionaryProcessingSettingsSource(IReadOnlyDictionary<string, string?> values)
        : IProcessingSettingsSource
    {
        public Dictionary<string, string?> MutableValues { get; } = new(values);

        public IReadOnlyDictionary<string, string?> Values => MutableValues;

        public ValueTask<IReadOnlyDictionary<string, string?>> LoadAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Values);
    }
}
