using Bonobo.Git.Server.Git;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bonobo.Git.Server.Test.Unit
{
    [TestClass]
    public class RepositoryImporterTest
    {
        [TestMethod]
        public void HttpsUrlIsAccepted()
        {
            var source = ParseOrFail("https://github.com/owner/project.git");

            Assert.AreEqual("https://github.com/owner/project.git", source.Url);
            Assert.IsFalse(source.HasCredentials);
        }

        [TestMethod]
        public void HttpUrlIsAccepted()
        {
            ParseOrFail("http://intranet/git/project.git");
        }

        [TestMethod]
        public void SurroundingWhitespaceIsIgnored()
        {
            Assert.AreEqual("https://github.com/owner/project", ParseOrFail("  https://github.com/owner/project ").Url);
        }

        [TestMethod]
        public void LocalPathsAreRefused()
        {
            // Would let any user allowed to create a repository copy any repository on the server
            AssertRefused(@"C:\repos\secret.git");
            AssertRefused("file:///C:/repos/secret.git");
            AssertRefused(@"\\server\share\secret.git");
        }

        [TestMethod]
        public void OtherSchemesAreRefused()
        {
            AssertRefused("ssh://git@github.com/owner/project.git");
            AssertRefused("git@github.com:owner/project.git");
            AssertRefused("git://github.com/owner/project.git");
            AssertRefused("ftp://example.com/project.git");
            AssertRefused("ext::sh -c touch% /tmp/pwned");
        }

        [TestMethod]
        public void EmptyOrRelativeUrlsAreRefused()
        {
            AssertRefused(null);
            AssertRefused("");
            AssertRefused("   ");
            AssertRefused("owner/project.git");
        }

        [TestMethod]
        public void CredentialsAreMovedOutOfTheUrl()
        {
            var source = ParseOrFail("https://bob:t%40ken@github.com/owner/project.git");

            Assert.AreEqual("https://github.com/owner/project.git", source.Url);
            Assert.AreEqual("bob", source.Username);
            Assert.AreEqual("t@ken", source.Password);
        }

        [TestMethod]
        public void ExplicitCredentialsWinOverTheUrl()
        {
            ImportSource source;
            Assert.IsTrue(RepositoryImporter.TryParseSource("https://bob:old@github.com/owner/project.git", "alice", "new", out source));

            Assert.AreEqual("alice", source.Username);
            Assert.AreEqual("new", source.Password);
            Assert.AreEqual("https://github.com/owner/project.git", source.Url);
        }

        [TestMethod]
        public void TokenWithoutUserNameGetsAPlaceholderUserName()
        {
            ImportSource source;
            Assert.IsTrue(RepositoryImporter.TryParseSource("https://github.com/owner/project.git", "", "token", out source));

            Assert.AreEqual(RepositoryImporter.TokenUsername, source.Username);
            Assert.AreEqual("token", source.Password);
        }

        private static ImportSource ParseOrFail(string url)
        {
            ImportSource source;
            Assert.IsTrue(RepositoryImporter.TryParseSource(url, null, null, out source), url);
            return source;
        }

        private static void AssertRefused(string url)
        {
            ImportSource source;
            Assert.IsFalse(RepositoryImporter.TryParseSource(url, null, null, out source), url);
            Assert.IsNull(source, url);
        }
    }
}
