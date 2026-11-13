using System.Text.Json.Serialization;
using RouteAudio.Core.Models;

namespace RouteAudio.Core.Rules;

/// <summary>规则匹配上下文。</summary>
public sealed record AppMatchContext(AppKey App)
{
    public string? Aumid { get; init; }
    public string? WindowTitle { get; init; }
    public string? ParentProcessName { get; init; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(AllNode), "all")]
[JsonDerivedType(typeof(AnyNode), "any")]
[JsonDerivedType(typeof(PathGlobNode), "path")]
[JsonDerivedType(typeof(ProcessNameGlobNode), "processName")]
[JsonDerivedType(typeof(SignerContainsNode), "signer")]
[JsonDerivedType(typeof(ProductContainsNode), "product")]
[JsonDerivedType(typeof(AumidGlobNode), "aumid")]
[JsonDerivedType(typeof(ParentProcessGlobNode), "parent")]
public abstract record MatchNode
{
    [JsonIgnore]
    public abstract int ConditionCount { get; }

    public abstract bool Matches(in AppMatchContext context);
}

public sealed record AllNode : MatchNode
{
    public List<MatchNode> Children { get; init; } = [];

    public override int ConditionCount => Children.Sum(c => c.ConditionCount);

    public override bool Matches(in AppMatchContext context)
    {
        var ctx = context;
        return Children.All(c => c.Matches(in ctx));
    }
}

public sealed record AnyNode : MatchNode
{
    public List<MatchNode> Children { get; init; } = [];

    public override int ConditionCount => Children.Sum(c => c.ConditionCount);

    public override bool Matches(in AppMatchContext context)
    {
        var ctx = context;
        return Children.Any(c => c.Matches(in ctx));
    }
}

public sealed record PathGlobNode : MatchNode
{
    public string Pattern { get; init; } = "*";

    public override int ConditionCount => 1;

    public override bool Matches(in AppMatchContext context) =>
        GlobMatcher.IsMatch(context.App.ExecutablePath, Pattern);
}

public sealed record ProcessNameGlobNode : MatchNode
{
    public string Pattern { get; init; } = "*";

    public override int ConditionCount => 1;

    public override bool Matches(in AppMatchContext context) =>
        GlobMatcher.IsMatch(context.App.ProcessName, Pattern);
}

public sealed record SignerContainsNode : MatchNode
{
    public string Text { get; init; } = "";

    public override int ConditionCount => 1;

    public override bool Matches(in AppMatchContext context) =>
        !string.IsNullOrEmpty(context.App.Signer) &&
        context.App.Signer!.Contains(Text, StringComparison.OrdinalIgnoreCase);
}

public sealed record ProductContainsNode : MatchNode
{
    public string Text { get; init; } = "";

    public override int ConditionCount => 1;

    public override bool Matches(in AppMatchContext context) =>
        !string.IsNullOrEmpty(context.App.ProductName) &&
        context.App.ProductName!.Contains(Text, StringComparison.OrdinalIgnoreCase);
}

public sealed record AumidGlobNode : MatchNode
{
    public string Pattern { get; init; } = "*";

    public override int ConditionCount => 1;

    public override bool Matches(in AppMatchContext context) =>
        !string.IsNullOrEmpty(context.Aumid) && GlobMatcher.IsMatch(context.Aumid!, Pattern);
}

public sealed record ParentProcessGlobNode : MatchNode
{
    public string Pattern { get; init; } = "*";

    public override int ConditionCount => 1;

    public override bool Matches(in AppMatchContext context) =>
        !string.IsNullOrEmpty(context.ParentProcessName) && GlobMatcher.IsMatch(context.ParentProcessName!, Pattern);
}
