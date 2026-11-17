#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FlexGit
{
    /// <summary>Tag operations. Access through <see cref="GitRepository.Tags"/>.</summary>
    public sealed partial class TagCommands
    {
        private readonly GitRepository _repository;

        internal TagCommands(GitRepository repository) => _repository = repository;

        private GitContext Context => _repository.Context;

        /// <summary>Lists tags, newest first.</summary>
        public async Task<IReadOnlyList<TagInfo>> ListAsync(CancellationToken cancellationToken = default)
        {
            var request = Context.Request("for-each-ref", "--sort=-creatordate", "--format=" + RefParser.TagFormat, "refs/tags");
            request.ReadOnly = true;
            var result = await Context.RunLockedCheckedAsync(request, cancellationToken).ConfigureAwait(false);
            return RefParser.ParseTags(result.StandardOutput);
        }

        /// <summary>True when the tag exists locally.</summary>
        public async Task<bool> ExistsAsync(string name, CancellationToken cancellationToken = default)
        {
            GitArguments.EnsureSafeRef(name, nameof(name));
            var request = Context.Request("show-ref", "--verify", "--quiet", "refs/tags/" + name);
            request.ReadOnly = true;
            var result = await Context.RunLockedAsync(request, cancellationToken).ConfigureAwait(false);
            return result.Succeeded;
        }

        /// <summary>
        /// Creates a tag. With a <paramref name="message"/> the tag is annotated (records tagger and date), otherwise lightweight.
        /// Without <paramref name="force"/>, an existing tag is not overwritten (<see cref="GitErrorKind.AlreadyExists"/>).
        /// </summary>
        public Task CreateAsync(string name, string? target = null, string? message = null, bool force = false, CancellationToken cancellationToken = default)
        {
            GitArguments.EnsureSafeRef(name, nameof(name));
            var request = Context.Request("tag");
            request.AddIf(force, "--force");
            if (message != null)
            {
                request.Add("--annotate").Add("--file=-");
                request.StandardInput = GitArguments.Utf8(message);
            }
            request.Add(name);
            if (target != null)
                request.Add(GitArguments.EnsureSafeRef(target, nameof(target)));
            return Context.RunLockedCheckedAsync(request, cancellationToken);
        }

        /// <summary>Deletes a local tag.</summary>
        public Task DeleteAsync(string name, CancellationToken cancellationToken = default)
        {
            GitArguments.EnsureSafeRef(name, nameof(name));
            return Context.RunLockedCheckedAsync(Context.Request("tag", "--delete", name), cancellationToken);
        }
    }
}
