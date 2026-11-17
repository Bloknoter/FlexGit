#nullable enable
using System;

namespace FlexGit
{
    /// <summary>Name, e-mail and timestamp of a commit author/committer or tagger.</summary>
    public sealed class GitSignature
    {
        public GitSignature(string name, string email, DateTimeOffset when)
        {
            Name = name ?? string.Empty;
            Email = email ?? string.Empty;
            When = when;
        }

        public string Name { get; }

        public string Email { get; }

        public DateTimeOffset When { get; }

        public override string ToString() => $"{Name} <{Email}> {When:u}";
    }

    /// <summary>A name/e-mail pair used for commits (user.name / user.email).</summary>
    public sealed class UserIdentity
    {
        public UserIdentity(string name, string email)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Name is required.", nameof(name));
            Name = name;
            Email = email ?? string.Empty;
        }

        public string Name { get; }

        public string Email { get; }

        public override string ToString() => $"{Name} <{Email}>";
    }
}
