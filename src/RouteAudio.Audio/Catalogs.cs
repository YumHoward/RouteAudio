using System.Diagnostics;
using RouteAudio.Core.Abstractions;
using RouteAudio.Core.Models;

namespace RouteAudio.Audio;

/// <summary>把 Windows 的进程信息映射成 Core 的 AppKey。</summary>
public sealed class AppCatalog : IAppCatalog
{
    public AppKey Resolve(uint processId)
    {
        var (name, path) = ProcessInfoHelper.GetProcessInfo(processId);

        if (string.IsNullOrEmpty(path))
        {
            var fallbackName = string.IsNullOrEmpty(name) ? $"pid{processId}" : name!;
            return AppKey.Create(fallbackName + ".exe", fallbackName);
        }

        string? product = null;
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            product = string.IsNullOrWhiteSpace(info.ProductName) ? null : info.ProductName;
        }
        catch
        {
            // 读不到版本信息就退化为进程名
        }

        return AppKey.Create(path, name, productName: product);
    }
}

public sealed class WindowsDeviceCatalog : IDeviceCatalog, IDisposable
{
    private readonly DeviceService _devices;

    public WindowsDeviceCatalog(DeviceService devices) => _devices = devices;

    public IReadOnlyList<DeviceDescriptor> GetDevices()
    {
        return _devices.GetRenderEndpoints()
            .Select(d => new DeviceDescriptor
            {
                Id = d.Id,
                Name = d.FriendlyName,
                EnumeratorName = d.EnumeratorName,
                ContainerId = d.ContainerId?.ToString("D"),
                IsDefaultConsole = d.IsDefaultConsole,
                IsDefaultMultimedia = d.IsDefaultMultimedia,
                IsDefaultCommunications = d.IsDefaultCommunications,
                IsVirtual = d.LooksVirtual,
                IsActive = d.State == Interop.DeviceState.ACTIVE,
            })
            .ToList();
    }

    public void Dispose()
    {
    }
}

public sealed class WindowsSessionCatalog : ISessionCatalog
{
    private readonly SessionService _sessions;

    public WindowsSessionCatalog(SessionService sessions) => _sessions = sessions;

    public IReadOnlyList<SessionInfo> GetSessions()
    {
        return _sessions.GetAllSessions()
            .Select(s => new SessionInfo
            {
                EndpointId = s.EndpointId,
                EndpointName = s.EndpointName,
                ProcessId = s.ProcessId,
                ProcessName = s.ProcessName,
                ProcessPath = s.ProcessPath,
                DisplayName = s.DisplayName,
                IsSystemSounds = s.IsSystemSounds,
                Volume = s.Volume,
                Muted = s.Muted,
                IsActive = s.State == Interop.AudioSessionState.Active,
            })
            .ToList();
    }

    public int SetMute(uint processId, bool muted) => _sessions.SetMuteForProcess(processId, muted);

    public int SetVolume(uint processId, float level) => _sessions.SetVolumeForProcess(processId, level);
}

/// <summary>检测系统里是否存在虚拟音频端点（VB-CABLE / VoiceMeeter / 其他）。</summary>
public static class VirtualCableDetector
{
    public static IReadOnlyList<DeviceDescriptor> FindVirtualEndpoints(IEnumerable<DeviceDescriptor> devices) =>
        devices.Where(d => d.IsVirtual).ToList();

    public static bool HasVirtualEndpoint(IEnumerable<DeviceDescriptor> devices) =>
        devices.Any(d => d.IsVirtual);
}
