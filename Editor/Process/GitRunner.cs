#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FlexGit
{
    /// <summary>
    /// Starts git processes: non-interactive environment, concurrent stdout/stderr reading
    /// (no pipe deadlocks), UTF-8 stdin, progress parsing and process-tree cancellation.
    /// </summary>
    internal sealed class GitRunner
    {
        private static readonly UTF8Encoding s_utf8 = new UTF8Encoding(false);

        /// <summary>Prepended to every command: machine-friendly output regardless of user config.</summary>
        private static readonly string[] s_defaultArguments =
        {
            "-c", "color.ui=false",
            "-c", "core.quotepath=false",
            "-c", "advice.detachedHead=false"
        };

        /// <summary>Inherited variables that would redirect git to another repository.</summary>
        private static readonly string[] s_scrubbedVariables =
        {
            "GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE", "GIT_PREFIX", "GIT_NAMESPACE",
            "GIT_OBJECT_DIRECTORY", "GIT_COMMON_DIR", "GIT_ALTERNATE_OBJECT_DIRECTORIES"
        };

        private readonly GitInstallation _installation;
        private readonly GitOptions _options;

        public GitRunner(GitInstallation installation, GitOptions options)
        {
            _installation = installation;
            _options = options;
        }

        public GitInstallation Installation => _installation;

        public GitOptions Options => _options;

        public async Task<GitResult> RunAsync(GitRunRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var allArguments = new List<string>(s_defaultArguments.Length + request.Arguments.Count);
            allArguments.AddRange(s_defaultArguments);
            allArguments.AddRange(request.Arguments);
            var displayCommand = "git " + GitArguments.RedactUrls(GitArguments.Join(request.Arguments));

            var workingDirectory = request.WorkingDirectory ?? Directory.GetCurrentDirectory();
            if (!Directory.Exists(workingDirectory))
                throw new GitCommandException(new GitError(GitErrorKind.NotFound,
                    $"Working directory '{workingDirectory}' does not exist.", -1, string.Empty, displayCommand));

            var startInfo = new ProcessStartInfo
            {
                FileName = _installation.ExecutablePath,
                Arguments = GitArguments.Join(allArguments),
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            ConfigureEnvironment(startInfo, request);
            TrySetUtf8StandardInput(startInfo);

            var stopwatch = Stopwatch.StartNew();
            using var process = new Process { StartInfo = startInfo };
            try
            {
                process.Start();
            }
            catch (Exception e) when (e is Win32Exception || e is InvalidOperationException || e is FileNotFoundException)
            {
                throw new GitCommandException(new GitError(GitErrorKind.GitNotFound,
                    $"Failed to start git '{_installation.ExecutablePath}': {e.Message}", -1, string.Empty, displayCommand));
            }

            var stdoutTask = ReadAllBytesAsync(process.StandardOutput.BaseStream);
            var stderrTask = ReadStandardErrorAsync(process.StandardError.BaseStream, request.Progress);
            var stdinTask = WriteStandardInputAsync(process, request.StandardInput);
            var exitTask = Task.Run(() => process.WaitForExit());

            var killed = false;
            using (cancellationToken.Register(() =>
                   {
                       try
                       {
                           if (process.HasExited)
                               return;
                       }
                       catch (InvalidOperationException)
                       {
                           return;
                       }

                       killed = true;
                       ProcessTree.Kill(process);
                   }))
            {
                await exitTask.ConfigureAwait(false);

                var streams = Task.WhenAll(stdoutTask, stderrTask);
                if (killed)
                    await Task.WhenAny(streams, Task.Delay(2000)).ConfigureAwait(false); // orphaned children may hold the pipes
                else
                    await streams.ConfigureAwait(false);
            }

            try
            {
                await stdinTask.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Stdin failures (process exited early) are irrelevant once the process is done.
            }

            stopwatch.Stop();

            if (killed)
            {
                _options.Log(GitLogLevel.Debug, $"{displayCommand} -> cancelled after {stopwatch.ElapsedMilliseconds} ms");
                throw new OperationCanceledException("The git command was cancelled.", cancellationToken);
            }

            var result = new GitResult(process.ExitCode, stdoutTask.Result, stderrTask.Result, displayCommand, stopwatch.Elapsed);
            if (result.Succeeded)
                _options.Log(GitLogLevel.Debug, $"{displayCommand} -> ok ({stopwatch.ElapsedMilliseconds} ms)");
            else
                _options.Log(GitLogLevel.Debug, $"{displayCommand} -> exit {result.ExitCode} ({stopwatch.ElapsedMilliseconds} ms): {GitErrorClassifier.ExtractMessage(result)}");
            return result;
        }

        private void ConfigureEnvironment(ProcessStartInfo startInfo, GitRunRequest request)
        {
            var environment = startInfo.Environment;
            foreach (var name in s_scrubbedVariables)
                environment.Remove(name);

            environment["GIT_TERMINAL_PROMPT"] = "0";   // never block on a credential prompt
            environment["GIT_EDITOR"] = "true";          // never open an editor
            environment["GIT_SEQUENCE_EDITOR"] = "true";
            environment["GIT_PAGER"] = "cat";
            environment["LC_ALL"] = "C";                 // English messages for error classification
            environment["LANG"] = "C";
            environment.Remove("LANGUAGE");

            if (request.ReadOnly)
                environment["GIT_OPTIONAL_LOCKS"] = "0";

            if (request.LiteralPathspecs)
                environment["GIT_LITERAL_PATHSPECS"] = "1";
            else
                environment.Remove("GIT_LITERAL_PATHSPECS");

            Apply(environment, _options.Environment);
            Apply(environment, request.Environment);
        }

        private static void Apply(IDictionary<string, string> environment, IDictionary<string, string?>? overrides)
        {
            if (overrides == null)
                return;
            foreach (var pair in overrides)
            {
                if (pair.Value == null)
                    environment.Remove(pair.Key);
                else
                    environment[pair.Key] = pair.Value;
            }
        }

        private static void TrySetUtf8StandardInput(ProcessStartInfo startInfo)
        {
            // StandardInputEncoding may be missing on older Mono profiles; isolate the member access
            // in its own method so a missing member only skips this step.
            try
            {
                SetUtf8StandardInput(startInfo);
            }
            catch (Exception e) when (e is MissingMemberException || e is TypeLoadException || e is NotSupportedException)
            {
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void SetUtf8StandardInput(ProcessStartInfo startInfo)
        {
            startInfo.StandardInputEncoding = s_utf8;
        }

        private static async Task WriteStandardInputAsync(Process process, byte[]? data)
        {
            try
            {
                if (data != null && data.Length > 0)
                {
                    var stream = process.StandardInput.BaseStream;
                    await stream.WriteAsync(data, 0, data.Length).ConfigureAwait(false);
                    await stream.FlushAsync().ConfigureAwait(false);
                }
            }
            catch (IOException)
            {
                // git exited before reading all input.
            }
            catch (ObjectDisposedException)
            {
            }
            finally
            {
                try
                {
                    process.StandardInput.Close();
                }
                catch (IOException)
                {
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }

        private static async Task<byte[]> ReadAllBytesAsync(Stream stream)
        {
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, 81920).ConfigureAwait(false);
            return buffer.ToArray();
        }

        /// <summary>
        /// Reads stderr, reporting progress lines as they arrive. Lines overwritten with '\r'
        /// (git's in-place progress updates) are dropped from the returned text, keeping it readable.
        /// </summary>
        private static async Task<string> ReadStandardErrorAsync(Stream stream, IProgress<GitProgress>? progress)
        {
            var decoder = s_utf8.GetDecoder();
            var bytes = new byte[4096];
            var chars = new char[s_utf8.GetMaxCharCount(bytes.Length)];
            var text = new StringBuilder();
            var line = new StringBuilder();
            var pendingCarriageReturn = false;

            while (true)
            {
                var read = await stream.ReadAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
                if (read == 0)
                    break;

                var count = decoder.GetChars(bytes, 0, read, chars, 0);
                for (var i = 0; i < count; i++)
                {
                    var c = chars[i];
                    if (c == '\r')
                    {
                        Report(line, progress);
                        pendingCarriageReturn = true;
                    }
                    else if (c == '\n')
                    {
                        if (!pendingCarriageReturn)
                            Report(line, progress);
                        if (line.Length > 0)
                            text.Append(line).Append('\n');
                        line.Clear();
                        pendingCarriageReturn = false;
                    }
                    else
                    {
                        if (pendingCarriageReturn)
                        {
                            line.Clear(); // the previous progress state was overwritten
                            pendingCarriageReturn = false;
                        }
                        line.Append(c);
                    }
                }
            }

            if (line.Length > 0)
            {
                if (!pendingCarriageReturn)
                    Report(line, progress);
                text.Append(line).Append('\n');
            }

            return text.ToString();
        }

        private static void Report(StringBuilder line, IProgress<GitProgress>? progress)
        {
            if (progress == null || line.Length == 0)
                return;
            if (ProgressParser.TryParse(line.ToString(), out var parsed) && parsed != null)
                progress.Report(parsed);
        }
    }
}
