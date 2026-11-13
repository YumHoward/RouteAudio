using System.Runtime.InteropServices;

namespace RouteAudio.Interop;

/// <summary>
/// 访问系统内部 WinRT 类 <c>Windows.Media.Internal.AudioPolicyConfig</c>。
/// 该类没有公开 winmd，只能通过激活工厂 + IID 探测 + 原始虚表调用来使用。
/// 所有调用都必须检查 HRESULT；未知版本一律视为不可用。
/// </summary>
public sealed class AudioPolicyConfig : IDisposable
{
    public const string ActivatableClassId = "Windows.Media.Internal.AudioPolicyConfig";

    // 策略 API 需要的是「设备接口路径」，不是 IMMDevice::GetId() 的端点 ID：
    //   \\?\SWD#MMDEVAPI#{0.0.0.00000000}.{guid}#{e6327cad-dcec-4949-ae8a-991e976a79d2}
    private const string MmdevapiToken = @"\\?\SWD#MMDEVAPI#";
    private const string RenderInterfaceSuffix = "#{e6327cad-dcec-4949-ae8a-991e976a79d2}";
    private const string CaptureInterfaceSuffix = "#{2eef81be-33fa-4800-9670-1cd474972c3f}";

    /// <summary>端点 ID → 设备接口路径（Set 需要）。</summary>
    public static string ToDeviceInterfaceId(string endpointId, EDataFlow flow = EDataFlow.eRender)
    {
        if (endpointId.StartsWith(@"\\?\", StringComparison.Ordinal)) return endpointId;
        var suffix = flow == EDataFlow.eRender ? RenderInterfaceSuffix : CaptureInterfaceSuffix;
        return $"{MmdevapiToken}{endpointId}{suffix}";
    }

    /// <summary>设备接口路径 → 端点 ID（Get 返回的是接口路径）。</summary>
    public static string? FromDeviceInterfaceId(string? deviceInterfaceId)
    {
        if (string.IsNullOrEmpty(deviceInterfaceId)) return deviceInterfaceId;
        var id = deviceInterfaceId;
        if (id.StartsWith(MmdevapiToken, StringComparison.Ordinal)) id = id[MmdevapiToken.Length..];
        if (id.EndsWith(RenderInterfaceSuffix, StringComparison.OrdinalIgnoreCase)) id = id[..^RenderInterfaceSuffix.Length];
        else if (id.EndsWith(CaptureInterfaceSuffix, StringComparison.OrdinalIgnoreCase)) id = id[..^CaptureInterfaceSuffix.Length];
        return id;
    }

    // 社区已验证的 IID（Win10 1709 / Win10 20H1-21H1 / Win11 21H2+）
    private static readonly Guid[] KnownInterfaceIds =
    [
        new("32aa8e18-6496-4e24-9f94-b800e7eccc45"),
        new("2a59116d-6c4f-45e0-a74f-707e3fef9258"),
        new("ab3d4648-e242-459f-b02f-541c70306324"),
    ];

    public const int ProcessNoAudio = unchecked((int)0x80070490); // HRESULT_FROM_WIN32(ERROR_NOT_FOUND)，表示该进程当前没有音频会话

    private IntPtr _factory;      // 激活工厂（IInspectable）
    private IntPtr _policy;       // QI 后的 AudioPolicyConfig 接口
    private readonly List<Guid> _iids = [];

    private AudioPolicyConfig(IntPtr factory, IntPtr policy, Guid interfaceId, IReadOnlyList<Guid> iids)
    {
        _factory = factory;
        _policy = policy;
        InterfaceId = interfaceId;
        _iids.AddRange(iids);
    }

    public Guid InterfaceId { get; }

    public IReadOnlyList<Guid> InterfaceIds => _iids;

    public static IReadOnlyList<Guid> KnownIds => KnownInterfaceIds;

    /// <summary>尝试打开工厂并 QI 到策略接口。失败返回 null（不抛异常）。</summary>
    public static AudioPolicyConfig? TryCreate(Action<string>? log = null)
    {
        using var classId = HString.FromString(ActivatableClassId);
        var iid = Guids.IID_IInspectable;
        var hr = NativeMethods.RoGetActivationFactory(classId.DangerousGetHandle(), ref iid, out var factory);
        if (hr < 0)
        {
            log?.Invoke($"RoGetActivationFactory failed: 0x{hr:X8}");
            return null;
        }

        var iids = ReadIids(factory);
        log?.Invoke($"factory IIDs: {string.Join(", ", iids.Select(g => g.ToString("D")))}");

        var target = iids.FirstOrDefault(g => KnownInterfaceIds.Contains(g));
        if (target == Guid.Empty)
        {
            log?.Invoke("no known AudioPolicyConfig IID found (unverified OS build)");
            return null;
        }

        hr = QueryInterface(factory, ref target, out var policy);
        if (hr < 0)
        {
            log?.Invoke($"QI {target:D} failed: 0x{hr:X8}");
            return null;
        }

        return new AudioPolicyConfig(factory, policy, target, iids);
    }

    private static IReadOnlyList<Guid> ReadIids(IntPtr factory)
    {
        var list = new List<Guid>();
        if (factory == IntPtr.Zero) return list;

        var vtbl = Marshal.ReadIntPtr(factory);
        var getIidsPtr = Marshal.ReadIntPtr(vtbl, 3 * IntPtr.Size);
        var getIids = Marshal.GetDelegateForFunctionPointer<GetIidsDelegate>(getIidsPtr);

        var hr = getIids(factory, out var count, out var iidsPtr);
        if (hr < 0 || iidsPtr == IntPtr.Zero) return list;

        for (var i = 0; i < count; i++)
        {
            list.Add(Marshal.PtrToStructure<Guid>(iidsPtr + (i * 16)));
        }

        NativeMethods.CoTaskMemFree(iidsPtr);
        return list;
    }

    private static int QueryInterface(IntPtr obj, ref Guid iid, out IntPtr result)
    {
        var vtbl = Marshal.ReadIntPtr(obj);
        var fn = Marshal.ReadIntPtr(vtbl, 0);
        var qi = Marshal.GetDelegateForFunctionPointer<QueryInterfaceDelegate>(fn);
        return qi(obj, ref iid, out result);
    }

    /// <summary>
    /// 在候选虚表槽位中探测 Get/Set 组合。测试对象应为「当前确实有音频播放」的进程，
    /// 以提高判定可靠性。成功返回 true 并固定槽位。
    /// </summary>
    public bool TryDetectSlots(uint probeProcessId, ERole role = ERole.eMultimedia, Action<string>? log = null)
    {
        // 已知布局：Get=26, Set=25, Clear=27。先试它，再试相邻组合。
        var candidates = new (int Get, int Set)[]
        {
            (26, 25), (25, 26), (24, 25), (27, 26),
        };

        foreach (var (getSlot, setSlot) in candidates)
        {
            var hr = InvokeGet(getSlot, probeProcessId, EDataFlow.eRender, role, out var deviceId);
            if (hr == HResults.S_OK)
            {
                if (!LooksLikeEndpointId(deviceId))
                {
                    log?.Invoke($"slot {getSlot}: returned non-endpoint string '{deviceId}', skipped");
                    continue;
                }

                GetSlot = getSlot;
                SetSlot = setSlot;
                ClearSlot = setSlot + 2;
                log?.Invoke($"detected slots: get={getSlot} set={setSlot} (deviceId='{deviceId}')");
                return true;
            }

            if (hr == ProcessNoAudio)
            {
                GetSlot = getSlot;
                SetSlot = setSlot;
                ClearSlot = setSlot + 2;
                log?.Invoke($"detected slots: get={getSlot} set={setSlot} (no audio session on probe pid)");
                return true;
            }

            log?.Invoke($"slot {getSlot}: hr=0x{hr:X8}");
        }

        return false;
    }

    public int GetSlot { get; private set; } = 26;

    public int SetSlot { get; private set; } = 25;

    public int ClearSlot { get; private set; } = 27;

    private static bool LooksLikeEndpointId(string? id) =>
        string.IsNullOrEmpty(id) || (id.StartsWith('{') && id.Contains("}.{", StringComparison.Ordinal));

    public int Get(uint processId, EDataFlow flow, ERole role, out string? deviceId) =>
        InvokeGet(GetSlot, processId, flow, role, out deviceId);

    public int Set(uint processId, EDataFlow flow, ERole role, string? deviceId)
    {
        // 传 null/空 = 清除覆盖；否则必须转换成设备接口路径
        var value = string.IsNullOrEmpty(deviceId) ? null : ToDeviceInterfaceId(deviceId!, flow);
        using var hstr = HString.FromString(value ?? string.Empty);
        return InvokeSet(SetSlot, processId, flow, role, hstr.DangerousGetHandle());
    }

    public int ClearAll() => InvokeClear(ClearSlot);

    private int InvokeGet(int slot, uint pid, EDataFlow flow, ERole role, out string? deviceId)
    {
        deviceId = null;
        if (_policy == IntPtr.Zero) return HResults.E_FAIL;

        var vtbl = Marshal.ReadIntPtr(_policy);
        var fn = Marshal.ReadIntPtr(vtbl, slot * IntPtr.Size);
        var del = Marshal.GetDelegateForFunctionPointer<GetPersistedDefaultAudioEndpointDelegate>(fn);
        var hr = del(_policy, pid, (int)flow, (int)role, out var hstring);
        if (hr < 0) return hr;
        if (hstring != IntPtr.Zero)
        {
            // Get 返回设备接口路径，统一解包成端点 ID
            deviceId = FromDeviceInterfaceId(HString.ToString(hstring));
            NativeMethods.WindowsDeleteString(hstring);
        }

        return hr;
    }

    private int InvokeSet(int slot, uint pid, EDataFlow flow, ERole role, IntPtr deviceId)
    {
        if (_policy == IntPtr.Zero) return HResults.E_FAIL;
        var vtbl = Marshal.ReadIntPtr(_policy);
        var fn = Marshal.ReadIntPtr(vtbl, slot * IntPtr.Size);
        var del = Marshal.GetDelegateForFunctionPointer<SetPersistedDefaultAudioEndpointDelegate>(fn);
        return del(_policy, pid, (int)flow, (int)role, deviceId);
    }

    private int InvokeClear(int slot)
    {
        if (_policy == IntPtr.Zero) return HResults.E_FAIL;
        var vtbl = Marshal.ReadIntPtr(_policy);
        var fn = Marshal.ReadIntPtr(vtbl, slot * IntPtr.Size);
        var del = Marshal.GetDelegateForFunctionPointer<ClearDelegate>(fn);
        return del(_policy);
    }

    public void Dispose()
    {
        if (_policy != IntPtr.Zero)
        {
            _ = Marshal.Release(_policy);
            _policy = IntPtr.Zero;
        }

        if (_factory != IntPtr.Zero)
        {
            _ = Marshal.Release(_factory);
            _factory = IntPtr.Zero;
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetIidsDelegate(IntPtr self, out uint iidCount, out IntPtr iids);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int QueryInterfaceDelegate(IntPtr self, ref Guid iid, out IntPtr ppv);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetPersistedDefaultAudioEndpointDelegate(IntPtr self, uint processId, int flow, int role, out IntPtr deviceId);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetPersistedDefaultAudioEndpointDelegate(IntPtr self, uint processId, int flow, int role, IntPtr deviceId);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int ClearDelegate(IntPtr self);
}
