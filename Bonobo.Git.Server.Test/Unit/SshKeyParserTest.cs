using Bonobo.Git.Server.Security;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace Bonobo.Git.Server.Test.Unit
{
    /// <summary>
    /// The fingerprints below were computed from the key blobs themselves, and match what
    /// 'ssh-keygen -lf' prints for a key with the same public part.
    /// </summary>
    [TestClass]
    public class SshKeyParserTest
    {
        private const string Ed25519Data = "AAAAC3NzaC1lZDI1NTE5AAAAIAMKERgfJi00O0JJUFdeZWxzeoGIj5adpKuyucDHztXc";
        private const string Ed25519Fingerprint = "SHA256:Nn8ZCCPyVHWGaXFTtHcymOXUZhB3CVamc+RruvKUZEU";

        private const string Rsa2048Data = "AAAAB3NzaC1yc2EAAAADAQABAAABAQCAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAD";
        private const string Rsa2048Fingerprint = "SHA256:gvZxjFBLxzr1iL5d3HfP26dzuvJHI2G2I39ifsIQHIU";

        private const string Rsa1024Data = "AAAAB3NzaC1yc2EAAAADAQABAAAAgQCAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAw==";

        private const string DssData = "AAAAB3NzaC1kc3MAAAABBwAAAAELAAAAAQ0AAAABEQ==";

        [TestMethod]
        public void Ed25519KeyIsAccepted()
        {
            var key = ParseOrFail("ssh-ed25519 " + Ed25519Data + " user@host");

            Assert.AreEqual("ssh-ed25519", key.KeyType);
            Assert.AreEqual(Ed25519Data, key.Base64Data);
            Assert.AreEqual("user@host", key.Comment);
            Assert.AreEqual(Ed25519Fingerprint, key.Fingerprint);
            Assert.AreEqual(256, key.KeySize);
        }

        [TestMethod]
        public void Rsa2048KeyIsAccepted()
        {
            var key = ParseOrFail("ssh-rsa " + Rsa2048Data + " user@host");

            Assert.AreEqual("ssh-rsa", key.KeyType);
            Assert.AreEqual(Rsa2048Fingerprint, key.Fingerprint);
            Assert.AreEqual(2048, key.KeySize);
        }

        [TestMethod]
        public void LegacyMd5FingerprintIsAlsoComputed()
        {
            var key = ParseOrFail("ssh-ed25519 " + Ed25519Data);

            Assert.AreEqual("MD5:77:5a:01:b1:e8:63:5a:70:18:da:83:24:08:18:48:1c", key.LegacyFingerprint);
        }

        [TestMethod]
        public void CommentIsOptional()
        {
            var key = ParseOrFail("ssh-ed25519 " + Ed25519Data);

            Assert.AreEqual(String.Empty, key.Comment);
            Assert.AreEqual("ssh-ed25519 " + Ed25519Data, key.Text);
        }

        [TestMethod]
        public void CommentMayContainSpaces()
        {
            var key = ParseOrFail("ssh-ed25519 " + Ed25519Data + " my work laptop");

            Assert.AreEqual("my work laptop", key.Comment);
        }

        [TestMethod]
        public void SurroundingWhitespaceAndTrailingNewlineAreIgnored()
        {
            // This is exactly what comes out of a copy-and-paste from a .pub file
            var key = ParseOrFail("  ssh-ed25519 " + Ed25519Data + " user@host\r\n");

            Assert.AreEqual(Ed25519Fingerprint, key.Fingerprint);
        }

        [TestMethod]
        public void TextDropsAnythingBeyondTypeKeyAndComment()
        {
            var key = ParseOrFail("ssh-ed25519 " + Ed25519Data + " user@host");

            // What we store is rebuilt from the parsed parts, never echoed back verbatim
            Assert.AreEqual("ssh-ed25519 " + Ed25519Data + " user@host", key.Text);
        }

        [TestMethod]
        public void KeyWithLeadingOptionsIsRejected()
        {
            // Options are how a key could smuggle in its own forced command, so the whole line goes
            AssertRejected("command=\"cmd.exe\" ssh-ed25519 " + Ed25519Data);
        }

        [TestMethod]
        public void PrivateKeyIsRejected()
        {
            AssertRejected("-----BEGIN OPENSSH PRIVATE KEY-----\nabcdef\n-----END OPENSSH PRIVATE KEY-----");
        }

        [TestMethod]
        public void TwoKeysAtOnceAreRejected()
        {
            AssertRejected("ssh-ed25519 " + Ed25519Data + "\nssh-ed25519 " + Ed25519Data);
        }

        [TestMethod]
        public void DsaKeyIsRejected()
        {
            AssertRejected("ssh-dss " + DssData);
        }

        [TestMethod]
        public void SmallRsaKeyIsRejected()
        {
            AssertRejected("ssh-rsa " + Rsa1024Data);
        }

        [TestMethod]
        public void KeyTypeNotMatchingTheBlobIsRejected()
        {
            // An ed25519 blob presented as an RSA key - the fingerprint we showed would be a lie
            AssertRejected("ssh-rsa " + Ed25519Data);
        }

        [TestMethod]
        public void MalformedBase64IsRejected()
        {
            AssertRejected("ssh-ed25519 not!valid!base64");
        }

        [TestMethod]
        public void TruncatedBlobIsRejected()
        {
            // Valid base64, but the length prefix runs off the end of the data
            AssertRejected("ssh-ed25519 AAAAC3Nz");
        }

        [TestMethod]
        public void KeyWithNoDataIsRejected()
        {
            AssertRejected("ssh-ed25519");
        }

        [TestMethod]
        public void EmptyInputIsRejected()
        {
            AssertRejected(null);
            AssertRejected("");
            AssertRejected("    ");
        }

        private static ParsedSshKey ParseOrFail(string input)
        {
            ParsedSshKey key;
            string error;
            Assert.IsTrue(SshKeyParser.TryParse(input, out key, out error), "Key was rejected: {0}", error);
            Assert.IsNotNull(key);
            Assert.IsNull(error);
            return key;
        }

        private static void AssertRejected(string input)
        {
            ParsedSshKey key;
            string error;
            Assert.IsFalse(SshKeyParser.TryParse(input, out key, out error), "Key should have been rejected");
            Assert.IsNull(key);
            Assert.IsFalse(String.IsNullOrEmpty(error), "A rejection must explain itself");
        }
    }
}
