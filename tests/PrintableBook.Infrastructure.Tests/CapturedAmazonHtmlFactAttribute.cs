namespace PrintableBook.Infrastructure.Tests;

[AttributeUsage(AttributeTargets.Method)]
internal sealed class CapturedAmazonHtmlFactAttribute : FactAttribute
{
    public CapturedAmazonHtmlFactAttribute()
    {
        if (!CapturedAmazonHtmlTestGate.IsEnabled(
                Environment.GetEnvironmentVariable(CapturedAmazonHtmlTestGate.EnvironmentVariable)))
        {
            Skip = CapturedAmazonHtmlTestGate.DisabledMessage;
        }
    }
}
