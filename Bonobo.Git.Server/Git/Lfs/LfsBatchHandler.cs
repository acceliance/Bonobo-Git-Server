using System;
using System.Collections.Generic;

namespace Bonobo.Git.Server.Git.Lfs
{
    /// <summary>
    /// Decides, for each object of a Batch API request, what the client has to do next. No HTTP in here, so it is unit-testable.
    /// </summary>
    public static class LfsBatchHandler
    {
        public const string DownloadOperation = "download";
        public const string UploadOperation = "upload";
        public const string BasicTransfer = "basic";

        /// <summary>A batch above this many objects is refused (the git-lfs client sends batches of 100 by default).</summary>
        public const int MaxObjectsPerBatch = 1000;

        /// <param name="objectsUrl">URL of the objects collection, ending with a slash: {repo}.git/info/lfs/objects/</param>
        public static LfsBatchResponse Process(LfsBatchRequest request, LfsObjectStore store, string objectsUrl)
        {
            var response = new LfsBatchResponse
            {
                Transfer = BasicTransfer,
                HashAlgorithm = "sha256",
                Objects = new List<LfsBatchObject>()
            };

            bool upload = request.Operation == UploadOperation;
            foreach (var spec in request.Objects)
            {
                var result = new LfsBatchObject { Oid = spec.Oid, Size = spec.Size };
                response.Objects.Add(result);

                if (!LfsObjectStore.IsValidOid(spec.Oid) || spec.Size < 0)
                {
                    result.Error = new LfsObjectError { Code = 422, Message = "Invalid object id or size" };
                    continue;
                }

                long storedSize;
                bool exists = store.TryGetSize(spec.Oid, out storedSize);

                if (upload)
                {
                    if (exists && storedSize == spec.Size)
                    {
                        continue;   // already on the server: no action, the client skips it
                    }
                    result.Authenticated = true;
                    result.Actions = new Dictionary<string, LfsAction>
                    {
                        { "upload", new LfsAction { Href = objectsUrl + spec.Oid } },
                        { "verify", new LfsAction { Href = objectsUrl + "verify" } }
                    };
                }
                else
                {
                    if (!exists)
                    {
                        result.Error = new LfsObjectError { Code = 404, Message = "Object does not exist" };
                    }
                    else if (storedSize != spec.Size)
                    {
                        result.Error = new LfsObjectError { Code = 422, Message = "Object size does not match" };
                    }
                    else
                    {
                        result.Authenticated = true;
                        result.Actions = new Dictionary<string, LfsAction>
                        {
                            { "download", new LfsAction { Href = objectsUrl + spec.Oid } }
                        };
                    }
                }
            }
            return response;
        }

        /// <summary>
        /// Asks the client to send this header with every object request. The git-lfs client does not reliably send its
        /// credentials with the upload/download/verify requests (it got a 401 and gave up retrying), so the server hands
        /// back the Authorization header it just received on the batch call. It goes back only to the caller who sent it.
        /// </summary>
        public static void AddHeader(LfsBatchResponse response, string name, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }
            foreach (var obj in response.Objects)
            {
                if (obj.Actions == null)
                {
                    continue;
                }
                foreach (var action in obj.Actions.Values)
                {
                    if (action.Header == null)
                    {
                        action.Header = new Dictionary<string, string>();
                    }
                    action.Header[name] = value;
                }
            }
        }

        /// <summary>Null when the request is acceptable, otherwise the message to send back with HTTP 422.</summary>
        public static string Validate(LfsBatchRequest request)
        {
            if (request == null || request.Objects == null)
            {
                return "Malformed batch request";
            }
            if (request.Operation != DownloadOperation && request.Operation != UploadOperation)
            {
                return "Unknown operation, expected 'download' or 'upload'";
            }
            if (request.Transfers != null && request.Transfers.Count > 0 && !request.Transfers.Contains(BasicTransfer))
            {
                return "Only the 'basic' transfer adapter is supported";
            }
            if (request.Objects.Count > MaxObjectsPerBatch)
            {
                return "Too many objects in one batch (maximum " + MaxObjectsPerBatch + ")";
            }
            foreach (var spec in request.Objects)
            {
                if (spec == null)
                {
                    return "Malformed batch request";
                }
            }
            return null;
        }
    }
}
