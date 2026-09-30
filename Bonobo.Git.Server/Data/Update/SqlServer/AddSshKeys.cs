using System;

namespace Bonobo.Git.Server.Data.Update.SqlServer
{
    public class AddSshKeys : IUpdateScript
    {
        public string Command
        {
            get
            {
                return @"
                    CREATE TABLE SshKey (
                        Id          UNIQUEIDENTIFIER NOT NULL,
                        User_Id     UNIQUEIDENTIFIER NOT NULL,
                        Name        NVARCHAR (255)   NOT NULL,
                        KeyType     NVARCHAR (64)    NOT NULL,
                        Fingerprint NVARCHAR (255)   NOT NULL,
                        PublicKey   NVARCHAR (MAX)   NOT NULL,
                        KeySize     INT              NOT NULL
                                                     CONSTRAINT Def_SshKey_KeySize DEFAULT 0,
                        CreatedAt   DATETIME         NOT NULL,
                        LastUsed    DATETIME         NULL,
                        CONSTRAINT PK_SshKey PRIMARY KEY (Id),
                        CONSTRAINT UNQ_SshKey_Fingerprint UNIQUE (Fingerprint),
                        FOREIGN KEY (
                            User_Id
                        )
                        REFERENCES [User] (Id)
                    );

                    CREATE INDEX IX_SshKey_User ON SshKey (User_Id);";
            }
        }

        public string Precondition
        {
            get
            {
                return @"
            IF EXISTS(SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'SshKey')
                SELECT 0
            ELSE
                SELECT 1
";
            }
        }

        public void CodeAction(BonoboGitServerContext context) { }
    }
}
