using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Bonobo.Git.Server.Git.Lfs;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;

namespace Bonobo.Git.Server.Test.Unit
{
    [TestClass]
    public class LfsBatchHandlerTest
    {
        private const string ObjectsUrl = "https://git.example.org/SOW.git/info/lfs/objects/";

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

        private string Store(string text)
        {
            byte[] content = Encoding.UTF8.GetBytes(text);
            string oid = LfsObjectStoreTest.OidOf(content);
            _store.Save(oid, new MemoryStream(content), content.Length);
            return oid;
        }

        private static LfsBatchRequest Request(string operation, string oid, long size)
        {
            return new LfsBatchRequest
            {
                Operation = operation,
                Transfers = new List<string> { "basic" },
                Objects = new List<LfsObjectSpec> { new LfsObjectSpec { Oid = oid, Size = size } }
            };
        }

        [TestMethod]
        public void Upload_OfANewObject_GivesUploadAndVerifyActions()
        {
            string oid = new string('a', 64);

            var response = LfsBatchHandler.Process(Request("upload", oid, 10), _store, ObjectsUrl);

            Assert.AreEqual("basic", response.Transfer);
            var obj = response.Objects[0];
            Assert.IsNull(obj.Error);
            Assert.AreEqual(ObjectsUrl + oid, obj.Actions["upload"].Href);
            Assert.AreEqual(ObjectsUrl + "verify", obj.Actions["verify"].Href);
        }

        [TestMethod]
        public void Upload_OfAnObjectAlreadyOnTheServer_GivesNoAction()
        {
            string oid = Store("already here");

            var response = LfsBatchHandler.Process(Request("upload", oid, "already here".Length), _store, ObjectsUrl);

            Assert.IsNull(response.Objects[0].Actions);
            Assert.IsNull(response.Objects[0].Error);
        }

        [TestMethod]
        public void Upload_OfAnObjectStoredWithAnotherSize_IsUploadedAgain()
        {
            string oid = Store("stored");

            var response = LfsBatchHandler.Process(Request("upload", oid, 999), _store, ObjectsUrl);

            Assert.IsNotNull(response.Objects[0].Actions["upload"]);
        }

        [TestMethod]
        public void Download_OfAKnownObject_GivesADownloadAction()
        {
            string oid = Store("download me");

            var response = LfsBatchHandler.Process(Request("download", oid, "download me".Length), _store, ObjectsUrl);

            Assert.AreEqual(ObjectsUrl + oid, response.Objects[0].Actions["download"].Href);
            Assert.IsFalse(response.Objects[0].Actions.ContainsKey("upload"));
        }

        [TestMethod]
        public void Download_OfAnUnknownObject_Is404ForThatObject()
        {
            var response = LfsBatchHandler.Process(Request("download", new string('c', 64), 5), _store, ObjectsUrl);

            Assert.AreEqual(404, response.Objects[0].Error.Code);
            Assert.IsNull(response.Objects[0].Actions);
        }

        [TestMethod]
        public void Download_WithAWrongSize_Is422ForThatObject()
        {
            string oid = Store("sized");

            var response = LfsBatchHandler.Process(Request("download", oid, 1), _store, ObjectsUrl);

            Assert.AreEqual(422, response.Objects[0].Error.Code);
        }

        [TestMethod]
        public void AnInvalidObjectId_IsAnErrorForThatObjectOnly()
        {
            var request = Request("upload", "../../etc/passwd", 1);
            request.Objects.Add(new LfsObjectSpec { Oid = new string('d', 64), Size = 1 });

            var response = LfsBatchHandler.Process(request, _store, ObjectsUrl);

            Assert.AreEqual(422, response.Objects[0].Error.Code);
            Assert.IsNotNull(response.Objects[1].Actions["upload"]);
        }

        [TestMethod]
        public void Validate_AcceptsAWellFormedRequest()
        {
            Assert.IsNull(LfsBatchHandler.Validate(Request("upload", new string('a', 64), 1)));
            Assert.IsNull(LfsBatchHandler.Validate(Request("download", new string('a', 64), 1)));
        }

        [TestMethod]
        public void Validate_AcceptsARequestWithoutTransfers()
        {
            var request = Request("download", new string('a', 64), 1);
            request.Transfers = null;
            Assert.IsNull(LfsBatchHandler.Validate(request));
        }

        [TestMethod]
        public void Validate_RefusesBadRequests()
        {
            Assert.IsNotNull(LfsBatchHandler.Validate(null));
            Assert.IsNotNull(LfsBatchHandler.Validate(new LfsBatchRequest { Operation = "upload" }), "no objects");
            Assert.IsNotNull(LfsBatchHandler.Validate(Request("delete", new string('a', 64), 1)), "unknown operation");

            var tus = Request("upload", new string('a', 64), 1);
            tus.Transfers = new List<string> { "tus" };
            Assert.IsNotNull(LfsBatchHandler.Validate(tus), "only the basic transfer exists");

            var tooMany = Request("upload", new string('a', 64), 1);
            for (int i = 0; i < LfsBatchHandler.MaxObjectsPerBatch; i++)
            {
                tooMany.Objects.Add(new LfsObjectSpec { Oid = new string('a', 64), Size = 1 });
            }
            Assert.IsNotNull(LfsBatchHandler.Validate(tooMany));
        }

        [TestMethod]
        public void TheClientJson_IsReadAndTheResponseJson_HasTheDocumentedShape()
        {
            string oid = new string('e', 64);
            string json = "{\"operation\":\"upload\",\"transfers\":[\"basic\"],\"ref\":{\"name\":\"refs/heads/master\"},"
                          + "\"objects\":[{\"oid\":\"" + oid + "\",\"size\":123}]}";

            var request = JsonConvert.DeserializeObject<LfsBatchRequest>(json);
            var response = LfsBatchHandler.Process(request, _store, ObjectsUrl);
            string output = JsonConvert.SerializeObject(response, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
            var parsed = Newtonsoft.Json.Linq.JObject.Parse(output);

            Assert.AreEqual("basic", (string)parsed["transfer"]);
            Assert.AreEqual("sha256", (string)parsed["hash_algo"]);
            Assert.AreEqual(oid, (string)parsed["objects"][0]["oid"]);
            Assert.AreEqual(123, (long)parsed["objects"][0]["size"]);
            Assert.AreEqual(ObjectsUrl + oid, (string)parsed["objects"][0]["actions"]["upload"]["href"]);
            Assert.IsNull(parsed["objects"][0]["error"], "null fields are left out");
        }
    }
}
