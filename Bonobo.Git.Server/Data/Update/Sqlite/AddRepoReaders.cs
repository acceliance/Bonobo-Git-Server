using System;

namespace Bonobo.Git.Server.Data.Update.Sqlite
{
    public class AddRepoReaders : IUpdateScript
    {
        public string Command
        {
            get
            {
                return @"
                    CREATE TABLE IF NOT EXISTS [UserRepository_Reader] (
                        [User_Id] Char(36) Not Null,
                        [Repository_Id] Char(36) Not Null,
                        Constraint [UNQ_UserRepository_Reader_1] Unique ([User_Id], [Repository_Id]),
                        Foreign Key ([User_Id]) References [User]([Id]),
                        Foreign Key ([Repository_Id]) References [Repository]([Id])
                    );";
            }
        }

        public string Precondition
        {
            get { return null; }
        }

        public void CodeAction(BonoboGitServerContext context) { }
    }
}
