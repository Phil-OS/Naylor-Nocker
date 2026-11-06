using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using NaylorNocker.Windows.Interop;
namespace NaylorNocker.Windows.Filtering;

internal sealed class NativeFilterBuffer : IDisposable
{
    private readonly List<IntPtr> _allocations = [];
    public FwpmFilter Filter;
    public NativeFilterBuffer(GrantFilterPlan plan, Guid subLayer, bool reauthorize)
    {
        plan = GrantFilterPlan.Create(plan.Source, plan.LocalAddress, plan.Port);
        try
        {
            List<FwpmFilterCondition> conditions =
            [
                Address(new("b235ae9a-1d64-49b8-a44c-5ff3d9095045"), plan.Source),
                new() { FieldKey = new("3971ef2b-623e-4f9a-8cb1-6e79b806b9a7"),
                    Value = new() { Type = FwpDataType.UInt8, UInt8 = 6 } },
                new() { FieldKey = new("0c1ba1af-5765-453f-af22-a8f791ac775b"),
                    Value = new() { Type = FwpDataType.UInt16, UInt16 = plan.Port } },
                new() { FieldKey = new("632ce23b-5167-435c-86d7-e903684aa80c"),
                    MatchType = reauthorize ? FwpMatchType.FlagsAllSet : FwpMatchType.FlagsNoneSet,
                    Value = new() { Type = FwpDataType.UInt32, UInt32 = 4 } }
            ];
            if (plan.LocalAddress is not null)
                conditions.Add(Address(new("d9ee00de-c1ef-4617-bfe3-ffd8f5a08957"), plan.LocalAddress));
            if (plan.InterfaceIndex is uint index)
                conditions.Add(new() { FieldKey = new("667fd755-d695-434a-8af5-d3835a1259bc"),
                    Value = new() { Type = FwpDataType.UInt32, UInt32 = index } });

            var stride = Marshal.SizeOf<FwpmFilterCondition>();
            var memory = Allocate(stride * conditions.Count);
            for (var i = 0; i < conditions.Count; i++)
                Marshal.StructureToPtr(conditions[i], memory + i * stride, false);
            Filter = new()
            {
                Key = Guid.NewGuid(),
                DisplayData = new() { Name = String(reauthorize
                    ? "naylor nocker: retention" : "naylor nocker: admission") },
                Flags = 8, // FWPM_FILTER_FLAG_CLEAR_ACTION_RIGHT: candidate hard permit; prove precedence.
                LayerKey = plan.Layer,
                SubLayerKey = subLayer,
                ConditionCount = checked((uint)conditions.Count),
                Conditions = memory,
                Action = new() { Type = 0x1002 } // FWP_ACTION_PERMIT
            };
        }
        catch { Dispose(); throw; }
    }

    private FwpmFilterCondition Address(Guid key, IPAddress address)
    {
        var v4 = address.AddressFamily == AddressFamily.InterNetwork;
        return new() { FieldKey = key, Value = new()
        {
            Type = v4 ? FwpDataType.V4AddressMask : FwpDataType.V6AddressMask,
            Pointer = v4
                ? Structure(new FwpV4AddressMask
                    { Address = BinaryPrimitives.ReadUInt32BigEndian(address.GetAddressBytes()), Mask = uint.MaxValue })
                : Structure(new FwpV6AddressMask { Address = address.GetAddressBytes(), PrefixLength = 128 })
        }};
    }

    private IntPtr Structure<T>(T value) where T : struct
    {
        var memory = Allocate(Marshal.SizeOf<T>());
        Marshal.StructureToPtr(value, memory, false);
        return memory;
    }

    private IntPtr Allocate(int bytes)
    {
        var pointer = Marshal.AllocHGlobal(bytes);
        _allocations.Add(pointer);
        return pointer;
    }

    private IntPtr String(string value)
    {
        var pointer = Marshal.StringToHGlobalUni(value);
        _allocations.Add(pointer);
        return pointer;
    }

    public void Dispose()
    {
        foreach (var pointer in _allocations) Marshal.FreeHGlobal(pointer);
        _allocations.Clear();
        Filter = default;
    }
}
