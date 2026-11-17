#nullable enable
using System.Globalization;
using System.Text.RegularExpressions;

namespace FlexGit
{
    /// <summary>Parses git / git-lfs progress lines ("Phase:  45% (450/1000)...").</summary>
    internal static class ProgressParser
    {
        private static readonly Regex s_progress = new Regex(
            @"^(?<remote>remote:\s*)?(?<phase>[A-Za-z][A-Za-z ]*?):\s+(?<percent>\d{1,3})%\s+\((?<current>\d+)/(?<total>\d+)\)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static bool TryParse(string line, out GitProgress? progress)
        {
            progress = null;
            if (string.IsNullOrEmpty(line))
                return false;

            var match = s_progress.Match(line.Trim());
            if (!match.Success)
                return false;

            var percent = int.Parse(match.Groups["percent"].Value, CultureInfo.InvariantCulture);
            if (percent > 100)
                percent = 100;

            progress = new GitProgress(
                match.Groups["phase"].Value.Trim(),
                percent,
                long.Parse(match.Groups["current"].Value, CultureInfo.InvariantCulture),
                long.Parse(match.Groups["total"].Value, CultureInfo.InvariantCulture),
                match.Groups["remote"].Success,
                line);
            return true;
        }
    }
}
