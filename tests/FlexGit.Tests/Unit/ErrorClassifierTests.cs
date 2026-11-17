namespace FlexGit.Tests.Unit;

[TestClass]
public class ErrorClassifierTests
{
    [DataTestMethod]
    [DataRow("fatal: not a git repository (or any of the parent directories): .git", GitErrorKind.NotARepository)]
    [DataRow("fatal: detected dubious ownership in repository at 'E:/x'", GitErrorKind.UnsafeRepository)]
    [DataRow("fatal: could not read Username for 'https://github.com': terminal prompts disabled", GitErrorKind.AuthenticationFailed)]
    [DataRow("remote: Invalid username or password.\nfatal: Authentication failed for 'https://github.com/a/b.git/'", GitErrorKind.AuthenticationFailed)]
    [DataRow("git@github.com: Permission denied (publickey).", GitErrorKind.AuthenticationFailed)]
    [DataRow("fatal: unable to access 'https://github.com/a/b.git/': The requested URL returned error: 403", GitErrorKind.AuthenticationFailed)]
    [DataRow("remote: Repository not found.\nfatal: repository 'https://github.com/a/b.git/' not found", GitErrorKind.RemoteNotFound)]
    [DataRow("fatal: 'C:/nope' does not appear to be a git repository", GitErrorKind.RemoteNotFound)]
    [DataRow("fatal: unable to access 'https://nohost/': Could not resolve host: nohost", GitErrorKind.NetworkError)]
    [DataRow("fatal: Unable to create 'E:/r/.git/index.lock': File exists.", GitErrorKind.IndexLocked)]
    [DataRow("error: Your local changes to the following files would be overwritten by checkout:\n\ta.txt", GitErrorKind.LocalChangesWouldBeOverwritten)]
    [DataRow("error: The following untracked working tree files would be overwritten by merge:", GitErrorKind.LocalChangesWouldBeOverwritten)]
    [DataRow("CONFLICT (content): Merge conflict in a.txt\nAutomatic merge failed; fix conflicts and then commit the result.", GitErrorKind.Conflicts)]
    [DataRow(" ! [rejected]        main -> main (fetch first)\nerror: failed to push some refs", GitErrorKind.Rejected)]
    [DataRow("fatal: Not possible to fast-forward, aborting.", GitErrorKind.CannotFastForward)]
    [DataRow("There is no tracking information for the current branch.", GitErrorKind.NoUpstream)]
    [DataRow("fatal: There is no merge to abort (MERGE_HEAD missing).", GitErrorKind.NoOperationInProgress)]
    [DataRow("fatal: No rebase in progress?", GitErrorKind.NoOperationInProgress)]
    [DataRow("nothing to commit, working tree clean", GitErrorKind.NothingToCommit)]
    [DataRow("error: The branch 'x' is not fully merged.", GitErrorKind.NotFullyMerged)]
    [DataRow("fatal: a branch named 'x' already exists", GitErrorKind.AlreadyExists)]
    [DataRow("fatal: 'a..b' is not a valid branch name", GitErrorKind.InvalidName)]
    [DataRow("fatal: invalid reference: nope", GitErrorKind.NotFound)]
    [DataRow("merge: nope - not something we can merge", GitErrorKind.NotFound)]
    [DataRow("*** Please tell me who you are.", GitErrorKind.IdentityNotConfigured)]
    [DataRow("git: 'lfs' is not a git command. See 'git --help'.", GitErrorKind.LfsNotInstalled)]
    [DataRow("something completely different", GitErrorKind.Unknown)]
    public void ClassifyText_MapsKnownMessages(string text, GitErrorKind expected)
    {
        Assert.AreEqual(expected, GitErrorClassifier.ClassifyText(text));
    }
}
