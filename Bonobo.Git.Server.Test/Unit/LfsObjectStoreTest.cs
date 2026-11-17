using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Bonobo.Git.Server.Git.Lfs;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bonobo.Git.Server.Test.Unit
{
    [TestClass]
    public class LfsObjectStoreTest
    {
        private string _repository;
        private LfsObjectStore _store;

        [TestInitialize]
        public void Init()
        {
            _repository = Path.Combine(Path.GetTempPath(), "lfs-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_repository);
            _store = new LfsObjectStore(_repository);
        }

        [TestCleanup]
        public void Cleanup()
        {
            Directory.Delete(_repository, true);
        }

        public static string OidOf(byte[] content)
        {
            using (var sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(content)).Replace("-", "").ToLowerInvariant();
            }
        }

        [TestMethod]
        public void IsValidOid_AcceptsOnlyLowercaseSha256Hex()
        {
            Assert.IsTrue(LfsObjectStore.IsValidOid(new string('a', 64)));
            Assert.IsTrue(LfsObjectStore.IsValidOid(OidOf(new byte[] { 1 })));
            Assert.IsFalse(LfsObjectStore.IsValidOid(null));
            Assert.IsFalse(LfsObjectStore.IsValidOid(""));
            Assert.IsFalse(LfsObjectStore.IsValidOid(new string('a', 63)));
            Assert.IsFalse(LfsObjectStore.IsValidOid(new string('a', 65)));
            Assert.IsFalse(LfsObjectStore.IsValidOid(new string('A', 64)));
            Assert.IsFalse(LfsObjectStore.IsValidOid(new string('g', 64)));
            Assert.IsFalse(LfsObjectStore.IsValidOid("..\\..\\" + new string('a', 58)));
            Assert.IsFalse(LfsObjectStore.IsValidOid(new string('a', 63) + "\n"));
        }

        [TestMethod]
        public void GetObjectPath_UsesTheGitLfsLayout()
        {
            string oid = "abcd" + new string('0', 60);
            string expected = Path.Combine(_repository, "lfs", "objects", "ab", "cd", oid);
            Assert.AreEqual(expected, _store.GetObjectPath(oid));
        }

        [TestMethod]
        public void GetObjectPath_RefusesAPathTraversal()
        {
            Assert.Throws<ArgumentException>(() => _store.GetObjectPath("../../../../windows/win.ini"));
        }

        [TestMethod]
        public void Save_StoresAnObjectWhoseHashMatches()
        {
            byte[] content = Encoding.UTF8.GetBytes("hello large file");
            string oid = OidOf(content);

            var result = _store.Save(oid, new MemoryStream(content), content.Length);

            Assert.AreEqual(LfsSaveResult.Stored, result);
            long size;
            Assert.IsTrue(_store.TryGetSize(oid, out size));
            Assert.AreEqual(content.Length, size);
            CollectionAssert.AreEqual(content, File.ReadAllBytes(_store.GetObjectPath(oid)));
        }

        [TestMethod]
        public void Save_WithAnUnknownSize_StillChecksTheHash()
        {
            byte[] content = new byte[300000];   // more than one copy buffer
            new Random(1).NextBytes(content);

            Assert.AreEqual(LfsSaveResult.Stored, _store.Save(OidOf(content), new MemoryStream(content), -1));
        }

        [TestMethod]
        public void Save_RefusesContentThatDoesNotMatchItsOid_AndKeepsNothing()
        {
            byte[] content = Encoding.UTF8.GetBytes("tampered");
            string oid = OidOf(Encoding.UTF8.GetBytes("original"));

            var result = _store.Save(oid, new MemoryStream(content), content.Length);

            Assert.AreEqual(LfsSaveResult.HashMismatch, result);
            long size;
            Assert.IsFalse(_store.TryGetSize(oid, out size));
            Assert.AreEqual(0, Directory.GetFiles(Path.Combine(_repository, "lfs", "tmp")).Length, "no temporary file left behind");
        }

        [TestMethod]
        public void Save_RefusesASizeThatDoesNotMatch()
        {
            byte[] content = Encoding.UTF8.GetBytes("some content");

            var result = _store.Save(OidOf(content), new MemoryStream(content), content.Length + 1);

            Assert.AreEqual(LfsSaveResult.SizeMismatch, result);
            long size;
            Assert.IsFalse(_store.TryGetSize(OidOf(content), out size));
        }

        [TestMethod]
        public void Save_TwiceTheSameObject_IsAnIdempotentSuccess()
        {
            byte[] content = Encoding.UTF8.GetBytes("same twice");
            string oid = OidOf(content);

            Assert.AreEqual(LfsSaveResult.Stored, _store.Save(oid, new MemoryStream(content), content.Length));
            Assert.AreEqual(LfsSaveResult.AlreadyStored, _store.Save(oid, new MemoryStream(content), content.Length));
        }

        [TestMethod]
        public void TryGetSize_IsFalseForAnUnknownOrInvalidOid()
        {
            long size;
            Assert.IsFalse(_store.TryGetSize(new string('b', 64), out size));
            Assert.IsFalse(_store.TryGetSize("not-an-oid", out size));
        }
    }
}
