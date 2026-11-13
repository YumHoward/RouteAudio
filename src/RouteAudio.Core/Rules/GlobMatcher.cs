using System.Text;
using System.Text.RegularExpressions;

namespace RouteAudio.Core.Rules;

/// <summary>
/// 通配符匹配：<c>*</c> 匹配任意非分隔符字符，<c>**</c> 匹配任意字符，<c>?</c> 匹配单个字符。
/// 路径与进程名都按不区分大小写处理。
/// </summary>
public static class GlobMatcher
{
    private static readonly Dictionary<string, Regex> Cache = new(StringComparer.Ordinal);

    public static bool IsMatch(string input, string pattern)
    {
        if (string.IsNullOrEmpty(pattern)) return false;
        if (pattern == "*") return true;

        var regex = GetOrCreate(pattern);
        return regex.IsMatch(input.Replace('/', '\\'));
    }

    private static Regex GetOrCreate(string pattern)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(pattern, out var cached)) return cached;

            var regex = new Regex(Translate(pattern), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
            Cache[pattern] = regex;
            return regex;
        }
    }

    internal static string Translate(string pattern)
    {
        // 不含路径分隔符的模式（如 *.exe、spotify*）按「任意字符」处理，方便用户直接写文件名；
        // 含分隔符的模式（如 C:\Games\*\game.exe）按「单段」处理，* 不跨越目录。
        var segmentOnly = !pattern.Contains('\\') && !pattern.Contains('/');
        var singleStar = segmentOnly ? ".*" : "[^\\\\]*";

        var sb = new StringBuilder("^");
        var i = 0;
        while (i < pattern.Length)
        {
            var c = pattern[i];
            switch (c)
            {
                case '*':
                    if (i + 1 < pattern.Length && pattern[i + 1] == '*')
                    {
                        sb.Append(".*");
                        i += 2;
                        continue;
                    }

                    sb.Append(singleStar);
                    i++;
                    continue;
                case '?':
                    sb.Append('.');
                    i++;
                    continue;
                default:
                    sb.Append(Regex.Escape(c.ToString()));
                    i++;
                    continue;
            }
        }

        sb.Append('$');
        return sb.ToString();
    }
}
