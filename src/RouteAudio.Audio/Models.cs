using RouteAudio.Interop;

namespace RouteAudio.Audio;

public sealed record DeviceEndpointInfo
{
    public required string Id { get; init; }
    public required string FriendlyName { get; init; }
    public string? Description { get; init; }
    public string? InterfaceName { get; init; }
    public string? EnumeratorName { get; init; }
    public uint FormFactor { get; init; }
    public Guid? ContainerId { get; init; }
    public DeviceState State { get; init; }
    public bool IsDefaultConsole { get; init; }
    public bool IsDefaultMultimedia { get; init; }
    public bool IsDefaultCommunications { get; init; }
    public string? Alias { get; init; }

    public bool IsDefaultAny => IsDefaultConsole || IsDefaultMultimedia || IsDefaultCommunications;

    /// <summary>启发式判断是否为虚拟/软件端点（用于 UI 标记）。</summary>
    public bool LooksVirtual
    {
        get
        {
            if (string.Equals(EnumeratorName, "SWD", StringComparison.OrdinalIgnoreCase)) return true;
            var name = FriendlyName ?? string.Empty;
            return name.Contains("virtual", StringComparison.OrdinalIgnoreCase)
                || name.Contains("虚拟", StringComparison.Ordinal)
                || name.Contains("cable", StringComparison.OrdinalIgnoreCase)
                || name.Contains("broadcast", StringComparison.OrdinalIgnoreCase)
                || name.Contains("voicemeeter", StringComparison.OrdinalIgnoreCase);
        }
    }

    public string DisplayName => string.IsNullOrWhiteSpace(Alias) ? FriendlyName : Alias!;

    public override string ToString() => $"{FriendlyName} [{ShortId}]{(IsDefaultAny ? " (默认)" : "")}";

    private string ShortId => Id.Length > 12 ? Id[^12..] : Id;
}

public sealed record AudioSessionInfo
{
    public required string EndpointId { get; init; }
    public required string EndpointName { get; init; }
    public required uint ProcessId { get; init; }
    public string? ProcessName { get; init; }
    public string? ProcessPath { get; init; }
    public string? SessionIdentifier { get; init; }
    public string? InstanceIdentifier { get; init; }
    public string? DisplayName { get; init; }
    public AudioSessionState State { get; init; }
    public bool IsSystemSounds { get; init; }
    public float Volume { get; init; }
    public bool Muted { get; init; }

    public override string ToString() =>
        $"{ProcessName ?? "?"}({ProcessId}) @ {(EndpointName.Length > 28 ? EndpointName[..28] : EndpointName)} v={Volume:P0}{(Muted ? " MUTE" : "")}";
}
