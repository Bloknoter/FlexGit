#nullable enable

namespace FlexGit
{
    /// <summary>A lightweight or annotated tag.</summary>
    public sealed class TagInfo
    {
        internal TagInfo(string name, string canonicalName, bool isAnnotated, string targetSha, GitSignature? tagger, string? message)
        {
            Name = name;
            CanonicalName = canonicalName;
            IsAnnotated = isAnnotated;
            TargetSha = targetSha;
            Tagger = tagger;
            Message = message;
        }

        /// <summary>Tag name, e.g. "v1.2.0".</summary>
        public string Name { get; }

        /// <summary>Full ref name, e.g. "refs/tags/v1.2.0".</summary>
        public string CanonicalName { get; }

        public bool IsAnnotated { get; }

        /// <summary>Commit (or object) the tag points to.</summary>
        public string TargetSha { get; }

        /// <summary>Tagger of an annotated tag, otherwise null.</summary>
        public GitSignature? Tagger { get; }

        /// <summary>Subject of an annotated tag's message, otherwise null.</summary>
        public string? Message { get; }

        public override string ToString() => Name;
    }

    /// <summary>A configured remote.</summary>
    public sealed class RemoteInfo
    {
        internal RemoteInfo(string name, string fetchUrl, string pushUrl)
        {
            Name = name;
            FetchUrl = fetchUrl;
            PushUrl = pushUrl;
        }

        public string Name { get; }

        public string FetchUrl { get; }

        /// <summary>Push URL (same as <see cref="FetchUrl"/> unless remote.&lt;name&gt;.pushurl is set).</summary>
        public string PushUrl { get; }

        public override string ToString() => $"{Name} {GitArguments.RedactUrls(FetchUrl)}";
    }
}
