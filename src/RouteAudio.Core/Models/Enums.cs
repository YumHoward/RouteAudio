namespace RouteAudio.Core.Models;

/// <summary>输出设备角色（与 WASAPI 的 ERole 同序）。</summary>
public enum AudioRole
{
    Console = 0,
    Multimedia = 1,
    Communications = 2,
}

public enum RouteMode
{
    /// <summary>优先策略路由，失败则按镜像/虚拟声卡能力降级。</summary>
    Auto = 0,

    /// <summary>只允许写系统策略，不做音频处理。</summary>
    PolicyOnly = 1,

    /// <summary>不走策略，用音频管线接管（镜像/虚拟声卡）。</summary>
    StreamingOnly = 2,
}

public enum RouteState
{
    NotConfigured = 0,

    /// <summary>应用没有运行，或还没有产生音频会话。</summary>
    Pending = 1,

    /// <summary>策略已写入，但当前正在播放的会话尚未迁移（需要应用重新建会话）。</summary>
    AppliedPendingSession = 2,

    /// <summary>策略已生效，音频正在目标设备上。</summary>
    ActivePolicy = 3,

    /// <summary>音频管线（镜像/虚拟声卡）正在运行。</summary>
    ActiveStreaming = 4,

    /// <summary>目标设备不可用，已回退到默认设备。</summary>
    Degraded = 5,

    Failed = 6,
}

public enum RouteReason
{
    None = 0,
    NotRunning = 1,
    NoAudioSession = 2,
    PolicyApiUnavailable = 3,
    EndpointNotFound = 4,
    EndpointExclusive = 5,
    AppExclusiveMode = 6,
    ProtectedContent = 7,
    AccessDenied = 8,
    CaptureFailed = 9,
    FormatUnsupported = 10,
    OverriddenExternally = 11,
    BackendUnavailable = 12,
    Unsupported = 13,
    Unknown = 99,
}

public enum BackendKind
{
    None = 0,
    Policy = 1,
    ProcessLoopback = 2,
    VirtualCable = 3,
}

public static class RouteStateText
{
    public static string ToText(this RouteState state) => state switch
    {
        RouteState.NotConfigured => "未设置",
        RouteState.Pending => "等待应用出声",
        RouteState.AppliedPendingSession => "已设置（下次生效）",
        RouteState.ActivePolicy => "已生效",
        RouteState.ActiveStreaming => "管线运行中",
        RouteState.Degraded => "已回退",
        RouteState.Failed => "失败",
        _ => "未知",
    };

    public static string ToText(this RouteReason reason) => reason switch
    {
        RouteReason.None => "",
        RouteReason.NotRunning => "应用未运行",
        RouteReason.NoAudioSession => "应用当前没有音频会话",
        RouteReason.PolicyApiUnavailable => "当前系统不支持策略路由",
        RouteReason.EndpointNotFound => "目标设备已断开",
        RouteReason.EndpointExclusive => "目标设备被独占",
        RouteReason.AppExclusiveMode => "该应用使用了独占模式",
        RouteReason.ProtectedContent => "受保护内容无法采集",
        RouteReason.AccessDenied => "无法访问该进程",
        RouteReason.CaptureFailed => "音频采集失败",
        RouteReason.FormatUnsupported => "设备不支持所需音频格式",
        RouteReason.OverriddenExternally => "设置被外部改写",
        RouteReason.BackendUnavailable => "音频后端不可用",
        RouteReason.Unsupported => "系统不支持此操作",
        _ => "未知错误",
    };
}
