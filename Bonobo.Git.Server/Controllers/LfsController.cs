using Bonobo.Git.Server.Configuration;
using Bonobo.Git.Server.Git.Lfs;
using Bonobo.Git.Server.Security;
using Newtonsoft.Json;
using Serilog;
using System;
using System.IO;
using System.Text;
using System.Web.Mvc;
using Unity;
using Repository = LibGit2Sharp.Repository;

namespace Bonobo.Git.Server.Controllers
{
    /// <summary>
    /// Git LFS server (Batch API + "basic" transfer), so that large files can be pushed to and pulled from a repository.
    /// The git-lfs client derives the URL from the remote: https://host/repo.git  ->  https://host/repo.git/info/lfs
    /// Same authentication ([GitAuthorize], HTTP basic) and same permissions as git itself:
    /// downloading needs Pull access, uploading needs Push access.
    /// </summary>
    [GitAuthorize]
    [RepositoryNameNormalizer("repositoryName")]
    public class LfsController : Controller
    {
        private const string LfsContentType = "application/vnd.git-lfs+json";

        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore
        };

        [Dependency]
        public IRepositoryPermissionService RepositoryPermissionService { get; set; }

        [HttpPost]
        public ActionResult Batch(string repositoryName)
        {
            LfsBatchRequest request = ReadJson<LfsBatchRequest>();
            string problem = LfsBatchHandler.Validate(request);
            if (problem != null)
            {
                return Error(422, problem);
            }

            var level = request.Operation == LfsBatchHandler.UploadOperation ? RepositoryAccessLevel.Push : RepositoryAccessLevel.Pull;
            ActionResult denied = CheckAccess(repositoryName, level);
            if (denied != null)
            {
                return denied;
            }

            // The request URL is .../info/lfs/objects/batch: the object URLs are its siblings
            string path = Request.Url.AbsolutePath;
            string objectsUrl = Request.Url.GetLeftPart(UriPartial.Authority) + path.Substring(0, path.Length - "batch".Length);

            var response = LfsBatchHandler.Process(request, StoreOf(repositoryName), objectsUrl);
            return Json(200, response);
        }

        [HttpPut]
        public ActionResult Upload(string repositoryName, string oid)
        {
            ActionResult denied = CheckAccess(repositoryName, RepositoryAccessLevel.Push);
            if (denied != null)
            {
                return denied;
            }
            if (!LfsObjectStore.IsValidOid(oid))
            {
                return Error(422, "Invalid object id");
            }

            // Large objects: no buffering, no request length limit (the IIS limit is maxAllowedContentLength in web.config)
            Stream body = Request.GetBufferlessInputStream(disableMaxRequestLength: true);
            long expectedSize = Request.ContentLength > 0 ? Request.ContentLength : -1;

            LfsSaveResult result = StoreOf(repositoryName).Save(oid, body, expectedSize);
            switch (result)
            {
                case LfsSaveResult.HashMismatch:
                    Log.Warning("LfsC: upload of {Oid} to {RepositoryName} refused, the content does not match its id", oid, repositoryName);
                    return Error(422, "The content does not match the object id (sha256)");
                case LfsSaveResult.SizeMismatch:
                    Log.Warning("LfsC: upload of {Oid} to {RepositoryName} refused, size mismatch", oid, repositoryName);
                    return Error(422, "The content size does not match");
                default:
                    Log.Information("LfsC: {Oid} {Result} in {RepositoryName}", oid, result, repositoryName);
                    return new HttpStatusCodeResult(200);
            }
        }

        [HttpGet]
        public ActionResult Download(string repositoryName, string oid)
        {
            ActionResult denied = CheckAccess(repositoryName, RepositoryAccessLevel.Pull);
            if (denied != null)
            {
                return denied;
            }

            long size;
            var store = StoreOf(repositoryName);
            if (!LfsObjectStore.IsValidOid(oid) || !store.TryGetSize(oid, out size))
            {
                return Error(404, "Object does not exist");
            }
            return new FilePathResult(store.GetObjectPath(oid), "application/octet-stream");
        }

