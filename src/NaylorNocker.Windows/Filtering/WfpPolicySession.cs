using System.ComponentModel;
using System.Net;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using NaylorNocker.Windows.Interop;

namespace NaylorNocker.Windows.Filtering;

// Shared by the foreground proof and the service candidate; see Windows acceptance status.
[SupportedOSPlatform("windows")]
public sealed class WfpPolicySession : IDisposable
{
    internal object Sync { get; } = new();
    private readonly SafeWfpEngineHandle _engine;
    internal Guid SubLayer { get; } = Guid.NewGuid();
    internal bool IsDisposed { get; private set; }

    private WfpPolicySession(SafeWfpEngineHandle engine) => _engine = engine;

    public static WfpPolicySession Open()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8)
            throw new PlatformNotSupportedException("naylor nocker requires Windows x64 and administrator rights.");
        var name = Marshal.StringToHGlobalUni("naylor nocker");
        try
        {
            var options = new FwpmSession
            {
                Key = Guid.NewGuid(), DisplayData = new() { Name = name },
                Flags = 1, TransactionWaitTimeoutMs = 2000 // FWPM_SESSION_FLAG_DYNAMIC
            };
            Check(WfpNative.FwpmEngineOpen0(null, 10, IntPtr.Zero, ref options, out var handle), "FwpmEngineOpen0");
            var session = new WfpPolicySession(new SafeWfpEngineHandle(handle));
            try
            {
                var subLayer = new FwpmSubLayer
                {
                    Key = session.SubLayer, DisplayData = new() { Name = name }, Weight = ushort.MaxValue
                };
                Check(WfpNative.FwpmSubLayerAdd0(session._engine, ref subLayer, IntPtr.Zero), "FwpmSubLayerAdd0");
                return session;
            }
            catch { session.Dispose(); throw; }
        }
        finally { Marshal.FreeHGlobal(name); }
    }

    public NativeGrant AddGrant(IPAddress source, IPAddress? localAddress, ushort port)
    {
        var plan = GrantFilterPlan.Create(source, localAddress, port);
        var retention = InTransaction(() =>
        {
            using var buffer = new NativeFilterBuffer(plan, SubLayer, reauthorize: true);
            return AddFilter(ref buffer.Filter);
        });
        return new NativeGrant(this, plan, retention);
    }

    public bool ContainsFilter(ulong id)
    {
        lock (Sync)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            var result = WfpNative.FwpmFilterGetById0(_engine, id, out var pointer);
            if (result == 0x80320003) return false; // FWP_E_FILTER_NOT_FOUND
            Check(result, "FwpmFilterGetById0");
            WfpNative.FwpmFreeMemory0(ref pointer);
            return true;
        }
    }

    internal T InTransaction<T>(Func<T> action)
    {
        lock (Sync)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            var began = false;
            try
            {
                Check(WfpNative.FwpmTransactionBegin0(_engine, 0), "FwpmTransactionBegin0");
                began = true;
                var value = action();
                Check(WfpNative.FwpmTransactionCommit0(_engine), "FwpmTransactionCommit0");
                return value;
            }
            catch
            {
                if (began) _ = WfpNative.FwpmTransactionAbort0(_engine);
                // Losing any policy update is fatal: release ALL dynamic permits.
                Dispose();
                throw;
            }
        }
    }

    internal ulong AddFilter(ref FwpmFilter filter)
    {
        Check(WfpNative.FwpmFilterAdd0(_engine, ref filter, IntPtr.Zero, out var id), "FwpmFilterAdd0");
        return id;
    }

    internal void DeleteFilter(ulong id) =>
        Check(WfpNative.FwpmFilterDeleteById0(_engine, id), "FwpmFilterDeleteById0");

    public void Dispose()
    {
        lock (Sync)
        {
            if (IsDisposed) return;
            IsDisposed = true;
            _engine.Dispose();
            Check(_engine.CloseError, "FwpmEngineClose0");
        }
    }

    private static void Check(uint error, string operation)
    {
        if (error != 0) throw new WfpOperationException(operation, error);
    }
}

public sealed class WfpOperationException(string operation, uint error)
    : Win32Exception(unchecked((int)error), $"{operation} failed (0x{error:X8}).")
{
    public string Operation { get; } = operation;
}
