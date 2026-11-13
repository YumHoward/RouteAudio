using RouteAudio.Core.Abstractions;
using RouteAudio.Core.Models;
using RouteAudio.Interop;

namespace RouteAudio.Audio;

/// <summary>
/// 主线后端：通过系统内部策略接口把「进程 → 端点」写进系统，由音频引擎在新会话创建时执行。
/// 零延迟、零音质损失、不需要常驻音频线程。
/// </summary>
public sealed class PolicyRoutingBackend : IRoutingBackend, IDisposable
{
    private readonly AudioPolicyConfig? _policy;
    private readonly IAuditLog _log;

    public PolicyRoutingBackend(IAuditLog? log = null)
    {
        ComThread.EnsureInitialized();
        _log = log ?? NullAuditLog.Instance;
        _policy = AudioPolicyConfig.TryCreate(message => _log.Info($"[policy] {message}"));
        if (_policy is not null)
        {
            _log.Info($"[policy] 接口就绪 IID={_policy.InterfaceId:D} set={_policy.SetSlot} get={_policy.GetSlot}");
        }
        else
        {
            _log.Warn("[policy] 策略接口不可用（未知系统版本或接口变更）");
        }
    }

    public string Id => "policy";

    public BackendKind Kind => BackendKind.Policy;

    public bool IsAvailable => _policy is not null;

    public string StatusText => _policy is null
        ? "策略接口不可用（将无法路由）"
        : "策略接口就绪";

    public Task<RouteResult> ApplyAsync(RouteRequest request, CancellationToken cancellationToken) =>
        Task.Run(() => ApplyCore(request), cancellationToken);

    public Task<RouteResult> RemoveAsync(uint processId, AppKey app, CancellationToken cancellationToken) =>
        Task.Run(() => RemoveCore(processId), cancellationToken);

    private RouteResult ApplyCore(RouteRequest request)
    {
        ComThread.EnsureInitialized();
        if (_policy is null) return RouteResult.Failure(RouteReason.PolicyApiUnavailable, "AudioPolicyConfig 不可用");

        var roles = RolesFor(request.Role);

        foreach (var role in roles)
        {
            var currentHr = _policy.Get(request.ProcessId, EDataFlow.eRender, role, out var current);
            if (currentHr >= 0 && string.Equals(current, request.DeviceId, StringComparison.OrdinalIgnoreCase))
            {
                continue; // 幂等：已经是目标端点
            }

            var setHr = _policy.Set(request.ProcessId, EDataFlow.eRender, role, request.DeviceId);
            if (setHr == AudioPolicyConfig.ProcessNoAudio)
            {
                return RouteResult.Failure(RouteReason.NoAudioSession, "该进程当前没有音频会话");
            }

            if (setHr < 0)
            {
                _log.Warn($"[policy] Set pid={request.ProcessId} role={role} -> 0x{setHr:X8}");
                return RouteResult.Failure(MapHResult(setHr), $"SetPersistedDefaultAudioEndpoint 0x{setHr:X8}");
            }
        }

        var primary = roles[0];
        var verifyHr = _policy.Get(request.ProcessId, EDataFlow.eRender, primary, out var verified);
        if (verifyHr < 0 || !string.Equals(verified, request.DeviceId, StringComparison.OrdinalIgnoreCase))
        {
            return RouteResult.Failure(RouteReason.OverriddenExternally, "写入后回读不一致（可能被系统或其他工具改写）");
        }

        return RouteResult.Success(BackendKind.Policy);
    }

    private RouteResult RemoveCore(uint processId)
    {
        ComThread.EnsureInitialized();
        if (_policy is null) return RouteResult.Success(BackendKind.Policy);

        foreach (var role in new[] { ERole.eConsole, ERole.eMultimedia, ERole.eCommunications })
        {
            var _ = _policy.Set(processId, EDataFlow.eRender, role, null);
        }

        return RouteResult.Success(BackendKind.Policy);
    }

    /// <summary>
    /// 普通应用同时写 Console 与 Multimedia 两个角色，覆盖「应用自己按角色取默认设备」的差异；
    /// 通信类应用只写 Communications，避免影响其媒体播放。
    /// </summary>
    private static ERole[] RolesFor(AudioRole role) => role switch
    {
        AudioRole.Communications => [ERole.eCommunications],
        AudioRole.Multimedia => [ERole.eMultimedia, ERole.eConsole],
        _ => [ERole.eConsole, ERole.eMultimedia],
    };

    private static RouteReason MapHResult(int hr) => hr switch
    {
        HResults.E_INVALIDARG => RouteReason.NoAudioSession,
        HResults.E_ACCESSDENIED => RouteReason.AccessDenied,
        HResults.E_POINTER => RouteReason.Unknown,
        _ => RouteReason.Unknown,
    };

    public void Dispose() => _policy?.Dispose();
}
