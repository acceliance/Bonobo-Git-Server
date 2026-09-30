using Bonobo.Git.Server.Configuration;
using Bonobo.Git.Server.Data;
using Bonobo.Git.Server.Git;
using Bonobo.Git.Server.Models;
using Bonobo.Git.Server.Security;
using Serilog;
using System;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Unity;

namespace Bonobo.Git.Server.SshShell
{
    /// <summary>
    /// The forced command behind every SSH key in authorized_keys.
    ///
    /// sshd has already proved that the client holds the private key for one particular Bonobo key
    /// by the time we are started, and passes us that key's id. We therefore never authenticate
    /// anything ourselves - we decide what the authenticated user is allowed to do, and then hand
    /// our stdin/stdout straight to git.
    ///
    /// Nothing about the client's request is trusted: the requested command arrives in
    /// SSH_ORIGINAL_COMMAND and is matched against a whitelist, and the repository path we give git
    /// is built from the name stored in the database, never from what the client sent.
    /// </summary>
    public static class Program
    {
        private const int ExitFailure = 1;

        public static int Main(string[] args)
        {
            // Must happen before anything reads UserConfiguration: the shared configuration code
            // resolves '~\...' paths through ASP.NET's hosting environment, which does not exist here.
            UserConfiguration.PathResolver = new ApplicationDirectoryPathResolver();

            ConfigureLogging();

            try
            {
                return Run(args);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "SshShell: Unhandled exception");
                Fail("An internal error occurred.");
                return ExitFailure;
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }

        private static int Run(string[] args)
        {
            Guid keyId;
            if (args.Length != 1 || !Guid.TryParse(args[0], out keyId))
            {
                // This is a configuration problem, not a client one - authorized_keys always passes an id.
                Log.Error("SshShell: Expected a single key id argument, got {Count}", args.Length);
                Fail("This account is only for git access.");
                return ExitFailure;
            }

            string originalCommand = Environment.GetEnvironmentVariable("SSH_ORIGINAL_COMMAND");

            SshGitCommand command;
            string parseError;
            if (!SshCommandParser.TryParse(originalCommand, out command, out parseError))
            {
                Log.Warning("SshShell: Refused command {Command} for key {KeyId}: {Reason}", originalCommand, keyId, parseError);
                Fail(parseError);
                return ExitFailure;
            }

            IUnityContainer container = BuildContainer();

            var keyRepository = container.Resolve<ISshKeyRepository>();
            SshKeyModel key = keyRepository.GetKey(keyId);
            if (key == null)
            {
                // The key was deleted between authorized_keys being written and this connection.
                Log.Warning("SshShell: Unknown key {KeyId}", keyId);
                Fail("This key is no longer registered.");
                return ExitFailure;
            }

            var repositoryRepository = container.Resolve<IRepositoryRepository>();
            RepositoryModel repository = repositoryRepository.GetRepository(command.RepositoryName);
            var permissionService = container.Resolve<IRepositoryPermissionService>();

            // Answer 'no such repository' identically whether the repository is missing or merely
            // invisible to this user, so that SSH access cannot be used to enumerate repositories.
            if (repository == null || !permissionService.HasPermission(key.UserId, repository.Id, command.RequiredAccessLevel))
            {
                Log.Warning("SshShell: User {UserId} denied {Level} on {RepositoryName}",
                    key.UserId, command.RequiredAccessLevel, command.RepositoryName);
                Fail(String.Format("Repository '{0}' not found, or you do not have permission to access it.", command.RepositoryName));
                return ExitFailure;
            }

            keyRepository.RecordKeyUsed(keyId, DateTime.UtcNow);

            Log.Information("SshShell: User {UserId} running {Service} on {RepositoryName}",
                key.UserId, command.Service, repository.Name);

            return RunGit(container, key.UserId, command.Service, repository.Name);
        }

        private static int RunGit(IUnityContainer container, Guid userId, string service, string repositoryName)
        {
            var locator = container.Resolve<IGitRepositoryLocator>();

            // Built from the stored repository name, so a crafted request cannot reach outside the
            // repository directory however it was quoted.
            string repositoryPath = locator.GetRepositoryDirectoryPath(repositoryName).FullName;

            string gitPath = ResolvePath(ConfigurationManager.AppSettings["GitPath"]);
            string gitHomePath = ResolvePath(ConfigurationManager.AppSettings["GitHomePath"]);

            if (String.IsNullOrEmpty(gitPath) || !File.Exists(gitPath))
            {
                Log.Error("SshShell: git.exe not found at {GitPath}", gitPath);
                Fail("The server is misconfigured: git was not found.");
                return ExitFailure;
            }

            var info = new ProcessStartInfo(gitPath, service + " \"" + repositoryPath + "\"")
            {
                CreateNoWindow = true,
                // The whole point: git inherits our stdin/stdout, which sshd has wired to the client.
                // Redirecting them here would corrupt the pack protocol.
                UseShellExecute = false,
                RedirectStandardInput = false,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
                WorkingDirectory = Path.GetDirectoryName(repositoryPath) ?? Environment.CurrentDirectory,
            };

            if (!String.IsNullOrEmpty(gitHomePath))
            {
                info.EnvironmentVariables["HOME"] = gitHomePath;
            }

            SetHookEnvironment(info, container, userId);

            using (var process = Process.Start(info))
            {
                process.WaitForExit();
                return process.ExitCode;
            }
        }

