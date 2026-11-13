using System.Runtime.InteropServices;

namespace RouteAudio.Interop;

/// <summary>
/// PROPVARIANT 的最小实现。x64 下结构体 24 字节：vt(2) + reserved(6) + union(16)。
/// 只覆盖本项目用到的类型：LPWSTR / BLOB / UI4 / BOOL / IUNKNOWN。
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 24)]
public struct PropVariant
{
    public const ushort VT_EMPTY = 0;
    public const ushort VT_I4 = 3;
    public const ushort VT_BOOL = 11;
    public const ushort VT_UI4 = 19;
    public const ushort VT_LPWSTR = 31;
    public const ushort VT_BLOB = 65;

    [FieldOffset(0)] public ushort VarType;
    [FieldOffset(8)] public IntPtr PointerValue;
    [FieldOffset(8)] public int Int32Value;
    [FieldOffset(8)] public uint UInt32Value;
    [FieldOffset(8)] public short BoolValue;
    [FieldOffset(8)] public uint BlobSize;
    [FieldOffset(16)] public IntPtr BlobData;

    public static PropVariant FromString(string value)
    {
        var pv = new PropVariant { VarType = VT_LPWSTR, PointerValue = Marshal.StringToCoTaskMemUni(value) };
        return pv;
    }

    public static PropVariant FromBool(bool value) => new() { VarType = VT_BOOL, BoolValue = value ? (short)-1 : (short)0 };

    public static PropVariant FromUInt32(uint value) => new() { VarType = VT_UI4, UInt32Value = value };

    public static PropVariant FromBlob(IntPtr data, uint size) => new() { VarType = VT_BLOB, BlobSize = size, BlobData = data };

    public string? AsString() => VarType == VT_LPWSTR ? Marshal.PtrToStringUni(PointerValue) : null;

    public uint? AsUInt32() => VarType switch
    {
        VT_UI4 => UInt32Value,
        VT_I4 => (uint)Int32Value,
        _ => null,
    };

    public void Clear()
    {
        if (VarType == VT_EMPTY) return;
        _ = NativeMethods.PropVariantClear(ref this);
        VarType = VT_EMPTY;
    }
}
