using RouteAudio.Core.Models;
using RouteAudio.Core.Rules;
using Xunit;

namespace RouteAudio.Core.Tests;

public class RuleEvaluatorTests
{
    private static AppKey Chrome { get; } = AppKey.Create(@"C:\Program Files\Google\Chrome\chrome.exe", "chrome", productName: "Google Chrome");

    private static AppMatchContext ContextFor(AppKey app) => new(app);

    [Fact]
    public void HigherPriorityWins()
    {
        var low = new Rule
        {
            Name = "low",
            Priority = 1,
            Match = new ProcessNameGlobNode { Pattern = "chrome" },
            Actions = [new RouteToDeviceAction { DeviceId = "low" }],
        };

        var high = new Rule
        {
            Name = "high",
            Priority = 100,
            Match = new ProcessNameGlobNode { Pattern = "chrome" },
            Actions = [new RouteToDeviceAction { DeviceId = "high" }],
        };

        var result = RuleEvaluator.Evaluate([low, high], ContextFor(Chrome));

        Assert.NotNull(result);
        Assert.Equal("high", result!.Rule.Name);
        Assert.Equal("high", ((RouteToDeviceAction)result.Actions[0]).DeviceId);
    }

    [Fact]
    public void MoreSpecificWinsAtSamePriority()
    {
        var generic = new Rule
        {
            Name = "generic",
            Priority = 10,
            Match = new ProcessNameGlobNode { Pattern = "chrome" },
            Actions = [new RouteToDeviceAction { DeviceId = "generic" }],
        };

        var specific = new Rule
        {
            Name = "specific",
            Priority = 10,
            Match = new AllNode
            {
                Children =
                [
                    new ProcessNameGlobNode { Pattern = "chrome" },
                    new PathGlobNode { Pattern = @"C:\Program Files\**" },
                ],
            },
            Actions = [new RouteToDeviceAction { DeviceId = "specific" }],
        };

        var result = RuleEvaluator.Evaluate([generic, specific], ContextFor(Chrome));

        Assert.NotNull(result);
        Assert.Equal("specific", result!.Rule.Name);
    }

    [Fact]
    public void DisabledRulesAreSkipped()
    {
        var rule = new Rule
        {
            Name = "disabled",
            Priority = 100,
            Enabled = false,
            Match = new ProcessNameGlobNode { Pattern = "*" },
            Actions = [new RouteToDeviceAction { DeviceId = "x" }],
        };

        Assert.Null(RuleEvaluator.Evaluate([rule], ContextFor(Chrome)));
    }

    [Fact]
    public void SignerNodeMatchesCaseInsensitively()
    {
        var app = Chrome with { Signer = "Google LLC" };
        var rule = new Rule
        {
            Name = "signer",
            Match = new SignerContainsNode { Text = "google" },
            Actions = [new RouteToRoleAction { Role = AudioRole.Communications }],
        };

        var result = RuleEvaluator.Evaluate([rule], ContextFor(app));

        Assert.NotNull(result);
    }

    [Fact]
    public void UnmatchedRuleReturnsNull()
    {
        var rule = new Rule
        {
            Name = "other",
            Match = new ProcessNameGlobNode { Pattern = "steam" },
            Actions = [new RouteToDeviceAction { DeviceId = "x" }],
        };

        Assert.Null(RuleEvaluator.Evaluate([rule], ContextFor(Chrome)));
    }
}