        /// <summary>Called by the client after an upload, to check the server really holds the object.</summary>
        [HttpPost]
        public ActionResult Verify(string repositoryName)
        {
            ActionResult denied = CheckAccess(repositoryName, RepositoryAccessLevel.Push);
            if (denied != null)
            {
                return denied;
            }

            LfsObjectSpec spec = ReadJson<LfsObjectSpec>();
            long size;
            if (spec == null || !LfsObjectStore.IsValidOid(spec.Oid))
            {
                return Error(422, "Malformed verify request");
            }
            if (!StoreOf(repositoryName).TryGetSize(spec.Oid, out size))
            {
                return Error(404, "Object does not exist");
            }
            if (size != spec.Size)
            {
                return Error(422, "Object size does not match");
            }
            return new HttpStatusCodeResult(200);
        }

        /// <summary>Null when the caller may do this, otherwise the error to send.</summary>
        private ActionResult CheckAccess(string repositoryName, RepositoryAccessLevel level)
        {
            if (!RepositoryIsValid(repositoryName))
            {
                return Error(404, "Repository not found");
            }
            if (RepositoryPermissionService.HasPermission(User.Id(), repositoryName, level))
            {
                return null;
            }

            Log.Warning("LfsC: User {UserId} doesn't have permission {Permission} on repo {RepositoryName}", User.Id(), level, repositoryName);
            if (User.Id() == Guid.Empty)
            {
                // Anonymous: ask the client for credentials and try again
                Response.AddHeader("WWW-Authenticate", "Basic realm=\"Bonobo Git\"");
                return Error(401, "Credentials needed");
            }
            return Error(403, "You do not have access to this repository");
        }

        private static string RepositoryDirectory(string repositoryName)
        {
            return Path.Combine(UserConfiguration.Current.Repositories, repositoryName);
        }

        private static LfsObjectStore StoreOf(string repositoryName)
        {
            return new LfsObjectStore(RepositoryDirectory(repositoryName));
        }

        private static bool RepositoryIsValid(string repositoryName)
        {
            // The name comes from the URL: it must be one folder name inside the repositories directory, nothing else
            string root = Path.GetFullPath(UserConfiguration.Current.Repositories).TrimEnd(Path.DirectorySeparatorChar);
            string directory = Path.GetFullPath(RepositoryDirectory(repositoryName));
            bool inside = string.Equals(Path.GetDirectoryName(directory), root, StringComparison.OrdinalIgnoreCase);
            return inside && Repository.IsValid(directory);
        }

        private T ReadJson<T>() where T : class
        {
            try
            {
                using (var reader = new StreamReader(Request.InputStream, Encoding.UTF8))
                {
                    return JsonConvert.DeserializeObject<T>(reader.ReadToEnd());
                }
            }
            catch (JsonException ex)
            {
                Log.Warning(ex, "LfsC: unreadable JSON body");
                return null;
            }
        }

        private ActionResult Error(int statusCode, string message)
        {
            return Json(statusCode, new LfsErrorResponse { Message = message });
        }

        private ActionResult Json(int statusCode, object body)
        {
            Response.StatusCode = statusCode;
            Response.TrySkipIisCustomErrors = true;   // keep our JSON body instead of the IIS error page
            return new ContentResult
            {
                Content = JsonConvert.SerializeObject(body, JsonSettings),
                ContentType = LfsContentType,
                ContentEncoding = new UTF8Encoding(false)
            };
        }

        protected override void OnException(ExceptionContext filterContext)
        {
            Log.Error(filterContext.Exception, "Error caught in LfsController");
            filterContext.ExceptionHandled = true;

            var response = filterContext.HttpContext.Response;
            response.Clear();
            response.StatusCode = 500;
            response.TrySkipIisCustomErrors = true;
            filterContext.Result = new ContentResult
            {
                Content = JsonConvert.SerializeObject(new LfsErrorResponse { Message = "Internal server error" }, JsonSettings),
                ContentType = LfsContentType
            };
        }
    }
}
