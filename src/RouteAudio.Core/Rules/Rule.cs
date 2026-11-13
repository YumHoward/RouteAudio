using System.Text.Json.Serialization;
using RouteAudio.Core.Models;

namespace RouteAudio.Core.Rules;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(RouteToDeviceAction), "routeToDevice")]
[JsonDerivedType(typeof(RouteToRoleAction), "routeToRole")]
[JsonDerivedType(typeof(FollowDefaultAction), "followDefault")]
[JsonDerivedType(typeof(SetGainAction), "setGain")]
[JsonDerivedType(typeof(MuteAction), "mute")]
public abstract record RuleAction;

public sealed record RouteToDeviceAction : RuleAction
{
    public string DeviceId { get; init; } = "";
    public AudioRole Role { get; init; } = AudioRole.Console;
}

public sealed record RouteToRoleAction : RuleAction
{
    public AudioRole Role { get; init; } = AudioRole.Console;
}

public sealed record FollowDefaultAction : RuleAction;

public sealed record SetGainAction : RuleAction
{
    public double Db { get; init; }
}

public sealed record MuteAction : RuleAction
{
    public bool Mute { get; init; } = true;
}

public sealed class Rule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n");

    public string Name { get; set; } = "";

    public int Priority { get; set; }

    public bool Enabled { get; set; } = true;

    public MatchNode Match { get; set; } = new AnyNode();

    public List<RuleAction> Actions { get; set; } = [];
}

public sealed record RuleMatchResult(Rule Rule, IReadOnlyList<RuleAction> Actions, int MatchedConditions);

public static class RuleEvaluator
{
    /// <summary>
    /// 按「优先级降序 → 条件数降序 → 配置顺序」求值，首个命中即返回。
    /// 不做多规则合并，保证结果可预测、可解释。
    /// </summary>
    public static RuleMatchResult? Evaluate(IEnumerable<Rule> rules, in AppMatchContext context)
    {
        var ordered = rules
            .Where(r => r.Enabled)
            .Select((rule, index) => (rule, index))
            .OrderByDescending(x => x.rule.Priority)
            .ThenByDescending(x => x.rule.Match.ConditionCount)
            .ThenBy(x => x.index);

        foreach (var (rule, _) in ordered)
        {
            if (!rule.Match.Matches(context)) continue;
            return new RuleMatchResult(rule, rule.Actions, rule.Match.ConditionCount);
        }

        return null;
    }
}