        /// <summary>
        /// The same variables the HTTP path exports, so that a hook behaves identically whether the
        /// push arrived over HTTP or SSH. Unlike HTTP there is never an anonymous SSH user, so these
        /// are always populated.
        /// </summary>
        private static void SetHookEnvironment(ProcessStartInfo info, IUnityContainer container, Guid userId)
        {
            try
            {
                var membershipService = container.Resolve<IMembershipService>();
                var teamRepository = container.Resolve<ITeamRepository>();
                var roleProvider = container.Resolve<IRoleProvider>();

                UserModel user = membershipService.GetUserModel(userId);
                string username = user != null ? user.Username : String.Empty;
                string displayName = user != null ? user.DisplayName : String.Empty;

                info.EnvironmentVariables["AUTH_USER"] = username;
                info.EnvironmentVariables["REMOTE_USER"] = username;
                info.EnvironmentVariables["AUTH_USER_DISPLAYNAME"] = displayName;
                info.EnvironmentVariables["AUTH_USER_TEAMS"] =
                    UserExtensions.StringlistToEscapedStringForEnvVar(teamRepository.GetTeams(userId).Select(x => x.Name));
                info.EnvironmentVariables["AUTH_USER_ROLES"] =
                    UserExtensions.StringlistToEscapedStringForEnvVar(roleProvider.GetRolesForUser(userId));
            }
            catch (Exception ex)
            {
                // A hook losing its context is better than a push failing outright.
                Log.Warning(ex, "SshShell: Could not populate hook environment for user {UserId}", userId);
            }
        }

        /// <summary>
        /// Mirrors the registrations in Global.asax for the handful of services we need. The
        /// permission logic itself is shared with the web application rather than reimplemented,
        /// so SSH and HTTP can never disagree about who may push.
        /// </summary>
        private static IUnityContainer BuildContainer()
        {
            var container = new UnityContainer();

            switch ((AuthenticationSettings.MembershipService ?? String.Empty).ToLowerInvariant())
            {
                case "activedirectory":
                    container.RegisterType<IMembershipService, ADMembershipService>();
                    container.RegisterType<IRoleProvider, ADRoleProvider>();
                    container.RegisterType<ITeamRepository, ADTeamRepository>();
                    container.RegisterType<IRepositoryRepository, ADRepositoryRepository>();
                    container.RegisterType<ISshKeyRepository, ADSshKeyRepository>();
                    break;
                case "internal":
                    container.RegisterType<IMembershipService, EFMembershipService>();
                    container.RegisterType<IRoleProvider, EFRoleProvider>();
                    container.RegisterType<ITeamRepository, EFTeamRepository>();
                    container.RegisterType<IRepositoryRepository, EFRepositoryRepository>();
                    container.RegisterType<ISshKeyRepository, EFSshKeyRepository>();
                    break;
                default:
                    throw new ConfigurationErrorsException("Missing or unrecognised MembershipService setting");
            }

            container.RegisterType<IRepositoryPermissionService, RepositoryPermissionService>();

            // Func<BonoboGitServerContext> is resolved by Unity's built-in deferred resolution, the
            // same way the web application gets it - there is deliberately no registration for it.
            container.RegisterFactory<IGitRepositoryLocator>(
                (ctr, type, name) => new ConfigurationBasedRepositoryLocator(UserConfiguration.Current.Repositories));

            return container;
        }

        /// <summary>
        /// Resolves the '~\App_Data\...' style paths that the shared configuration uses. There is no
        /// ASP.NET hosting environment out here, so the application directory stands in for the site
        /// root - which is why the shell is deployed alongside a copy of the site's configuration.
        /// </summary>
        private static string ResolvePath(string path)
        {
            if (String.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            if (Path.IsPathRooted(path))
            {
                return path;
            }

            string relative = path.TrimStart('~').TrimStart('\\', '/');
            return Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relative));
        }

        private static void ConfigureLogging()
        {
            string logDirectory = ConfigurationManager.AppSettings["LogDirectory"];
            string resolved = ResolvePath(String.IsNullOrWhiteSpace(logDirectory) ? @"~\App_Data\Logs" : logDirectory);

            var configuration = new LoggerConfiguration().ReadFrom.AppSettings();
            if (!String.IsNullOrEmpty(resolved))
            {
                Directory.CreateDirectory(resolved);
                configuration = configuration.WriteTo.RollingFile(Path.Combine(resolved, "ssh-{Date}.txt"));
            }

            Log.Logger = configuration.CreateLogger();
        }

        /// <summary>
        /// Writes to stderr, which is the only channel the git client shows the user. Anything on
        /// stdout would be parsed as pack protocol and produce a confusing error instead.
        /// </summary>
        private static void Fail(string message)
        {
            Console.Error.WriteLine("Bonobo: " + message);
        }

        /// <summary>
        /// Resolves the site-relative paths in the shared configuration against the directory the
        /// shell was deployed to, standing in for ASP.NET's hosting environment.
        /// </summary>
        private sealed class ApplicationDirectoryPathResolver : IPathResolver
        {
            public string Resolve(string path)
            {
                return ResolvePath(path);
            }

            public string ResolveWithConfiguration(string configKey)
            {
                return Resolve(ConfigurationManager.AppSettings[configKey]);
            }
        }
    }
}
