Bonobo Git Server
==============================================

[![Build status](https://ci.appveyor.com/api/projects/status/4vyllwtb5i645lrt/branch/master?svg=true)](https://ci.appveyor.com/project/jakubgarfield/bonobo-git-server)

[![Maintained by Acceliance](https://img.shields.io/badge/maintained%20by-Acceliance-0072C6)](https://github.com/acceliance)
[![Fork of jakubgarfield/Bonobo-Git-Server](https://img.shields.io/badge/fork%20of-jakubgarfield%2FBonobo--Git--Server-lightgrey?logo=github)](https://github.com/jakubgarfield/Bonobo-Git-Server)
[![.NET Framework 4.8](https://img.shields.io/badge/.NET%20Framework-4.8-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/download/dotnet-framework/net48)
[![Last commit](https://img.shields.io/github/last-commit/acceliance/Bonobo-Git-Server?label=last%20commit)](https://github.com/acceliance/Bonobo-Git-Server/commits/master)

Thank you for downloading Bonobo Git Server. For more information please visit [http://bonobogitserver.com](http://bonobogitserver.com).


Acceliance contributions
-----------------------------------------------

This fork is maintained by [Acceliance](https://github.com/acceliance) and adds the following improvements on top of upstream Bonobo Git Server.

[![Mobile responsive](https://img.shields.io/badge/UI-mobile%20responsive-success)](#)
[![SVG support](https://img.shields.io/badge/rendering-SVG%20support-success)](#)
[![Markdown images](https://img.shields.io/badge/markdown-image%20paths%20fixed-success)](#)
[![Anonymous access](https://img.shields.io/badge/access-anonymous%20mode-success)](#)
[![Reader profile](https://img.shields.io/badge/roles-reader%20profile-success)](#)
[![SSH keys](https://img.shields.io/badge/access-SSH%20keys-success)](#)
[![SSH setup script](https://img.shields.io/badge/setup-local%20SSH%20script-success)](#)
[![Import from URL](https://img.shields.io/badge/repositories-import%20from%20URL-success)](#)
[![Dependencies](https://img.shields.io/badge/NuGet-dependencies%20updated-success)](#)

* **Mobile responsive UI** — layout and stylesheet reworked so the web frontend is usable on small screens.
* **SVG support** — SVG files are served and displayed in the repository browser.
* **Markdown image rendering** — absolute and repository-relative image paths in `README.md` now resolve correctly in the Blob and repository views (URLs generated through the `RepositoryRaw` route, including the repository UUID).
* **Anonymous access** — an anonymous browsing mode for repositories.
* **Reader profile** — a read-only user profile.
* **SSH keys** — users register SSH public keys on their account (ed25519, RSA 2048+, ECDSA and FIDO security keys) and clone over `git@server:repository.git`, the way GitHub works. Each key shows its fingerprint, when it was added and when it was last used; repository pages offer an SSH clone URL beside the HTTP one. Served by Windows OpenSSH with a forced command; see [How to clone over SSH?](#how-to-clone-over-ssh) for users and [SSH access](#ssh-access) for the server setup.
* **Local SSH setup script** — `Setup-LocalSsh.ps1`, shipped with the site, performs the whole SSH setup unattended on a development machine: OpenSSH Server, the `git` account, the shell deployment, ACLs, `web.config`, `config.xml` and `sshd_config`. See [Unattended local setup](#unattended-local-setup).
* **Import from URL** — a new repository can be populated from an existing http(s) Git repository (GitHub, GitLab, Azure DevOps, ...) at creation time. All branches and tags are copied; see [How to import an existing repository?](#how-to-import-an-existing-repository-from-a-url).
* **Updated dependencies** — NuGet packages refreshed to current versions, including the SQL provider dependency fix.


Prerequisites
-----------------------------------------------

* Internet Information Services 7 and higher
    * [How to Install IIS 8 on Windows 8](http://www.howtogeek.com/112455/how-to-install-iis-8-on-windows-8/)
    * [Installing IIS 8 on Windows Server 2012](http://www.iis.net/learn/get-started/whats-new-in-iis-8/installing-iis-8-on-windows-server-2012)
    * [Installing IIS 7 on Windows Server 2008 or Windows Server 2008 R2](http://www.iis.net/learn/install/installing-iis-7/installing-iis-7-and-above-on-windows-server-2008-or-windows-server-2008-r2)
    * [Installing IIS 7 on Windows Vista and Windows 7](http://www.iis.net/learn/install/installing-iis-7/installing-iis-on-windows-vista-and-windows-7)
* [.NET Framework 4.6](https://www.microsoft.com/en-gb/download/details.aspx?id=48130)
    * Windows Vista SP2, Windows 7, Windows 8 and higher
    * Windows Server 2008 R2, Windows Server 2008 SP2, Windows Server 2012 and higher
    * Don't forget to register .NET framework with your IIS
        * Run `%windir%\Microsoft.NET\Framework\v4.0.30319\aspnet_regiis.exe -ir` with administrator privileges

<hr />



Update
-----------------------------------------------

Before each update please read carefully the information about **compatibility issues** between your version and the latest one in [changelog](/changelog.md).

* Delete all the files in the installation folder **except App_Data**.
    * Default location is `C:\inetpub\wwwroot\Bonobo.Git.Server`.
* Copy the files from the downloaded archive to the server location.


<hr />



Installation
-----------------------------------------------

These steps illustrate simple installation with Windows 2008 Server and IIS 7. They are exactly the same for higher platforms (Windows Server 2012 and IIS 8.0).

* **Extract the files** from the installation archive to `C:\inetpub\wwwroot`

* **Allow IIS User to modify** `C:\inetpub\wwwroot\Bonobo.Git.Server\App_Data` folder. To do so
    * select Properties of App_Data folder,
    * go to Security tab, 
    * click edit, 
    * select IIS user (in my case IIS_IUSRS) and add Modify and Write permission,
    * confirm these settings with Apply button.

* **Convert Bonobo.Git.Server to Application** in IIS
    * Run IIS Manager and navigate to Sites -> Default Web Site. You should see Bonobo.Git.Server.
    * Right click on Bonobo Git Server and convert to application.
    * Check if the selected application pool runs on .NET 4.0 and convert the site.

* **Launch your browser** and go to [http://localhost/Bonobo.Git.Server](http://localhost/Bonobo.Git.Server). Now you can see the initial page of Bonobo Git Server and everything is working.
    * Default credentials are username: **admin** password: **admin**


<hr />


Frequently Asked Questions
-----------------------------------------------

#### How to clone a repository?

* Go to the **Repository Detail**.
* Copy the value in the **Git Repository Location**.
    * It should look like `http://servername/projectname.git`.
* Go to your command line and run `git clone http://servername/projectname.git`.

#### How to clone over SSH?

SSH cloning is available once an administrator has completed the [SSH access](#ssh-access) setup
and ticked **Offer SSH clone URLs** in Global Settings. Then:

* Generate a key pair on your machine if you don't have one: `ssh-keygen -t ed25519 -C "you@example.com"`.
* Log in, open your **Account** page and click **SSH Keys**.
    * Administrators can open any user's account and manage that user's keys the same way.
* Give the key a name, paste the contents of your **public** key file (`~/.ssh/id_ed25519.pub`, one line) and add it.
    * Accepted types: `ssh-ed25519`, `ssh-rsa` (2048 bits or more), `ecdsa-sha2-nistp256/384/521` and the FIDO `sk-` variants. DSA keys and private keys are refused.
    * A key can belong to only one account; adding a key that is already registered elsewhere is rejected.
    * The list shows each key's fingerprint, type and size, when it was added and when it was last used, with a delete button.
* Go to the **Repository Detail** page and copy the value in the **SSH** location. It looks like `git@servername:projectname.git`.
* Run `git clone git@servername:projectname.git`.

Over SSH you get exactly the permissions your account already has: no separate SSH permission
model, and no anonymous access. Adding or deleting a key takes effect immediately.

#### How to import an existing repository from a URL?

* Go to **Repositories -> Create**.
* Fill in **Import from URL** with the `http://` or `https://` address of the source repository, for example `https://github.com/owner/project.git`.
    * The last segment of the URL is offered as the repository **Name**; change it if you like.
    * Other protocols (`git://`, `ssh://`, local paths) are not accepted.
* For a private repository, fill in **Import user name** and **Import password or token**.
    * For GitHub, leave the user name empty and put a personal access token in the password field.
    * Credentials embedded in the URL (`https://user:token@host/...`) are accepted too.
    * The credentials are used once for the import and are never stored.
* Fill in the remaining fields as for an empty repository and click **Create**.

What happens:

* All branches and tags of the source are fetched into the new bare repository, like `git clone --mirror` without hosting-specific refs such as GitHub's `refs/pull/*`.
* The default branch follows the source's `HEAD`, falling back to `main` then `master`.
* The import is aborted after 30 minutes. On any failure the repository is removed again from disk and from the database, and the error is shown on the form so you can correct it and resubmit.
* The import is a one-time copy, not a mirror that keeps syncing. The source stays registered as the `origin` remote of the bare repository, which lets an administrator re-fetch by hand on the server if needed.

#### How do I change my password?

* Click on the **account settings** in the top right corner.
* Enter new password and confirmation.
* Save.

#### How to backup data?

* Go to the installation folder of Bonobo Git Server on the server.
    * Default location is `C:\inetpub\wwwroot\Bonobo.Git.Server`.
* Copy the content of App_Data folder to your backup directory.
* If you changed the location of your repositories, backup them as well.

#### How to change repositories folder?

* Log in as an administrator.
* Go to **Global Settings**.
* Set the desired value for the **Repository Directory**.
    * Directory must exist on the hard drive.
    * IIS User must have proper permissions to modify the folder.
* Save changes.    

#### Can I allow anonymous access to a repository?

* Edit the desired repository (or do this when creating the repository).
* Check **Anonymous** check box.
* Save.

For allowing anonymous push you have to modify global settings.

* Log in as an administrator.
* Go to **Global Settings**.
* Check the value **Allow push for anonymous repositories**
* Save changes.

#### I'd like to use git hooks to restrict access. How do I access the web frontend usernam?

Bonobo provides the following environment variables:

* `AUTH_USER`: The username used to login. Empty if it was an anonymous operation (clone/push/pull)
* `REMOTE_USER`: Same as `AUTH_USER`
* `AUTH_USER_TEAMS`: A comma-separated list containing all the teams the user belongs to. Commas in teams name are escaped with a backslash. Backslashes are also escaped with a `\`. Example: Teams 'Editors\ Architects', 'Programmers,Testers' will become `Editors\\ Architects,Programmers\,Testers`.
* `AUTH_USER_ROLES`: A comma-separated list containing all the roles the user belongs to. Commas in roles are escaped with a backslash. Backslashes are also escaped with a `\`.
* `AUTH_USER_DISPLAYNAME`: Given Name + Surname if available. Else the username.

**Beware that due to the way HTTP basic authentication works, if anonymous operations (push/pull) are enabled the variables above will always be empty!**

SSH access
-----------------------------------------------

Users can register SSH public keys on their account and clone over `git@server:repository.git`,
in the same way as GitHub. Keys are managed from **Account settings -> SSH Keys**.

IIS cannot serve SSH, so the SSH side is handled by **Windows OpenSSH**, which ships with Windows
Server 2019 and later. Bonobo owns the `authorized_keys` file: every key is written with a forced
command, so authenticating with a Bonobo key can only ever start `Bonobo.Git.Server.SshShell.exe`,
never a shell. That program checks the same permissions the web site uses, and then runs git.

Nothing below is done for you, and SSH stays off until all of it is in place.

#### 1. Install OpenSSH Server

    Add-WindowsCapability -Online -Name OpenSSH.Server~~~~0.0.1.0
    Set-Service -Name sshd -StartupType Automatic
    Start-Service sshd

#### 2. Create the git account

This is the account clients log in as, and the account sshd runs the forced command as. It needs
read access to the repository directory and to the Bonobo database, and nothing else. It does not
need to be an administrator, and it should not be the IIS application pool identity.

    New-LocalUser -Name git -NoPassword -UserMayNotChangePassword
    Set-LocalUser -Name git -PasswordNeverExpires $true

#### 3. Deploy the shell

Copy the build output of `Bonobo.Git.Server.SshShell` to a folder outside the web root, for
example `C:\ProgramData\Bonobo\ssh`. Then edit `Bonobo.Git.Server.SshShell.exe.config` so that
the connection string and the `UserConfiguration`, `GitPath`, `GitHomePath` and `LogDirectory`
settings point at the **same** installation the web site uses. They must all be absolute paths:
the shell runs outside IIS, so there is no site root for a `~\App_Data\...` path to resolve
against.

#### 4. Point sshd at Bonobo's authorized_keys file

In `%ProgramData%\ssh\sshd_config`:

    Match User git
        AuthorizedKeysFile C:\ProgramData\Bonobo\ssh\authorized_keys
        PubkeyAuthentication yes
        PasswordAuthentication no
        AllowTcpForwarding no
        PermitTTY no
        X11Forwarding no

Restart sshd afterwards with `Restart-Service sshd`.

#### 5. Tell Bonobo where those files are

In `web.config`:

    <add key="SshAuthorizedKeysPath" value="C:\ProgramData\Bonobo\ssh\authorized_keys" />
    <add key="SshShellPath" value="C:\ProgramData\Bonobo\ssh\Bonobo.Git.Server.SshShell.exe" />
    <add key="SshServiceAccount" value="git" />

The application pool identity needs write access to the folder holding `authorized_keys`, because
Bonobo rewrites that file whenever a key is added or removed, and once at every start-up.

#### 6. Turn it on

Log in as an administrator, go to **Global Settings**, tick **Offer SSH clone URLs** and set the
**SSH host name** clients should use. Set the port only if sshd is not on 22. Repository pages
will then show an SSH clone URL beside the HTTP one.

#### Unattended local setup

For a **development machine** running the site straight from a source checkout (Visual Studio or
IIS Express), `Setup-LocalSsh.ps1` does steps 1 to 6 for you. It ships in the web project folder
and in the published output. Run it from an elevated PowerShell:

    .\Setup-LocalSsh.ps1
    .\Setup-LocalSsh.ps1 -RepoRoot 'D:\src\Bonobo-Git-Server' -GitAccount gitssh -SshHostName myhost

It is idempotent and safe to re-run after a rebuild. It will:

* install and start Windows OpenSSH Server, falling back to Microsoft's Win32-OpenSSH release
  download when the Windows feature has no payload for the current build;
* create the local `git` account (or the one given with `-GitAccount`);
* build `Bonobo.Git.Server.SshShell` in Debug (skip with `-SkipBuild`) and copy it into the web
  project's `bin\` with an `.exe.config` rewritten to absolute paths into the checkout's `App_Data`;
* create `C:\ProgramData\Bonobo\ssh` for `authorized_keys` and set its ACL, and grant the `git`
  account traverse/read rights on the checkout, `bin\` and `App_Data\`;
* patch `web.config` (`SshAuthorizedKeysPath`, `SshShellPath`, `SshServiceAccount`) and
  `App_Data\config.xml` (absolute `Repositories` path, `SshEnabled`, `SshHost`);
* insert the `Match User git` block into `sshd_config`, keeping a `.bak` copy, validate it with
  `sshd -t` and restart sshd.

Afterwards restart the web app so it regenerates `authorized_keys`, add a key under
**Account settings -> SSH Keys**, and test with `ssh -T git@localhost`: being refused with
"This account is only for git access" means key authentication and the forced command both work.

Review the script before running it: it creates a local Windows account, edits `sshd_config` and
ACLs, and may download from GitHub. It is meant for local setups; a production server should
follow the manual steps above, with the shell deployed outside the web root.

#### Troubleshooting

* **`Permission denied (publickey)`** is almost always the file permissions on `authorized_keys`.
  Windows OpenSSH refuses to read a file that other accounts can write to. Bonobo tries to lock the
  file down to SYSTEM, Administrators, the application pool identity and the account named in
  `SshServiceAccount`, but it can only do that if it owns the file. Check `sshd`'s own log, and if
  necessary fix the ACL by hand once. Setting `SshHardenAuthorizedKeysPermissions` to `false` in
  `web.config` stops Bonobo touching the ACL at all.
* **Everything is refused with "This account is only for git access"** means sshd is invoking the
  shell without the key id argument, i.e. it is not using the generated `authorized_keys`. Check
  the `AuthorizedKeysFile` path and that `StrictModes` is not rejecting the file.
* Bonobo's own view of what happened is in `App_Data\Logs\ssh-*.txt`, written by the shell, and
  `App_Data\Logs\log-*.txt` for the file generation.
* Only `git-upload-pack`, `git-receive-pack` and `git-upload-archive` are accepted. An interactive
  `ssh git@server` is refused by design.

#### Notes

* Keys grant exactly the permissions the owning user already has - there is no separate SSH
  permission model, and a key cannot be shared between two accounts.
* Anonymous access is an HTTP concept and does not apply over SSH: every SSH connection is an
  identified user.
* Hooks receive `AUTH_USER`, `REMOTE_USER`, `AUTH_USER_TEAMS`, `AUTH_USER_ROLES` and
  `AUTH_USER_DISPLAYNAME` exactly as they do over HTTP, and unlike HTTP they are never empty.
* Deleting a user, or deleting a key, rewrites `authorized_keys` immediately.
* ed25519 keys are recommended. RSA keys below 2048 bits and DSA keys are refused.

<hr />



New release
-----------------------------------------------

* update [changelog](https://github.com/jakubgarfield/Bonobo-Git-Server/blob/master/changelog.md)
* update version numbers in [appveyor.yml](https://github.com/jakubgarfield/Bonobo-Git-Server/blob/master/appveyor.yml)
* add tag so it appears under [releases](https://github.com/jakubgarfield/Bonobo-Git-Server/releases) with `git tag -a 6.0.0 -m "Release 6.0.0"`
* add zipped version to bonobogitserver.com at [Bonobo-Git-Server-Web](https://github.com/jakubgarfield/Bonobo-Git-Server-Web)
