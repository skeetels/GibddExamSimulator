using System.Text.RegularExpressions;

namespace GibddExamSimulator.Tests;

public sealed class AndroidPackagingContractTests
{
    [Fact]
    public void AndroidProject_DeclaresInstallableOfflinePackageContract()
    {
        var repository = FindRepositoryRoot();
        var project = File.ReadAllText(Path.Combine(
            repository,
            "src",
            "GibddExamSimulator.Android",
            "GibddExamSimulator.Android.csproj"));

        Assert.Contains("<ApplicationId>app.gibddexamsimulator.mobile</ApplicationId>", project, StringComparison.Ordinal);
        Assert.Contains("<ApplicationDisplayVersion>2.0.4</ApplicationDisplayVersion>", project, StringComparison.Ordinal);
        Assert.Contains("<ApplicationVersion>204</ApplicationVersion>", project, StringComparison.Ordinal);
        Assert.Contains("<SupportedOSPlatformVersion>26.0</SupportedOSPlatformVersion>", project, StringComparison.Ordinal);
        Assert.Contains("assets/question-bank/ab/**/*", project, StringComparison.Ordinal);
        Assert.Contains("<AndroidPackageFormats>apk</AndroidPackageFormats>", project, StringComparison.Ordinal);
    }

    [Fact]
    public void AndroidClientConfiguration_ContainsNoPrivateTelegramCredential()
    {
        var repository = FindRepositoryRoot();
        var configurationRoot = Path.Combine(
            repository,
            "src",
            "GibddExamSimulator.Android",
            "Configuration");
        var tokenPattern = new Regex(
            @"\b\d{8,12}:[A-Za-z0-9_-]{30,}\b",
            RegexOptions.CultureInvariant);

        foreach (var file in Directory.EnumerateFiles(configurationRoot, "*.json"))
            Assert.DoesNotMatch(tokenPattern, File.ReadAllText(file));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GibddExamSimulator.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
