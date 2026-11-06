using Microsoft.Win32.SafeHandles;

namespace NaylorNocker.Windows.Interop;

internal sealed class SafeWfpEngineHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal uint CloseError { get; private set; }
    internal SafeWfpEngineHandle(IntPtr handle) : base(true) => SetHandle(handle);
    protected override bool ReleaseHandle()
    {
        CloseError = WfpNative.FwpmEngineClose0(handle);
        return CloseError == 0;
    }
}
