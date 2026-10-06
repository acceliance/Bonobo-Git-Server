using LibGit2Sharp;
using LibGit2Sharp.Handlers;
using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Bonobo.Git.Server.Git
{
    /// <summary>
    /// A remote repository URL which has been checked and is safe to hand to libgit2.
    /// </summary>
    public class ImportSource
    {
        /// <summary>The URL with any user name or password stripped off, so it can be stored in the remote's config</summary>
        public string Url { get; internal set; }

        public string Username { get; internal set; }

        public string Password { get; internal set; }

        public bool HasCredentials
        {
            get { return !String.IsNullOrEmpty(Username) || !String.IsNullOrEmpty(Password); }
        }
    }

    /// <summary>
    /// Populates a freshly created bare repository with the branches and tags of a remote one -
    /// the equivalent of 'git clone --mirror', minus hosting-specific refs such as GitHub's refs/pull/*.
    ///
    /// Only http and https sources are accepted. libgit2 would happily clone a local path too, which
    /// would let anyone allowed to create a repository copy any other repository on this server,
    /// whatever their permissions on it.
    /// </summary>
    public static class RepositoryImporter
    {
        public const string RemoteName = "origin";

        /// <summary>Used as the user name when only a token is supplied - GitHub and GitLab accept any non-empty name with a token</summary>
        public const string TokenUsername = "git";

        public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(30);

        private static readonly string[] RefSpecs =
        {
            "+refs/heads/*:refs/heads/*",
            "+refs/tags/*:refs/tags/*",
        };

        /// <summary>
        /// Validates the URL the user typed. Credentials embedded in the URL (https://user:token@host/...)
        /// are moved out of it; explicitly supplied ones win.
        /// </summary>
        public static bool TryParseSource(string url, string username, string password, out ImportSource source)
        {
            source = null;

            Uri uri;
            if (String.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out uri))
            {
                return false;
            }

            if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            {
                return false;
            }

            if (String.IsNullOrEmpty(uri.Host))
            {
                return false;
            }

            string urlUsername = null;
            string urlPassword = null;
            if (!String.IsNullOrEmpty(uri.UserInfo))
            {
                string[] parts = uri.UserInfo.Split(new[] { ':' }, 2);
                urlUsername = Uri.UnescapeDataString(parts[0]);
                urlPassword = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : null;
            }

            var builder = new UriBuilder(uri) { UserName = "", Password = "" };

            source = new ImportSource
            {
                Url = builder.Uri.AbsoluteUri,
                Username = !String.IsNullOrEmpty(username) ? username : urlUsername,
                Password = !String.IsNullOrEmpty(password) ? password : urlPassword,
            };
            if (String.IsNullOrEmpty(source.Username) && !String.IsNullOrEmpty(source.Password))
            {
                source.Username = TokenUsername;
            }
            return true;
        }

        /// <summary>
        /// Fetches every branch and tag of <paramref name="source"/> into the bare repository at
        /// <paramref name="repositoryPath"/>, and points HEAD at the remote's default branch.
        /// Throws LibGit2SharpException (or a subclass) when the remote can't be read.
        /// </summary>
        public static void Import(ImportSource source, string repositoryPath)
        {
            CredentialsHandler credentials = null;
            if (source.HasCredentials)
            {
                credentials = (url, usernameFromUrl, types) =>
                    new UsernamePasswordCredentials { Username = source.Username, Password = source.Password };
            }

            var stopwatch = Stopwatch.StartNew();
            var options = new FetchOptions
            {
                CredentialsProvider = credentials,
                TagFetchMode = TagFetchMode.None,
                // Returning false aborts the transfer - libgit2 has no timeout of its own
                OnTransferProgress = progress => stopwatch.Elapsed < Timeout,
            };

            using (var repository = new Repository(repositoryPath))
            {
                repository.Network.Remotes.Add(RemoteName, source.Url, RefSpecs[0]).Dispose();
                repository.Network.Remotes.Update(RemoteName, r => r.FetchRefSpecs.Add(RefSpecs[1]));

                string defaultBranch;
                using (Remote remote = repository.Network.Remotes[RemoteName])
                {
                    // ListReferences refuses a null handler, unlike Fetch
                    var references = credentials != null
                        ? repository.Network.ListReferences(remote, credentials)
                        : repository.Network.ListReferences(remote);
                    defaultBranch = FindDefaultBranch(references.ToList());
                }

                Commands.Fetch(repository, RemoteName, RefSpecs, options, "import from " + source.Url);

                if (defaultBranch != null && repository.Refs[defaultBranch] != null)
                {
                    repository.Refs.UpdateTarget(repository.Refs.Head, repository.Refs[defaultBranch], "import");
                }

                Log.Information("Import: fetched {Url} into {Path} in {Elapsed}, HEAD is {Head}",
                    source.Url, repositoryPath, stopwatch.Elapsed, defaultBranch);
            }
        }

        /// <summary>
        /// The branch the remote's HEAD names. Old servers don't advertise the symref, in which case
        /// we pick a branch on the same commit, preferring the usual names.
        /// </summary>
        public static string FindDefaultBranch(IList<Reference> remoteReferences)
        {
            Reference head = remoteReferences.FirstOrDefault(r => r.CanonicalName == "HEAD");
            if (head == null)
            {
                return null;
            }

            var symbolic = head as SymbolicReference;
            if (symbolic != null && symbolic.TargetIdentifier.StartsWith("refs/heads/", StringComparison.Ordinal))
            {
                return symbolic.TargetIdentifier;
            }

            var candidates = remoteReferences
                .Where(r => r.CanonicalName.StartsWith("refs/heads/", StringComparison.Ordinal) && r.TargetIdentifier == head.TargetIdentifier)
                .Select(r => r.CanonicalName)
                .ToList();

            return candidates.FirstOrDefault(n => n == "refs/heads/main")
                ?? candidates.FirstOrDefault(n => n == "refs/heads/master")
                ?? candidates.FirstOrDefault();
        }
    }
}
