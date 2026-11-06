using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Logging.EventLog;
using NaylorNocker.Core.Admission;
using NaylorNocker.Core.Configuration;
using NaylorNocker.Service;
using NaylorNocker.Service.Diagnostics;
using NaylorNocker.Windows.Filtering;

return await ServiceCommand.RunAsync(args, RunServiceAsync, Console.Error, ServiceDiagnostics.ReportConfigurationFailure);

static async Task<int> RunServiceAsync(ValidatedKnockOptions options)
{
    if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
    {
        await Console.Error.WriteLineAsync("naylor nocker requires Windows x64.");
        return 1;
    }
    var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
    {
        Args = [], DisableDefaults = true, ContentRootPath = AppContext.BaseDirectory
    });
    builder.Services.AddWindowsService(service => service.ServiceName = "NaylorNocker");
    builder.Services.Configure<HostOptions>(host => host.ShutdownTimeout = TimeSpan.FromSeconds(8));
    builder.Logging.ClearProviders();
    builder.Logging.SetMinimumLevel(Enum.Parse<LogLevel>(options.LogLevel));
    builder.Logging.AddFilter((category, _) => category?.StartsWith("NaylorNocker", StringComparison.Ordinal) == true);
    builder.Logging.AddConsole(console =>
    {
        console.MaxQueueLength = 256;
        console.QueueFullMode = ConsoleLoggerQueueFullMode.DropWrite;
    });
    builder.Logging.AddEventLog(events => { events.SourceName = "NaylorNocker"; events.LogName = "Application"; });
    builder.Logging.AddFilter<EventLogLoggerProvider>((category, level) =>
        category?.StartsWith("NaylorNocker", StringComparison.Ordinal) == true && level >= Enum.Parse<LogLevel>(options.LogLevel));
    builder.Services.AddSingleton<ServiceDiagnostics>();
    builder.Services.AddSingleton<IFirewallBackend>(_ => new WfpFirewallBackend(options.BindAddress));
    builder.Services.AddSingleton(provider => new KnockWorker(options,
        provider.GetRequiredService<IFirewallBackend>(), TimeProvider.System,
        provider.GetRequiredService<ServiceDiagnostics>(), provider.GetRequiredService<IHostApplicationLifetime>(), Environment.Exit));
    builder.Services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<KnockWorker>());
    var host = builder.Build();
    await using var hostDisposal = (IAsyncDisposable)host;
    var diagnostics = host.Services.GetRequiredService<ServiceDiagnostics>();
    KnockWorker? worker = null;
    try
    {
        worker = host.Services.GetRequiredService<KnockWorker>();
        await host.RunAsync();
        return worker.ExitCode;
    }
    catch (Exception error)
    {
        diagnostics.Fault("host", error);
        if (worker is not null) await worker.StopAsync(CancellationToken.None);
        return 1;
    }
}
