using Bonobo.Git.Server.Models;
using Bonobo.Git.Server.Security;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Bonobo.Git.Server.Test.Unit
{
    /// <summary>
    /// The generated file is what actually grants access, so these tests are about the two things
    /// that would be dangerous to get wrong: every key must carry the forced command, and a Windows
    /// path must survive sshd's own unescaping of the option value.
    /// </summary>
    [TestClass]
    public class AuthorizedKeysSynchronizerTest
    {
        private const string ShellPath = @"C:\Program Files\Bonobo\Bonobo.Git.Server.SshShell.exe";
        private const string PublicKey = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIAMKERgfJi00O0JJUFdeZWxzeoGIj5adpKuyucDHztXc user@host";

        [TestMethod]
        public void EmptyKeyListProducesNoKeyLines()
        {
            var content = AuthorizedKeysSynchronizer.BuildFileContent(ShellPath, new List<SshKeyModel>());

            Assert.IsFalse(KeyLines(content).Any());
        }

        [TestMethod]
        public void FileStartsWithAWarningComment()
        {
            var content = AuthorizedKeysSynchronizer.BuildFileContent(ShellPath, new List<SshKeyModel>());

            Assert.IsTrue(content.StartsWith("#"), "The file should announce that it is generated");
        }

        [TestMethod]
        public void EachKeyGetsExactlyOneLine()
        {
            var content = AuthorizedKeysSynchronizer.BuildFileContent(ShellPath, new[]
            {
                Key("Laptop", new DateTime(2024, 1, 1)),
                Key("Desktop", new DateTime(2024, 1, 2)),
                Key("Build server", new DateTime(2024, 1, 3)),
            });

            Assert.AreEqual(3, KeyLines(content).Count());
        }

        [TestMethod]
        public void EveryLineCarriesTheForcedCommandAndTheKeyId()
        {
            var key = Key("Laptop", new DateTime(2024, 1, 1));

            var line = KeyLines(AuthorizedKeysSynchronizer.BuildFileContent(ShellPath, new[] { key })).Single();

            StringAssert.StartsWith(line, "command=\"");
            StringAssert.Contains(line, key.Id.ToString("D"));
        }

        [TestMethod]
        public void EveryLineDisablesForwardingAndPtyAllocation()
        {
            var line = KeyLines(AuthorizedKeysSynchronizer.BuildFileContent(ShellPath, new[] { Key("Laptop", DateTime.UtcNow) })).Single();

            StringAssert.Contains(line, "no-port-forwarding");
            StringAssert.Contains(line, "no-agent-forwarding");
            StringAssert.Contains(line, "no-X11-forwarding");
            StringAssert.Contains(line, "no-pty");
        }

        [TestMethod]
        public void WindowsPathIsEscapedSoThatSshdReadsItBack()
        {
            var key = Key("Laptop", DateTime.UtcNow);

            var line = KeyLines(AuthorizedKeysSynchronizer.BuildFileContent(ShellPath, new[] { key })).Single();

            // sshd unescapes \\ to \ and \" to " inside the quoted option, so the path arrives doubled...
            StringAssert.Contains(line, @"C:\\Program Files\\Bonobo\\Bonobo.Git.Server.SshShell.exe");

            // ...and once sshd has unescaped it, the command is the quoted path plus the key id. The
            // inner quotes matter, because the path contains a space.
            Assert.AreEqual("\"" + ShellPath + "\" " + key.Id.ToString("D"), Unescape(OptionValue(line)));
        }

        [TestMethod]
        public void PublicKeyIsWrittenAfterTheOptions()
        {
            var line = KeyLines(AuthorizedKeysSynchronizer.BuildFileContent(ShellPath, new[] { Key("Laptop", DateTime.UtcNow) })).Single();

            StringAssert.EndsWith(line, " " + PublicKey);
        }

        [TestMethod]
        public void KeysAreWrittenOldestFirst()
        {
            var oldest = Key("Oldest", new DateTime(2024, 1, 1));
            var newest = Key("Newest", new DateTime(2024, 6, 1));

            var lines = KeyLines(AuthorizedKeysSynchronizer.BuildFileContent(ShellPath, new[] { newest, oldest })).ToList();

            StringAssert.Contains(lines[0], oldest.Id.ToString("D"));
            StringAssert.Contains(lines[1], newest.Id.ToString("D"));
        }

        [TestMethod]
        public void LinesUseUnixEndings()
        {
            var content = AuthorizedKeysSynchronizer.BuildFileContent(ShellPath, new[] { Key("Laptop", DateTime.UtcNow) });

            Assert.IsFalse(content.Contains("\r"), "OpenSSH reads this file, so it should not contain carriage returns");
        }

        private static SshKeyModel Key(string name, DateTime createdAt)
        {
            return new SshKeyModel
            {
                Id = Guid.NewGuid(),
                UserId = Guid.NewGuid(),
                Name = name,
                KeyType = "ssh-ed25519",
                PublicKey = PublicKey,
                Fingerprint = "SHA256:Nn8ZCCPyVHWGaXFTtHcymOXUZhB3CVamc+RruvKUZEU",
                KeySize = 256,
                CreatedAt = createdAt,
            };
        }

        private static IEnumerable<string> KeyLines(string content)
        {
            return content.Split('\n').Where(l => l.Length > 0 && !l.StartsWith("#"));
        }

        /// <summary>The contents of command="..." , still escaped</summary>
        private static string OptionValue(string line)
        {
            var start = line.IndexOf('"') + 1;
            var end = line.IndexOf("\",", start, StringComparison.Ordinal);
            return line.Substring(start, end - start);
        }

        /// <summary>The unescaping sshd performs when it reads a quoted option</summary>
        private static string Unescape(string value)
        {
            return value.Replace("\\\"", "\"").Replace("\\\\", "\\");
        }
    }
}
