using System;

namespace Bonobo.Git.Server.Data.Update.Sqlite
{
    public class AddSshKeys : IUpdateScript
    {
        public string Command
        {
            get
            {
                return @"
                    CREATE TABLE IF NOT EXISTS [SshKey] (
                        [Id] Char(36) Not Null,
                        [User_Id] Char(36) Not Null,
                        [Name] VarChar(255) Not Null,
                        [KeyType] VarChar(64) Not Null,
                        [Fingerprint] VarChar(255) Not Null,
                        [PublicKey] Text Not Null,
                        [KeySize] Integer Not Null Default 0,
                        [CreatedAt] DateTime Not Null,
                        [LastUsed] DateTime Null,
                        Constraint [PK_SshKey] Primary Key ([Id]),
                        Foreign Key ([User_Id]) References [User]([Id])
                    );

                    CREATE UNIQUE INDEX IF NOT EXISTS [UNQ_SshKey_Fingerprint] ON [SshKey] ([Fingerprint]);
                    CREATE INDEX IF NOT EXISTS [IX_SshKey_User] ON [SshKey] ([User_Id]);";
            }
        }

        public string Precondition
        {
            get { return null; }
        }

        public void CodeAction(BonoboGitServerContext context) { }
    }
}
