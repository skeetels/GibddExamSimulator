using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using GibddExamSimulator.Web;
using GibddExamSimulator.Web.Services;
using GibddExamSimulator.Application.Storage;
using GibddExamSimulator.Application.Synchronization;
using GibddExamSimulator.Mobile.Shared.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<BrowserStudyStore>();
builder.Services.AddScoped<WebQuestionBankLoader>();
builder.Services.AddScoped<OfflinePackageService>();
builder.Services.AddScoped<ILocalStudyStore>(services => services.GetRequiredService<BrowserStudyStore>());
builder.Services.AddScoped<IAuthSessionStore>(services => services.GetRequiredService<BrowserStudyStore>());
builder.Services.AddScoped<IDeviceLinkStateStore>(services => services.GetRequiredService<BrowserStudyStore>());
builder.Services.AddScoped<IMobileConfigurationProvider, WebMobileConfigurationProvider>();
builder.Services.AddScoped<IMobileQuestionBankLoader>(services => services.GetRequiredService<WebQuestionBankLoader>());
builder.Services.AddScoped<IMobileOfflinePackageService>(services => services.GetRequiredService<OfflinePackageService>());
builder.Services.AddScoped<IMobilePlatform, PwaMobilePlatform>();
builder.Services.AddScoped<IMobileQrScanner, WebQrScanner>();
builder.Services.AddScoped<MobileAppState>();

await builder.Build().RunAsync();
