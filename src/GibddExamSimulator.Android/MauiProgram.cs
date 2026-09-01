using GibddExamSimulator.Application.Storage;
using GibddExamSimulator.Infrastructure.Storage;
using GibddExamSimulator.Mobile.Shared.Services;
using Microsoft.Extensions.Logging;

namespace GibddExamSimulator.Android;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();
        builder.Services.AddMauiBlazorWebView();

        builder.Services.AddSingleton(_ =>
        {
            var dataDirectory = Path.Combine(FileSystem.AppDataDirectory, "Data");
            Directory.CreateDirectory(dataDirectory);
            return new DesktopStudyStore(Path.Combine(dataDirectory, "questions.db"));
        });
        builder.Services.AddSingleton<ILocalStudyStore>(services => services.GetRequiredService<DesktopStudyStore>());
        builder.Services.AddSingleton<AndroidAuthSessionStore>();
        builder.Services.AddSingleton<IAuthSessionStore>(services => services.GetRequiredService<AndroidAuthSessionStore>());
        builder.Services.AddSingleton<IMobileConfigurationProvider, AndroidConfigurationProvider>();
        builder.Services.AddSingleton<IMobileQuestionBankLoader, AndroidQuestionBankLoader>();
        builder.Services.AddSingleton<IMobileOfflinePackageService, AndroidOfflinePackageService>();
        builder.Services.AddSingleton<IMobilePlatform, AndroidMobilePlatform>();
        builder.Services.AddSingleton<MobileAppState>();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
