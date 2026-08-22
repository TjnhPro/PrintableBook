namespace PrintableBook.Infrastructure.Tests;

public sealed class LocalCorpusTestGateTests
{
    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("TRUE")]
    public void IsEnabled_accepts_explicit_opt_in_values(string value)
    {
        Assert.True(LocalCorpusTestGate.IsEnabled(value));
    }

    [Theory]
    [InlineData("")]
    [InlineData("yes")]
    [InlineData("0")]
    public void IsEnabled_rejects_non_opt_in_values(string value)
    {
        Assert.False(LocalCorpusTestGate.IsEnabled(value));
    }

    [Fact]
    public void DisabledMessage_explains_how_to_run_the_local_corpus()
    {
        Assert.Contains(LocalCorpusTestGate.EnvironmentVariable, LocalCorpusTestGate.DisabledMessage);
        Assert.Contains("TestScope=LocalCorpus", LocalCorpusTestGate.DisabledMessage);
    }
}
