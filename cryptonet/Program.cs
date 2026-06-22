using cryptonet.Data;
using cryptonet.Models;
using cryptonet.Services;
using cryptonet.Workers;
using DotNetEnv;
using Microsoft.AspNetCore.HttpOverrides;
using System.Diagnostics;

Env.TraversePath().Load();

var builder = WebApplication.CreateBuilder(args);

var skipPythonLaunch = Environment.GetEnvironmentVariable("SKIP_PYTHON_LAUNCH") == "true";

if (!skipPythonLaunch)
{
    var aiScriptPath = Path.Combine(builder.Environment.ContentRootPath, "ai_service.py");

    if (File.Exists(aiScriptPath))
    {
        var pythonProcess = new Process();
        pythonProcess.StartInfo.FileName = OperatingSystem.IsWindows() ? "python" : "python3";
        pythonProcess.StartInfo.Arguments = $"\"{aiScriptPath}\"";
        pythonProcess.StartInfo.UseShellExecute = false;
        pythonProcess.StartInfo.CreateNoWindow = true;
        pythonProcess.Start();
    }
}

var enableHttps = Environment.GetEnvironmentVariable("ENABLE_HTTPS") == "true";

builder.Services.Configure<AiServiceSettings>(builder.Configuration.GetSection("AiService"));
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});
builder.Services.AddControllersWithViews();
builder.Services.AddScoped<cryptonet.Filters.AuthFilter>();
builder.Services.AddSingleton<DbConnectionFactory>();
builder.Services.AddHttpClient<CoinGeckoService>();
builder.Services.AddHostedService<MarketUpdaterService>();
builder.Services.AddSession();
builder.Services.AddHttpClient();
builder.Services.AddHostedService<DailyAiUpdater>();
builder.Services.AddHostedService<AlertCheckerService>();
builder.Services.AddSignalR();
builder.Services.AddHostedService<PriceAlertService>();
builder.Services.AddScoped<GroqService>();
builder.Services.AddHostedService<NewsWorkerService>();

var app = builder.Build();

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");

    if (enableHttps)
    {
        app.UseHsts();
    }
}

if (enableHttps)
{
    app.UseHttpsRedirection();
}
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.UseSession();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");

app.MapHub<cryptonet.Hubs.AlertHub>("/alerthub");

app.Run();
