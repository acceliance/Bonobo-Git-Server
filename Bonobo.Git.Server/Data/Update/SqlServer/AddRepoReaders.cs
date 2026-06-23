using System;

namespace Bonobo.Git.Server.Data.Update.SqlServer
{
    public class AddRepoReaders : IUpdateScript
    {
        public string Command
        {
            get
            {
                return @"
                    CREATE TABLE UserRepository_Reader (
                        User_Id       UNIQUEIDENTIFIER NOT NULL,
                        Repository_Id UNIQUEIDENTIFIER NOT NULL,
                        CONSTRAINT UNQ_UserRepository_Reader_12 UNIQUE (
                            User_Id,
                            Repository_Id
                        ),
                        FOREIGN KEY (
                            User_Id
                        )
                        REFERENCES [User] (Id),
                        FOREIGN KEY (
                            Repository_Id
                        )
                        REFERENCES Repository (Id)
                    );";
            }
        }

        public string Precondition
        {
            get
            {
                return @"
            IF EXISTS(SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'UserRepository_Reader')
                SELECT 0
            ELSE
                SELECT 1
";
            }
        }

        public void CodeAction(BonoboGitServerContext context) { }
    }
}
