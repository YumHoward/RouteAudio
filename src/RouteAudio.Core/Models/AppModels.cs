using System.Text.Json.Serialization;

namespace RouteAudio.Core.Models;

/// <summary>输出设备（应用层视图，不含 COM 细节）。</summary>
public sealed record DeviceDescriptor
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Alias { get; init; }
    public string? EnumeratorName { get; init; }
    public string? ContainerId { get; init; }
    public bool IsDefaultConsole { get; init; }
    public bool IsDefaultMultimedia { get; init; }
    public bool IsDefaultCommunications { get; init; }
    public bool IsVirtual { get; init; }
    public bool IsActive { get; init; } = true;

    /// <summary>设备主音量 0..1（系统端点音量，不是某个应用）。</summary>
    public double Volume { get; init; } = 1;

    /// <summary>设备是否被静音。</summary>
    public bool Muted { get; init; }

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Alias) ? Name : Alias!;

    [JsonIgnore]
    public bool IsDefaultAny => IsDefaultConsole || IsDefaultMultimedia || IsDefaultCommunications;
}

/// <summary>应用身份：用于持久化匹配与展示。</summary>
public sealed record AppKey
{
    public required string ExecutablePath { get; init; }
    public required string ProcessName { get; init; }
    public string? Signer { get; init; }
    public string? ProductName { get; init; }

    [JsonIgnore]
    public string Id => $"{ProcessName.ToLowerInvariant()}|{ExecutablePath.ToLowerInvariant()}";

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(ProductName) ? ProcessName : ProductName!;

    public static AppKey Create(string executablePath, string? processName = null, string? signer = null, string? productName = null)
        => new()
        {
            ExecutablePath = executablePath,
            ProcessName = processName ?? Path.GetFileNameWithoutExtension(executablePath),
            Signer = signer,
            ProductName = productName,
        };
}

public sealed record RouteTarget
{
    public string? DeviceId { get; init; }
    public AudioRole Role { get; init; } = AudioRole.Console;

    public static RouteTarget ToDevice(string deviceId, AudioRole role = AudioRole.Console) => new() { DeviceId = deviceId, Role = role };

    public static RouteTarget ToRole(AudioRole role) => new() { DeviceId = null, Role = role };

    [JsonIgnore]
    public bool IsRoleTarget => string.IsNullOrEmpty(DeviceId);
}

public sealed class RouteEntry
{
    public AppKey App { get; set; } = AppKey.Create("unknown");

    public RouteTarget Target { get; set; } = RouteTarget.ToRole(AudioRole.Console);

    public RouteMode Mode { get; set; } = RouteMode.Auto;

    public double GainDb { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>同时输出到第二设备（镜像，走音频管线）。v1.1。</summary>
    public bool Mirror { get; set; }

    [JsonIgnore]
    public bool IsRoleTarget => Target.IsRoleTarget;
}

/// <summary>一次路由的实时状态（不持久化）。</summary>
public sealed record RouteStatus
{
    public required AppKey App { get; init; }
    public RouteState State { get; init; } = RouteState.NotConfigured;
    public RouteReason Reason { get; init; } = RouteReason.None;
    public string? DesiredDeviceId { get; init; }
    public string? DesiredDeviceName { get; init; }
    public string? ActualDeviceId { get; init; }
    public string? ActualDeviceName { get; init; }
    public BackendKind Backend { get; init; } = BackendKind.None;
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;
    public string? Message { get; init; }

    [JsonIgnore]
    public bool IsHealthy => State is RouteState.ActivePolicy or RouteState.ActiveStreaming or RouteState.Pending or RouteState.AppliedPendingSession;

    [JsonIgnore]
    public string StateText => State.ToText();

    public static RouteStatus For(AppKey app, RouteState state, RouteReason reason = RouteReason.None) =>
        new() { App = app, State = state, Reason = reason };
}

/// <summary>运行中的应用及其当前输出（UI 数据源）。</summary>
public sealed record AppRuntimeView
{
    public required AppKey App { get; init; }
    public required uint ProcessId { get; init; }
    public string? CurrentDeviceId { get; init; }
    public string? CurrentDeviceName { get; init; }
    public bool HasAudio { get; init; }
    public float Volume { get; init; } = 1f;
    public bool Muted { get; init; }
    public RouteStatus? Status { get; init; }
}
