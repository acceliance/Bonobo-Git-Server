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
[![Dependencies](https://img.shields.io/badge/NuGet-dependencies%20updated-success)](#)

* **Mobile responsive UI** — layout and stylesheet reworked so the web frontend is usable on small screens.
* **SVG support** — SVG files are served and displayed in the repository browser.
* **Markdown image rendering** — absolute and repository-relative image paths in `README.md` now resolve correctly in the Blob and repository views (URLs generated through the `RepositoryRaw` route, including the repository UUID).
* **Anonymous access** — an anonymous browsing mode for repositories.
* **Reader profile** — a read-only user profile.
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

New release
-----------------------------------------------

* update [changelog](https://github.com/jakubgarfield/Bonobo-Git-Server/blob/master/changelog.md)
* update version numbers in [appveyor.yml](https://github.com/jakubgarfield/Bonobo-Git-Server/blob/master/appveyor.yml)
* add tag so it appears under [releases](https://github.com/jakubgarfield/Bonobo-Git-Server/releases) with `git tag -a 6.0.0 -m "Release 6.0.0"`
* add zipped version to bonobogitserver.com at [Bonobo-Git-Server-Web](https://github.com/jakubgarfield/Bonobo-Git-Server-Web)
