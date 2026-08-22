using System.Text.Json;

namespace PrintableBook.Infrastructure.Tests;

internal static class CertificationArtifactStore
{
    public static void Capture(string stage, string caseId, params string[] files)
    {
        var target = Path.Combine(FindRepositoryRoot(), "TestResults", "Phase3", stage, caseId);
        Directory.CreateDirectory(target);
        foreach (var file in files.Where(File.Exists))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
        }

        File.WriteAllText(Path.Combine(target, "result.json"), JsonSerializer.Serialize(new
        {
            caseId,
            stage,
            generatedUtc = DateTimeOffset.UtcNow,
            files = files.Select(Path.GetFileName).ToArray(),
            automatedStatus = "PASS",
            userValidationStatus = "Pending"
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git"))) return directory.FullName;
        }

        throw new DirectoryNotFoundException("The Phase 3 artifact store requires a repository root.");
    }
}
