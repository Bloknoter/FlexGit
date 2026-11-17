#nullable enable

namespace FlexGit
{
    /// <summary>A progress update parsed from git's stderr, e.g. "Receiving objects:  45% (450/1000)".</summary>
    public sealed class GitProgress
    {
        internal GitProgress(string phase, int percent, long current, long total, bool isRemote, string line)
        {
            Phase = phase;
            Percent = percent;
            Current = current;
            Total = total;
            IsRemote = isRemote;
            Line = line;
        }

        /// <summary>Phase name, e.g. "Receiving objects", "Resolving deltas", "Updating files", "Filtering content".</summary>
        public string Phase { get; }

        /// <summary>Completion of the phase, 0..100.</summary>
        public int Percent { get; }

        /// <summary>Items processed so far.</summary>
        public long Current { get; }

        /// <summary>Total items in the phase.</summary>
        public long Total { get; }

        /// <summary>True when the line was reported by the remote ("remote: ...").</summary>
        public bool IsRemote { get; }

        /// <summary>The original stderr line.</summary>
        public string Line { get; }

        /// <summary>Phase completion as 0..1.</summary>
        public float Fraction => Percent / 100f;

        public override string ToString() => $"{Phase}: {Percent}% ({Current}/{Total})";
    }
}
