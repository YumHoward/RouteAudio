using RouteAudio.Core.Rules;
using Xunit;

namespace RouteAudio.Core.Tests;

public class GlobMatcherTests
{
    [Theory]
    [InlineData(@"C:\Games\Cyberpunk\bin\x64\game.exe", @"C:\Games\**\*.exe", true)]
    [InlineData(@"C:\Games\Cyberpunk\game.exe", @"**\game.exe", true)]
    [InlineData(@"C:\Games\Cyberpunk\game.exe", @"C:\Games\*\game.exe", true)]
    [InlineData(@"C:\Games\Cyberpunk\bin\game.exe", @"C:\Games\*\game.exe", false)]
    [InlineData(@"C:\apps\spotify.exe", @"spotify.exe", false)]
    [InlineData(@"C:\apps\spotify.exe", @"*spotify*", true)]
    [InlineData(@"C:\apps\spotify.exe", @"*", true)]
    [InlineData(@"C:\apps\a.exe", @"?.exe", false)]
    [InlineData(@"C:\apps\ab.exe", @"*.exe", true)]
    public void IsMatch_HandlesCommonPatterns(string input, string pattern, bool expected)
    {
        Assert.Equal(expected, GlobMatcher.IsMatch(input, pattern));
    }

    [Fact]
    public void IsMatch_IsCaseInsensitive()
    {
        Assert.True(GlobMatcher.IsMatch(@"C:\Games\Game.EXE", @"c:\games\game.exe"));
    }
}
