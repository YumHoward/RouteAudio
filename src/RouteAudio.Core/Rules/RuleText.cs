using RouteAudio.Core.Models;

namespace RouteAudio.Core.Rules;

/// <summary>把规则翻译成人类可读的摘要（用于列表展示与命中解释）。</summary>
public static class RuleText
{
    public static string DescribeMatch(MatchNode node) => node switch
    {
        AllNode all => string.Join(" 且 ", all.Children.Select(DescribeMatch)),
        AnyNode any => string.Join(" 或 ", any.Children.Select(DescribeMatch)),
        PathGlobNode path => $"路径 ≈ {path.Pattern}",
        ProcessNameGlobNode name => $"进程名 ≈ {name.Pattern}",
        SignerContainsNode signer => $"签名者含「{signer.Text}」",
        ProductContainsNode product => $"产品名含「{product.Text}」",
        AumidGlobNode aumid => $"AUMID ≈ {aumid.Pattern}",
        ParentProcessGlobNode parent => $"父进程 ≈ {parent.Pattern}",
        _ => "（未知条件）",
    };

    public static string DescribeAction(RuleAction action, Func<string, string?>? resolveDeviceName = null)
    {
        switch (action)
        {
            case RouteToDeviceAction toDevice:
            {
                var name = resolveDeviceName?.Invoke(toDevice.DeviceId) ?? toDevice.DeviceId;
                return $"路由到「{name}」（{RoleText(toDevice.Role)}）";
            }

            case RouteToRoleAction toRole:
                return $"路由到{RoleText(toRole.Role)}默认设备";

            case FollowDefaultAction:
                return "跟随系统默认设备";

            case SetGainAction gain:
                return $"增益 {gain.Db:+0.0;-0.0;0} dB";

            case MuteAction mute:
                return mute.Mute ? "静音" : "取消静音";

            default:
                return "（未知动作）";
        }
    }

    public static string DescribeActions(IReadOnlyList<RuleAction> actions, Func<string, string?>? resolveDeviceName = null) =>
        actions.Count == 0 ? "（无动作）" : string.Join("；", actions.Select(a => DescribeAction(a, resolveDeviceName)));

    public static string RoleText(AudioRole role) => role switch
    {
        AudioRole.Communications => "通信设备",
        AudioRole.Multimedia => "多媒体设备",
        _ => "主设备",
    };
}
