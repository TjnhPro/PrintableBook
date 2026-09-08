using System.Reflection;
using PrintableBook.Core.Application.Updates;

namespace PrintableBook.Desktop.Updates;

public sealed class DesktopApplicationVersionProvider : IApplicationVersionProvider
{
    public Version CurrentVersion
    {
        get
        {
            var informationalVersion = typeof(App).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;

            if (string.IsNullOrWhiteSpace(informationalVersion) ||
                !Version.TryParse(informationalVersion, out var version))
            {
                throw new InvalidOperationException(
                    "PrintableBook Desktop informational version is missing or invalid.");
            }

            return version;
        }
    }
}
