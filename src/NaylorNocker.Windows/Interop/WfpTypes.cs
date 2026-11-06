using System.Runtime.InteropServices;

namespace NaylorNocker.Windows.Interop;

// x64 definitions from Microsoft's fwpmtypes.h / fwptypes.h. All strings are UTF-16 pointers.
internal enum FwpDataType : uint { Empty = 0, UInt8 = 1, UInt16 = 2, UInt32 = 3, V4AddressMask = 256, V6AddressMask = 257 }
internal enum FwpMatchType : uint { Equal = 0, FlagsAllSet = 6, FlagsNoneSet = 8 }

[StructLayout(LayoutKind.Explicit, Size = 16)]
internal struct FwpValue
{
    [FieldOffset(0)] public FwpDataType Type;
    [FieldOffset(8)] public byte UInt8;
    [FieldOffset(8)] public ushort UInt16;
    [FieldOffset(8)] public uint UInt32;
    [FieldOffset(8)] public IntPtr Pointer;
}

[StructLayout(LayoutKind.Sequential)]
internal struct FwpByteBlob { public uint Size; public IntPtr Data; }
[StructLayout(LayoutKind.Sequential)]
internal struct FwpmDisplayData { public IntPtr Name; public IntPtr Description; }
[StructLayout(LayoutKind.Sequential)]
internal struct FwpmSession
{
    public Guid Key;
    public FwpmDisplayData DisplayData;
    public uint Flags;
    public uint TransactionWaitTimeoutMs;
    public uint ProcessId;
    public IntPtr Sid;
    public IntPtr Username;
    public int KernelMode;
}
[StructLayout(LayoutKind.Sequential)]
internal struct FwpmSubLayer
{
    public Guid Key;
    public FwpmDisplayData DisplayData;
    public uint Flags;
    public IntPtr ProviderKey;
    public FwpByteBlob ProviderData;
    public ushort Weight;
}
[StructLayout(LayoutKind.Sequential)]
internal struct FwpmAction { public uint Type; public Guid FilterType; }
[StructLayout(LayoutKind.Sequential)]
internal struct FwpmFilterCondition { public Guid FieldKey; public FwpMatchType MatchType; public FwpValue Value; }
[StructLayout(LayoutKind.Explicit, Size = 16)]
internal struct FwpmContext
{
    [FieldOffset(0)] public ulong RawContext;
    [FieldOffset(0)] public Guid ProviderContextKey;
}
[StructLayout(LayoutKind.Sequential)]
internal struct FwpmFilter
{
    public Guid Key;
    public FwpmDisplayData DisplayData;
    public uint Flags;
    public IntPtr ProviderKey;
    public FwpByteBlob ProviderData;
    public Guid LayerKey;
    public Guid SubLayerKey;
    public FwpValue Weight;
    public uint ConditionCount;
    public IntPtr Conditions;
    public FwpmAction Action;
    public FwpmContext Context;
    public IntPtr Reserved;
    public ulong FilterId;
    public FwpValue EffectiveWeight;
}
[StructLayout(LayoutKind.Sequential)]
internal struct FwpV4AddressMask { public uint Address; public uint Mask; }
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct FwpV6AddressMask
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] Address;
    public byte PrefixLength;
}
