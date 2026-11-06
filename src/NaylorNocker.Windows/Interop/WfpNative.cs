using System.Runtime.InteropServices;

namespace NaylorNocker.Windows.Interop;

internal static class WfpNative
{
    private const string Library = "fwpuclnt.dll";
    [DllImport(Library, CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern uint FwpmEngineOpen0(string? server, uint authentication, IntPtr identity,
        ref FwpmSession session, out IntPtr engine);
    [DllImport(Library, ExactSpelling = true)]
    internal static extern uint FwpmEngineClose0(IntPtr engine);
    [DllImport(Library, ExactSpelling = true)]
    internal static extern uint FwpmSubLayerAdd0(SafeWfpEngineHandle engine, ref FwpmSubLayer subLayer, IntPtr security);
    [DllImport(Library, ExactSpelling = true)]
    internal static extern uint FwpmTransactionBegin0(SafeWfpEngineHandle engine, uint flags);
    [DllImport(Library, ExactSpelling = true)]
    internal static extern uint FwpmTransactionCommit0(SafeWfpEngineHandle engine);
    [DllImport(Library, ExactSpelling = true)]
    internal static extern uint FwpmTransactionAbort0(SafeWfpEngineHandle engine);
    [DllImport(Library, ExactSpelling = true)]
    internal static extern uint FwpmFilterAdd0(SafeWfpEngineHandle engine, ref FwpmFilter filter, IntPtr security, out ulong id);
    [DllImport(Library, ExactSpelling = true)]
    internal static extern uint FwpmFilterDeleteById0(SafeWfpEngineHandle engine, ulong id);
    [DllImport(Library, ExactSpelling = true)]
    internal static extern uint FwpmFilterGetById0(SafeWfpEngineHandle engine, ulong id, out IntPtr filter);
    [DllImport(Library, ExactSpelling = true)]
    internal static extern void FwpmFreeMemory0(ref IntPtr memory);
}
