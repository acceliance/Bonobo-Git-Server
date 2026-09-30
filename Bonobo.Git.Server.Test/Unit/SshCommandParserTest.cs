using Bonobo.Git.Server.Security;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace Bonobo.Git.Server.Test.Unit
{
    [TestClass]
    public class SshCommandParserTest
    {
        [TestMethod]
        public void UploadPackIsAPull()
        {
            var command = ParseOrFail("git-upload-pack 'myrepo.git'");

            Assert.AreEqual("upload-pack", command.Service);
            Assert.AreEqual("myrepo", command.RepositoryName);
            Assert.AreEqual(RepositoryAccessLevel.Pull, command.RequiredAccessLevel);
        }

        [TestMethod]
        public void ReceivePackIsAPush()
        {
            var command = ParseOrFail("git-receive-pack 'myrepo.git'");

            Assert.AreEqual("receive-pack", command.Service);
            Assert.AreEqual(RepositoryAccessLevel.Push, command.RequiredAccessLevel);
        }

        [TestMethod]
        public void UploadArchiveIsAPull()
        {
            var command = ParseOrFail("git-upload-archive 'myrepo.git'");

            Assert.AreEqual("upload-archive", command.Service);
            Assert.AreEqual(RepositoryAccessLevel.Pull, command.RequiredAccessLevel);
        }

        [TestMethod]
        public void SpaceSeparatedFormIsAccepted()
        {
            // Some clients send 'git upload-pack' rather than 'git-upload-pack'
            Assert.AreEqual("upload-pack", ParseOrFail("git upload-pack 'myrepo.git'").Service);
        }

        [TestMethod]
        public void GitSuffixIsOptional()
        {
            Assert.AreEqual("myrepo", ParseOrFail("git-upload-pack 'myrepo'").RepositoryName);
            Assert.AreEqual("myrepo", ParseOrFail("git-upload-pack 'myrepo.git'").RepositoryName);
        }

        [TestMethod]
        public void LeadingSlashIsStripped()
        {
            // 'ssh://git@host/myrepo.git' arrives with the slash still attached
            Assert.AreEqual("myrepo", ParseOrFail("git-upload-pack '/myrepo.git'").RepositoryName);
        }

        [TestMethod]
        public void DoubleQuotesAreAccepted()
        {
            Assert.AreEqual("myrepo", ParseOrFail("git-upload-pack \"myrepo.git\"").RepositoryName);
        }

        [TestMethod]
        public void UnquotedNameIsAccepted()
        {
            Assert.AreEqual("myrepo", ParseOrFail("git-upload-pack myrepo.git").RepositoryName);
        }

        [TestMethod]
        public void NamesWithDotsAndDashesSurvive()
        {
            Assert.AreEqual("my-repo.v2", ParseOrFail("git-upload-pack 'my-repo.v2.git'").RepositoryName);
        }

        [TestMethod]
        public void InteractiveShellIsRefused()
        {
            // An empty SSH_ORIGINAL_COMMAND means 'ssh git@host' with no command at all
            AssertRejected(null);
            AssertRejected("");
        }

        [TestMethod]
        public void ArbitraryCommandsAreRefused()
        {
            AssertRejected("cmd.exe");
            AssertRejected("powershell -c whoami");
            AssertRejected("scp -t /tmp");
            AssertRejected("git gc 'myrepo.git'");
        }

        [TestMethod]
        public void CommandThatMerelyStartsLikeGitIsRefused()
        {
            AssertRejected("git-upload-packet 'myrepo.git'");
        }

        [TestMethod]
        public void PathTraversalIsRefused()
        {
            AssertRejected("git-upload-pack '../../windows/system32'");
            AssertRejected("git-upload-pack '..'");
            AssertRejected("git-upload-pack 'sub/dir.git'");
            AssertRejected(@"git-upload-pack 'sub\dir.git'");
            AssertRejected("git-upload-pack 'C:/repos/other.git'");
        }

        [TestMethod]
        public void ExtraArgumentsAreRefused()
        {
            AssertRejected("git-upload-pack 'one.git' 'two.git'");
            AssertRejected("git-upload-pack one.git two.git");
        }

        [TestMethod]
        public void MissingRepositoryIsRefused()
        {
            AssertRejected("git-upload-pack");
            AssertRejected("git-upload-pack ''");
        }

        private static SshGitCommand ParseOrFail(string command)
        {
            SshGitCommand result;
            string error;
            Assert.IsTrue(SshCommandParser.TryParse(command, out result, out error), "Command was rejected: {0}", error);
            Assert.IsNotNull(result);
            return result;
        }

        private static void AssertRejected(string command)
        {
            SshGitCommand result;
            string error;
            Assert.IsFalse(SshCommandParser.TryParse(command, out result, out error), "Command should have been rejected: {0}", command);
            Assert.IsNull(result);
            Assert.IsFalse(String.IsNullOrEmpty(error), "A rejection must explain itself");
        }
    }
}
