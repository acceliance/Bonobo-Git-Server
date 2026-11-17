using System.Collections.Generic;
using Newtonsoft.Json;

namespace Bonobo.Git.Server.Git.Lfs
{
    /// <summary>
    /// JSON shapes of the Git LFS Batch API (https://github.com/git-lfs/git-lfs/blob/main/docs/api/batch.md).
    /// Only the "basic" transfer adapter is implemented.
    /// </summary>
    public class LfsObjectSpec
    {
        [JsonProperty("oid")]
        public string Oid { get; set; }

        [JsonProperty("size")]
        public long Size { get; set; }
    }

    public class LfsBatchRequest
    {
        [JsonProperty("operation")]
        public string Operation { get; set; }

        [JsonProperty("transfers")]
        public List<string> Transfers { get; set; }

        [JsonProperty("objects")]
        public List<LfsObjectSpec> Objects { get; set; }
    }

    public class LfsAction
    {
        [JsonProperty("href")]
        public string Href { get; set; }

        /// <summary>Headers the client must send with this request (the Authorization header, see LfsBatchHandler.AddHeader).</summary>
        [JsonProperty("header")]
        public System.Collections.Generic.Dictionary<string, string> Header { get; set; }
    }

    public class LfsObjectError
    {
        [JsonProperty("code")]
        public int Code { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }
    }

    public class LfsBatchObject
    {
        [JsonProperty("oid")]
        public string Oid { get; set; }

        [JsonProperty("size")]
        public long Size { get; set; }

        [JsonProperty("authenticated", NullValueHandling = NullValueHandling.Ignore)]
        public bool? Authenticated { get; set; }

        [JsonProperty("actions", NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, LfsAction> Actions { get; set; }

        [JsonProperty("error", NullValueHandling = NullValueHandling.Ignore)]
        public LfsObjectError Error { get; set; }
    }

    public class LfsBatchResponse
    {
        [JsonProperty("transfer")]
        public string Transfer { get; set; }

        [JsonProperty("objects")]
        public List<LfsBatchObject> Objects { get; set; }

        [JsonProperty("hash_algo")]
        public string HashAlgorithm { get; set; }
    }

    public class LfsErrorResponse
    {
        [JsonProperty("message")]
        public string Message { get; set; }
    }
}
