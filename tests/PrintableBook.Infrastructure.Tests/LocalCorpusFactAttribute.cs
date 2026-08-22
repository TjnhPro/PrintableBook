namespace PrintableBook.Infrastructure.Tests;

[AttributeUsage(AttributeTargets.Method)]
internal sealed class LocalCorpusFactAttribute : FactAttribute
{
    public LocalCorpusFactAttribute()
    {
        if (!LocalCorpusTestGate.IsEnabled(Environment.GetEnvironmentVariable(LocalCorpusTestGate.EnvironmentVariable)))
        {
            Skip = LocalCorpusTestGate.DisabledMessage;
        }
    }
}
