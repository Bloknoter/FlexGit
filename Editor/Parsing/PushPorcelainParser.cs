#nullable enable
using System.Collections.Generic;

namespace FlexGit
{
    /// <summary>Parses <c>git push --porcelain</c>: "&lt;flag&gt;\t&lt;from&gt;:&lt;to&gt;\t&lt;summary&gt;" lines.</summary>
    internal static class PushPorcelainParser
    {
        public static List<PushRefUpdate> Parse(string output)
        {
            var updates = new List<PushRefUpdate>();
            foreach (var line in RefParser.SplitLines(output))
            {
                if (line.Length < 3 || line[1] != '\t')
                    continue;

                PushRefStatus status;
                switch (line[0])
                {
                    case ' ': status = PushRefStatus.FastForward; break;
                    case '+': status = PushRefStatus.Forced; break;
                    case '-': status = PushRefStatus.Deleted; break;
                    case '*': status = PushRefStatus.New; break;
                    case '!': status = PushRefStatus.Rejected; break;
                    case '=': status = PushRefStatus.UpToDate; break;
                    default: continue;
                }

                var parts = line.Substring(2).Split('\t');
                var refs = parts[0];
                var colon = refs.IndexOf(':');
                var source = colon >= 0 ? refs.Substring(0, colon) : string.Empty;
                var destination = colon >= 0 ? refs.Substring(colon + 1) : refs;
                updates.Add(new PushRefUpdate(status, source, destination, parts.Length > 1 ? parts[1] : string.Empty));
            }
            return updates;
        }
    }
}
