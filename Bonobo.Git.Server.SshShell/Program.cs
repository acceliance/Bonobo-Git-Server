using Bonobo.Git.Server.Configuration;
using Bonobo.Git.Server.Data;
using Bonobo.Git.Server.Git;
using Bonobo.Git.Server.Models;
using Bonobo.Git.Server.Security;
using Serilog;
using System;
using System.Text;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Collections;
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

            // Only used to build the environment git runs with: starting git is done by hand below.
            var info = new ProcessStartInfo(gitPath) { UseShellExecute = false };

            if (!String.IsNullOrEmpty(gitHomePath))
            {
                info.EnvironmentVariables["HOME"] = gitHomePath;
            }

            SetHookEnvironment(info, container, userId);

            string commandLine = "\"" + gitPath + "\" " + service + " \"" + repositoryPath + "\"";
            string workingDirectory = Path.GetDirectoryName(repositoryPath) ?? Environment.CurrentDirectory;

            return RunWithInheritedStdio(gitPath, commandLine, workingDirectory, info.EnvironmentVariables);
        }

        /// <summary>
        /// Starts git with OUR stdin, stdout and stderr - the pipes sshd wired to the client - as its own,
        /// and waits for it. No bytes pass through this process, so the pack protocol cannot be
        /// altered, stalled or reordered by it.
        ///
        /// Process.Start cannot do this: without redirection it does not hand the parent's standard
        /// handles to the child, and sshd starts us without a console for git to inherit instead, so
        /// git would see neither the client's input nor an output the client can read. Redirecting and
        /// relaying the streams ourselves was tried, but a read pending on the client's input kept
        /// later input (everything a real clone sends after the ref advertisement) from reaching
        /// git. Passing the handles straight through with CreateProcess avoids both.
        /// </summary>
        private static int RunWithInheritedStdio(string exePath, string commandLine, string workingDirectory,
            System.Collections.Specialized.StringDictionary environment)
        {
            IntPtr stdIn = NativeMethods.GetStdHandle(NativeMethods.StdInputHandle);
            IntPtr stdOut = NativeMethods.GetStdHandle(NativeMethods.StdOutputHandle);
            IntPtr stdErr = NativeMethods.GetStdHandle(NativeMethods.StdErrorHandle);

            // The child can only use a handle that is marked inheritable.
            foreach (IntPtr handle in new[] { stdIn, stdOut, stdErr })
            {
                if (IsUsableHandle(handle))
                {
                    NativeMethods.SetHandleInformation(handle, NativeMethods.HandleFlagInherit, NativeMethods.HandleFlagInherit);
                }
            }

            // A Unicode environment block: NAME=value pairs, each ending in a NUL, then one more NUL.
            var names = new List<string>();
            foreach (DictionaryEntry entry in environment) names.Add((string)entry.Key);
            names.Sort(StringComparer.OrdinalIgnoreCase);
            var block = new StringBuilder();
            foreach (string name in names)
            {
                block.Append(name).Append('=').Append(environment[name]).Append('\0');
            }
            block.Append('\0');

            var startup = new NativeMethods.StartupInfo
            {
                cb = Marshal.SizeOf(typeof(NativeMethods.StartupInfo)),
                dwFlags = NativeMethods.StartfUseStdHandles,
                hStdInput = IsUsableHandle(stdIn) ? stdIn : IntPtr.Zero,
                hStdOutput = IsUsableHandle(stdOut) ? stdOut : IntPtr.Zero,
                hStdError = IsUsableHandle(stdErr) ? stdErr : IntPtr.Zero,
            };

            IntPtr environmentBlock = Marshal.StringToHGlobalUni(block.ToString());
            try
            {
                NativeMethods.ProcessInformation process;
                bool started = NativeMethods.CreateProcess(
                    exePath,
                    new StringBuilder(commandLine),
                    IntPtr.Zero,
                    IntPtr.Zero,
                    true, // inherit the handles marked above
                    NativeMethods.CreateUnicodeEnvironment | NativeMethods.CreateNoWindow,
                    environmentBlock,
                    workingDirectory,
                    ref startup,
                    out process);

                if (!started)
                {
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Could not start git.");
                }

                try
                {
                    NativeMethods.WaitForSingleObject(process.hProcess, NativeMethods.Infinite);

                    uint exitCode;
                    if (!NativeMethods.GetExitCodeProcess(process.hProcess, out exitCode))
                    {
                        throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                    }
                    return unchecked((int)exitCode);
                }
                finally
                {
                    NativeMethods.CloseHandle(process.hThread);
                    NativeMethods.CloseHandle(process.hProcess);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(environmentBlock);
            }
        }

        private static bool IsUsableHandle(IntPtr handle)
        {
            return handle != IntPtr.Zero && handle != new IntPtr(-1);
        }

        private static class NativeMethods
        {
            public const int StdInputHandle = -10;
            public const int StdOutputHandle = -11;
            public const int StdErrorHandle = -12;
            public const uint HandleFlagInherit = 0x00000001;
            public const int StartfUseStdHandles = 0x00000100;
            public const uint CreateUnicodeEnvironment = 0x00000400;
            public const uint CreateNoWindow = 0x08000000;
            public const uint Infinite = 0xFFFFFFFF;

            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            public struct StartupInfo
            {
                public int cb;
                public string lpReserved;
                public string lpDesktop;
                public string lpTitle;
                public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute;
                public int dwFlags;
                public short wShowWindow, cbReserved2;
                public IntPtr lpReserved2;
                public IntPtr hStdInput, hStdOutput, hStdError;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct ProcessInformation
            {
                public IntPtr hProcess, hThread;
                public int dwProcessId, dwThreadId;
            }

            [DllImport("kernel32.dll")]
            public static extern IntPtr GetStdHandle(int nStdHandle);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool SetHandleInformation(IntPtr hObject, uint dwMask, uint dwFlags);

            [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool CreateProcess(string lpApplicationName, StringBuilder lpCommandLine,
                IntPtr lpProcessAttributes, IntPtr lpThreadAttributes,
                [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles, uint dwCreationFlags,
                IntPtr lpEnvironment, string lpCurrentDirectory,
                ref StartupInfo lpStartupInfo, out ProcessInformation lpProcessInformation);

            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool GetExitCodeProcess(IntPtr hProcess, out uint lpExitCode);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool CloseHandle(IntPtr hObject);
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
