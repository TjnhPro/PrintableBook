namespace PrintableBook.Infrastructure.Tests;

[AttributeUsage(AttributeTargets.Method)]
internal sealed class ExternalCloakBrowserFactAttribute : FactAttribute
{
    public ExternalCloakBrowserFactAttribute()
    {
        if (!ExternalCloakBrowserTestGate.IsEnabled(
                Environment.GetEnvironmentVariable(ExternalCloakBrowserTestGate.EnvironmentVariable)))
        {
            Skip = ExternalCloakBrowserTestGate.DisabledMessage;
        }
    }
}
