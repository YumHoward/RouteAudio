using RouteAudio.Core.Models;

namespace RouteAudio.Core.Abstractions;

/// <summary>跨端点的一条音频会话（不含 COM 对象）。</summary>
public sealed record SessionInfo
{
    public required string EndpointId { get; init; }
    public required string EndpointName { get; init; }
    public required uint ProcessId { get; init; }
    public string? ProcessName { get; init; }
    public string? ProcessPath { get; init; }
    public string? DisplayName { get; init; }
    public bool IsSystemSounds { get; init; }
    public float Volume { get; init; } = 1f;
    public bool Muted { get; init; }
    public bool IsActive { get; init; } = true;
}

public interface IDeviceCatalog
{
    IReadOnlyList<DeviceDescriptor> GetDevices();

    /// <summary>设置设备端点主音量（0..1）。</summary>
    bool SetVolume(string deviceId, double volume);

    /// <summary>设置设备静音。</summary>
    bool SetMute(string deviceId, bool muted);
}

public interface IAppCatalog
{
    /// <summary>解析进程的应用身份（路径、进程名、版本信息）。失败时退化为进程名。</summary>
    AppKey Resolve(uint processId);
}

public interface ISessionCatalog
{
    IReadOnlyList<SessionInfo> GetSessions();

    /// <summary>对某进程的全部会话设置静音（跨端点）。返回生效的会话数。</summary>
    int SetMute(uint processId, bool muted);

    int SetVolume(uint processId, float level);
}

public sealed record RouteRequest(AppKey App, uint ProcessId, string DeviceId, AudioRole Role, RouteMode Mode);

public sealed record RouteResult(bool Ok, RouteReason Reason, string? Message, BackendKind Backend)
{
    public static RouteResult Success(BackendKind backend) => new(true, RouteReason.None, null, backend);

    public static RouteResult Failure(RouteReason reason, string? message = null, BackendKind backend = BackendKind.None)
        => new(false, reason, message, backend);
}

public interface IRoutingBackend
{
    string Id { get; }

    BackendKind Kind { get; }

    bool IsAvailable { get; }

    string StatusText { get; }

    Task<RouteResult> ApplyAsync(RouteRequest request, CancellationToken cancellationToken);

    Task<RouteResult> RemoveAsync(uint processId, AppKey app, CancellationToken cancellationToken);
}

public interface IAuditLog
{
    void Info(string message);

    void Warn(string message);

    void Error(string message, Exception? exception = null);
}

public sealed class NullAuditLog : IAuditLog
{
    public static readonly NullAuditLog Instance = new();

    public void Info(string message)
    {
    }

    public void Warn(string message)
    {
    }

    public void Error(string message, Exception? exception = null)
    {
    }
}
