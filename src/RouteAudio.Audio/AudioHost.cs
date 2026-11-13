using RouteAudio.Core.Abstractions;
using RouteAudio.Core.Config;
using RouteAudio.Core.Routing;

namespace RouteAudio.Audio;

/// <summary>
/// 组合根：把设备/会话/应用目录与策略后端装配成可用的路由服务。
/// v1.0 单进程，UI 与音频在同一进程内；v2 可整体替换为 IPC 客户端。
/// </summary>
public sealed class AudioHost : IDisposable
{
    private readonly DeviceService _devices;
    private readonly SessionService _sessions;
    private readonly PolicyRoutingBackend _backend;

    public AudioHost(ConfigStore store, IAuditLog log)
    {
        Log = log;
        _devices = new DeviceService();
        _sessions = new SessionService(_devices);
        _backend = new PolicyRoutingBackend(log);

        Devices = new WindowsDeviceCatalog(_devices);
        Sessions = new WindowsSessionCatalog(_sessions);
        Apps = new AppCatalog();
        Routing = new RoutingService(Devices, Sessions, Apps, _backend, store, log);
    }

    public IAuditLog Log { get; }

    public WindowsDeviceCatalog Devices { get; }

    public WindowsSessionCatalog Sessions { get; }

    public AppCatalog Apps { get; }

    public PolicyRoutingBackend Backend => _backend;

    public RoutingService Routing { get; }

    public bool HasVirtualCable => VirtualCableDetector.HasVirtualEndpoint(Devices.GetDevices());

    public void Dispose()
    {
        Routing.Dispose();
        _backend.Dispose();
        _devices.Dispose();
    }
}
