namespace GibddExamSimulator.Tests;

public sealed class ZeroConfigUiContractTests
{
    [Fact]
    public void WindowsFirstRun_IsQrPairingWithoutCredentialForm()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "GibddExamSimulator.App", "MainWindow.xaml"));

        Assert.Contains("ConverterParameter=Pairing", xaml, StringComparison.Ordinal);
        Assert.Contains("Синхронизация с телефоном", xaml, StringComparison.Ordinal);
        Assert.Contains("PairingQrImage", xaml, StringComparison.Ordinal);
        Assert.Contains("Продолжить пока без телефона", xaml, StringComparison.Ordinal);
        Assert.Contains("Подключённые устройства", xaml, StringComparison.Ordinal);
        Assert.Contains("Сбросить синхронизацию", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("PasswordBox", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"Email\"", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Content=\"Войти\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MobileFirstRun_HasCameraAndNoLoginRoute()
    {
        var root = FindRepositoryRoot();
        var pages = Path.Combine(root, "src", "GibddExamSimulator.Mobile.Shared", "Pages");
        var home = File.ReadAllText(Path.Combine(pages, "Home.razor"));
        var allPages = string.Join('\n', Directory.EnumerateFiles(pages, "*.razor").Select(File.ReadAllText));

        Assert.False(File.Exists(Path.Combine(pages, "Login.razor")));
        Assert.Contains("Открыть камеру", home, StringComparison.Ordinal);
        Assert.Contains("Ввести короткий код вручную", home, StringComparison.Ordinal);
        Assert.Contains("Подключённые устройства", home, StringComparison.Ordinal);
        Assert.Contains("Сбросить синхронизацию", home, StringComparison.Ordinal);
        Assert.DoesNotContain("@page \"/login\"", allPages, StringComparison.Ordinal);
        Assert.DoesNotContain("type=\"password\"", allPages, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GitHub owner", allPages, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Supabase URL", allPages, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Pwa_EncryptsAnonymousAuthWithWebCrypto()
    {
        var root = FindRepositoryRoot();
        var storage = File.ReadAllText(Path.Combine(root, "src", "GibddExamSimulator.Web", "wwwroot", "js", "storage.js"));
        var store = File.ReadAllText(Path.Combine(root, "src", "GibddExamSimulator.Web", "Services", "BrowserStudyStore.cs"));

        Assert.Contains("AES-GCM", storage, StringComparison.Ordinal);
        Assert.Contains("crypto.subtle.encrypt", storage, StringComparison.Ordinal);
        Assert.Contains("crypto.subtle.decrypt", storage, StringComparison.Ordinal);
        Assert.Contains("non-exportable", storage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("false,", storage, StringComparison.Ordinal);
        Assert.Contains("gibddStorage.secureGet", store, StringComparison.Ordinal);
        Assert.Contains("gibddStorage.securePut", store, StringComparison.Ordinal);
    }

    [Fact]
    public void DeploymentWorkflows_RequireBackendPagesAndExactReleaseAssets()
    {
        var root = FindRepositoryRoot();
        var workflows = Path.Combine(root, ".github", "workflows");
        var backend = File.ReadAllText(Path.Combine(workflows, "backend-deploy.yml"));
        var release = File.ReadAllText(Path.Combine(workflows, "release.yml"));

        Assert.Contains("supabase db push", backend, StringComparison.Ordinal);
        Assert.Contains("device-api", backend, StringComparison.Ordinal);
        Assert.Contains("configure_telegram_webhook.py", backend, StringComparison.Ordinal);
        Assert.Contains("GibddExamSimulator-Setup-2.0.2-win-x64.exe", release, StringComparison.Ordinal);
        Assert.Contains("GibddExamSimulator-2.0.2-android.apk", release, StringComparison.Ordinal);
        Assert.Contains("pairing-e2e-evidence.zip", release, StringComparison.Ordinal);
        Assert.Contains("validate_production_artifacts.py", release, StringComparison.Ordinal);
        Assert.DoesNotContain("DEV-SIGNED.apk\"", release, StringComparison.Ordinal);
    }

    [Fact]
    public void Android_ContainsNativeCameraScannerAndRuntimePermission()
    {
        var root = FindRepositoryRoot();
        var project = File.ReadAllText(Path.Combine(root, "src", "GibddExamSimulator.Android", "GibddExamSimulator.Android.csproj"));
        var manifest = File.ReadAllText(Path.Combine(root, "src", "GibddExamSimulator.Android", "Platforms", "Android", "AndroidManifest.xml"));
        var scanner = File.ReadAllText(Path.Combine(root, "src", "GibddExamSimulator.Android", "Services", "AndroidQrScanner.cs"));

        Assert.Contains("ZXing.Net.Maui.Controls", project, StringComparison.Ordinal);
        Assert.Contains("android.permission.CAMERA", manifest, StringComparison.Ordinal);
        Assert.Contains("Permissions.RequestAsync<Permissions.Camera>()", scanner, StringComparison.Ordinal);
        Assert.Contains("CameraBarcodeReaderView", scanner, StringComparison.Ordinal);
        Assert.DoesNotContain("PickPhoto", scanner, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ClientsShareOneVersionedDeploymentContract()
    {
        var root = FindRepositoryRoot();
        var paths = new[]
        {
            Path.Combine(root, "src", "GibddExamSimulator.App", "Configuration", "client-settings.json"),
            Path.Combine(root, "src", "GibddExamSimulator.Android", "Configuration", "client-settings.json"),
            Path.Combine(root, "src", "GibddExamSimulator.Web", "wwwroot", "client-settings.json")
        };
        var contents = paths.Select(File.ReadAllText).ToArray();

        Assert.All(contents, text =>
        {
            Assert.Contains("\"configVersion\": 1", text, StringComparison.Ordinal);
            Assert.Contains("\"environmentId\"", text, StringComparison.Ordinal);
            Assert.Contains("\"syncApiBaseUrl\"", text, StringComparison.Ordinal);
            Assert.Contains("\"configSha256\"", text, StringComparison.Ordinal);
            Assert.DoesNotContain("service_role", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("github_pat_", text, StringComparison.OrdinalIgnoreCase);
        });
        Assert.Equal(contents[0], contents[1]);
        Assert.Equal(contents[0], contents[2]);
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
