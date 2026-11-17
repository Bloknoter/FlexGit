#nullable enable
using System;

namespace FlexGit
{
    /// <summary>The git executable FlexGit uses, with its version and git-lfs availability.</summary>
    public sealed class GitInstallation
    {
        internal GitInstallation(string executablePath, Version version, string versionText, Version? lfsVersion)
        {
            ExecutablePath = executablePath;
            Version = version;
            VersionText = versionText;
            LfsVersion = lfsVersion;
        }

        /// <summary>Full path of the git executable.</summary>
        public string ExecutablePath { get; }

        /// <summary>Parsed git version (major.minor.patch).</summary>
        public Version Version { get; }

        /// <summary>Raw output of <c>git --version</c>.</summary>
        public string VersionText { get; }

        /// <summary>git-lfs version, or null when git-lfs is not installed.</summary>
        public Version? LfsVersion { get; }

        /// <summary>True when git-lfs is installed.</summary>
        public bool IsLfsAvailable => LfsVersion != null;

        public override string ToString() => $"git {Version} ({ExecutablePath}), git-lfs {(LfsVersion?.ToString() ?? "not installed")}";
    }
}
