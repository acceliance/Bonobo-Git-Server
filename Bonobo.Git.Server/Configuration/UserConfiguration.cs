using Bonobo.Git.Server.App_GlobalResources;
using System;
using System.Configuration;
using System.Xml.Serialization;

namespace Bonobo.Git.Server.Configuration
{

    [XmlRoot(ElementName = "Configuration", IsNullable = false)]
    public class UserConfiguration : ConfigurationEntry<UserConfiguration>
    {
        public bool AllowAnonymousPush { get; set; }
        [XmlElement(ElementName = "Repositories")]
        public string RepositoryPath { get; set; }
        public bool AllowUserRepositoryCreation { get; set; }
        public bool AllowPushToCreate { get; set; }
        public bool AllowAnonymousRegistration { get; set; }
        public string DefaultLanguage { get; set; }
        public string SiteTitle { get; set; }
        public string SiteLogoUrl { get; set; }
        public string SiteFooterMessage { get; set; }
        public string SiteCssUrl { get; set; }
        public bool IsCommitAuthorAvatarVisible { get; set; }
        public string LinksRegex { get; set; }
        public string LinksUrl { get; set; }

        /// <summary>Whether SSH clone URLs are offered in the UI. The sshd side is configured in web.config.</summary>
        public bool SshEnabled { get; set; }

        /// <summary>Host name clients should use for SSH, which is often not the web site's host name</summary>
        public string SshHost { get; set; }

        /// <summary>0 means the default SSH port</summary>
        public int SshPort { get; set; }

        /// <summary>The account in the clone URL, i.e. the 'git' in git@host:repo.git</summary>
        public string SshUser { get; set; }

        public string Repositories => PathResolver.Resolve(RepositoryPath);

        public bool HasSiteFooterMessage => !string.IsNullOrWhiteSpace(this.SiteFooterMessage);

        public bool HasCustomSiteLogo => !string.IsNullOrWhiteSpace(this.SiteLogoUrl);

        public bool HasCustomSiteCss => !string.IsNullOrWhiteSpace(SiteCssUrl);

        public bool HasLinks => !string.IsNullOrWhiteSpace(this.LinksRegex);

        public string GetSiteTitle() => !string.IsNullOrWhiteSpace(this.SiteTitle) ? this.SiteTitle : Resources.Layout_Title;

        public bool HasSshConfigured => SshEnabled && !string.IsNullOrWhiteSpace(SshHost);

        public int GetSshPort() => SshPort > 0 ? SshPort : 22;

        public string GetSshUser() => !string.IsNullOrWhiteSpace(SshUser) ? SshUser : "git";

        /// <summary>
        /// The clone URL to show for a repository. Uses the scp-like short form on the default port,
        /// because that is what users recognise, and the explicit ssh:// form otherwise - the short
        /// form has nowhere to put a port number.
        /// </summary>
        public string GetSshUrl(string repositoryName)
        {
            if (!HasSshConfigured)
            {
                return null;
            }

            return GetSshPort() == 22
                ? $"{GetSshUser()}@{SshHost}:{repositoryName}.git"
                : $"ssh://{GetSshUser()}@{SshHost}:{GetSshPort()}/{repositoryName}.git";
        }

        public static void Initialize()
        {
            if (IsInitialized)
            {
                return;
            }

            Current.RepositoryPath = ConfigurationManager.AppSettings["DefaultRepositoriesDirectory"];
            Current.Save();
        }

        private static bool IsInitialized => !String.IsNullOrEmpty(Current.RepositoryPath);
    }
}