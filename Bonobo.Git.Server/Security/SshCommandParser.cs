using System;
using System.IO;
using System.Linq;
using System.Text;

namespace Bonobo.Git.Server.Security
{
    /// <summary>
    /// A git request which arrived over SSH, once we have decided it is something we are willing to run.
    /// </summary>
    public class SshGitCommand
    {
        /// <summary>'upload-pack', 'receive-pack' or 'upload-archive' - never anything else</summary>
        public string Service { get; internal set; }

        /// <summary>The repository as the client asked for it, with the leading slash and '.git' removed</summary>
        public string RepositoryName { get; internal set; }

        public RepositoryAccessLevel RequiredAccessLevel
        {
            get { return Service == "receive-pack" ? RepositoryAccessLevel.Push : RepositoryAccessLevel.Pull; }
        }
    }

    /// <summary>
    /// Parses the SSH_ORIGINAL_COMMAND that sshd hands to our forced command, e.g.
    ///
    ///     git-upload-pack 'myrepo.git'
    ///
    /// This is the only thing standing between a remote client and a process launch, so it is a
    /// strict whitelist: three known services, one argument, and a repository name that cannot
    /// escape the repository directory. Everything else is refused.
    /// </summary>
    public static class SshCommandParser
    {
        private static readonly string[] PermittedServices = { "upload-pack", "receive-pack", "upload-archive" };

        public static bool TryParse(string command, out SshGitCommand result, out string error)
        {
            result = null;
            error = null;

            if (String.IsNullOrWhiteSpace(command))
            {
                error = "No command was supplied. This server only accepts git over SSH, not an interactive shell.";
                return false;
            }

            string remainder = command.Trim();
            string service = null;

            // Clients send either 'git-upload-pack <repo>' or 'git upload-pack <repo>'.
            foreach (string candidate in PermittedServices)
            {
                if (TryTakePrefix(ref remainder, "git-" + candidate) || TryTakePrefix(ref remainder, "git " + candidate))
                {
                    service = candidate;
                    break;
                }
            }

            if (service == null)
            {
                error = String.Format("Unsupported command '{0}'.", command);
                return false;
            }

            string repositoryName;
            if (!TryUnquote(remainder.Trim(), out repositoryName) || String.IsNullOrWhiteSpace(repositoryName))
            {
                error = "No repository was specified.";
                return false;
            }

            repositoryName = repositoryName.Trim();

            // 'git@host:/repo.git' and 'git@host:repo.git' are both common, and the '.git' suffix is optional.
            repositoryName = repositoryName.TrimStart('/');
            if (repositoryName.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            {
                repositoryName = repositoryName.Substring(0, repositoryName.Length - 4);
            }

            if (!IsSafeRepositoryName(repositoryName))
            {
                error = "Invalid repository name.";
                return false;
            }

            result = new SshGitCommand { Service = service, RepositoryName = repositoryName };
            return true;
        }

        private static bool TryTakePrefix(ref string input, string prefix)
        {
            if (!input.StartsWith(prefix, StringComparison.Ordinal))
            {
                return false;
            }

            // Must be followed by whitespace, otherwise 'git-upload-packet' would match 'git-upload-pack'.
            if (input.Length > prefix.Length && input[prefix.Length] != ' ' && input[prefix.Length] != '\t')
            {
                return false;
            }

            input = input.Substring(prefix.Length);
            return true;
        }

        /// <summary>
        /// Removes the shell quoting that git puts around the repository path, following the same
        /// rules a shell would.
        ///
        /// The distinction between the two quote characters matters: inside single quotes a shell
        /// escapes nothing at all, so treating a backslash as an escape there would silently turn
        /// 'sub\dir' into 'subdir' and let a name through that should have been refused.
        /// </summary>
        private static bool TryUnquote(string input, out string value)
        {
            value = null;

            if (input.Length == 0)
            {
                return false;
            }

            char quote = input[0];
            if (quote != '\'' && quote != '"')
            {
                // Unquoted: it must then be a single bare token.
                if (input.Any(Char.IsWhiteSpace))
                {
                    return false;
                }
                value = input;
                return true;
            }

            if (input.Length < 2 || input[input.Length - 1] != quote)
            {
                return false;
            }

            string inner = input.Substring(1, input.Length - 2);

            if (quote == '\'')
            {
                // Everything up to the closing quote is literal. git writes an embedded quote as
                // '\'' , which is really three concatenated words - we simply refuse those, because
                // a repository name containing a quote is refused later anyway.
                if (inner.IndexOf('\'') >= 0)
                {
                    return false;
                }

                value = inner;
                return true;
            }

            // Double quotes: a shell unescapes only \" and \\ ; any other backslash stays put.
            var builder = new StringBuilder(inner.Length);
            for (int i = 0; i < inner.Length; i++)
            {
                char c = inner[i];

                if (c == '\\' && i + 1 < inner.Length && (inner[i + 1] == '"' || inner[i + 1] == '\\'))
                {
                    builder.Append(inner[i + 1]);
                    i++;
                    continue;
                }

                if (c == '"')
                {
                    // An unescaped quote before the end means there is a second argument.
                    return false;
                }

                builder.Append(c);
            }

            value = builder.ToString();
            return true;
        }

        /// <summary>
        /// Bonobo repository names are flat, so anything resembling a path or a traversal is refused.
        /// The name is later looked up in the database and only the stored name is ever turned into a
        /// path, but refusing here keeps the rejection cheap and the reason obvious in the log.
        /// </summary>
        private static bool IsSafeRepositoryName(string name)
        {
            if (String.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            if (name.IndexOf('/') >= 0 || name.IndexOf('\\') >= 0 || name.IndexOf(':') >= 0)
            {
                return false;
            }

            if (name == "." || name == ".." || name.Contains(".."))
            {
                return false;
            }

            return !name.Any(c => Char.IsControl(c)) && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
        }
    }
}
