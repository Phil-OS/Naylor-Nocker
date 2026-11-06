using Microsoft.Extensions.Logging;
using System.Net.Sockets;
using System.Threading.Channels;
using NaylorNocker.Windows.Filtering;
using NaylorNocker.Core.Configuration;
namespace NaylorNocker.Service.Diagnostics;
public sealed record ServiceEvent(int Id, LogLevel Level, string Message);
public sealed class ServiceDiagnostics : IAsyncDisposable
{
    public static void ReportConfigurationFailure(ConfigurationException error)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            // Installation creates this source. Do not create one during validation/startup.
            if (System.Diagnostics.EventLog.SourceExists("NaylorNocker"))
                System.Diagnostics.EventLog.WriteEntry("NaylorNocker", error.Message,
                    System.Diagnostics.EventLogEntryType.Error, 1900);
        }
        catch { /* Console diagnostic and nonzero exit still apply if Event Log is unavailable. */ }
    }

    private readonly Channel<ServiceEvent> _events = Channel.CreateBounded<ServiceEvent>(256);
    private readonly Task _pump;

    public ServiceDiagnostics(ILogger<ServiceDiagnostics> logger)
        : this(entry => logger.Log(entry.Level, new EventId(entry.Id), "{Message}", entry.Message)) { }

    internal ServiceDiagnostics(Action<ServiceEvent> sink)
    {
        _pump = Task.Run(async () =>
        {
            await foreach (var entry in _events.Reader.ReadAllAsync())
                try { sink(entry); }
                catch { /* Local logging failure must never delay or bypass expiry. */ }
        });
    }

    public void Write(int id, LogLevel level, string message) =>
        _events.Writer.TryWrite(new(id, level, message));

    public void Fault(string stage, Exception error)
    {
        var category = error switch
        {
            WfpOperationException native => $"{native.Operation}/0x{unchecked((uint)native.NativeErrorCode):X8}",
            SocketException socket => socket.SocketErrorCode.ToString(),
            _ => error.GetType().Name
        };
        Write(1900, LogLevel.Critical, $"Service failure ({stage}: {category}).");
    }

    public async ValueTask DisposeAsync()
    {
        _events.Writer.TryComplete();
        try { await _pump.WaitAsync(TimeSpan.FromMilliseconds(100)); }
        catch (TimeoutException) { }
    }
}
