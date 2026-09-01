using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using GibddExamSimulator.Web;
using GibddExamSimulator.Web.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<BrowserStudyStore>();
builder.Services.AddScoped<WebQuestionBankLoader>();
builder.Services.AddScoped<OfflinePackageService>();
builder.Services.AddScoped<MobileAppState>();

await builder.Build().RunAsync();
