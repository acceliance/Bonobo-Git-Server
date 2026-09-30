using System;
using System.Configuration;

namespace Bonobo.Git.Server.Configuration
{
    /// <summary>
    /// Deployment-time SSH settings. These are paths on the server which an administrator has to
    /// agree with sshd_config, so they live in web.config rather than in the editable site settings.
    ///
    /// Things an administrator may reasonably want to change from the web UI - whether SSH is
    /// advertised at all, and the host name shown in clone URLs - are in UserConfiguration instead.
    /// </summary>
    public static class SshSettings
    {
        /// <summary>
        /// The authorized_keys file that Bonobo owns and rewrites. It must be the file that
        /// sshd_config's AuthorizedKeysFile points at for the git account.
        /// </summary>
        public static string AuthorizedKeysPath { get; private set; }

        /// <summary>
        /// Full path to Bonobo.Git.Server.SshShell.exe, which every key's forced command invokes.
        /// </summary>
        public static string ShellPath { get; private set; }

        /// <summary>
        /// When set, Bonobo tries to lock the authorized_keys file down to SYSTEM, Administrators
        /// and the account sshd reads it as. Windows OpenSSH refuses to read a loosely permissioned
        /// file, so getting this wrong is the most common reason for 'Permission denied (publickey)'.
        /// </summary>
        public static bool HardenAuthorizedKeysPermissions { get; private set; }

        /// <summary>
        /// The Windows account sshd authenticates the git user as, granted read access to
        /// authorized_keys when hardening is on. Empty means only SYSTEM and Administrators get access.
        /// </summary>
        public static string ServiceAccount { get; private set; }

        /// <summary>True once an administrator has configured both of the paths we cannot guess</summary>
        public static bool IsConfigured
        {
            get { return !String.IsNullOrWhiteSpace(AuthorizedKeysPath) && !String.IsNullOrWhiteSpace(ShellPath); }
        }

        static SshSettings()
        {
            AuthorizedKeysPath = ResolveIfPresent("SshAuthorizedKeysPath");
            ShellPath = ResolveIfPresent("SshShellPath");
            ServiceAccount = ConfigurationManager.AppSettings["SshServiceAccount"];

            var harden = ConfigurationManager.AppSettings["SshHardenAuthorizedKeysPermissions"];
            HardenAuthorizedKeysPermissions = String.IsNullOrWhiteSpace(harden) || Convert.ToBoolean(harden);
        }

        private static string ResolveIfPresent(string configKey)
        {
            var value = ConfigurationManager.AppSettings[configKey];
            return String.IsNullOrWhiteSpace(value) ? null : ConfigurationEntry<UserConfiguration>.PathResolver.Resolve(value);
        }
    }
}
