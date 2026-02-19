using Bonobo.Git.Server.Security;
using System;
using System.Web.Mvc;
using Unity;

namespace Bonobo.Git.Server
{
    public class WebAuthorizeRepositoryAttribute : WebAuthorizeAttribute
    {
        [Dependency]
        public IRepositoryPermissionService RepositoryPermissionService { get; set; }

        public bool RequiresRepositoryAdministrator { get; set; }

        public override void OnAuthorization(AuthorizationContext filterContext)
        {
            Guid repoId;
            var urlhelper = new UrlHelper(filterContext.RequestContext);

            if (Guid.TryParse(filterContext.Controller.ControllerContext.RouteData.Values["id"].ToString(), out repoId))
            {
                Guid userId = filterContext.HttpContext.User.Id();

                var requiredAccess = RequiresRepositoryAdministrator
                    ? RepositoryAccessLevel.Administer
                    : RepositoryAccessLevel.Push;

                // Allow anonymous users read-only (Pull) access to repos with AnonymousAccess enabled,
                // bypassing authentication entirely for those repos.
                if (!RequiresRepositoryAdministrator && userId == Guid.Empty)
                {
                    if (RepositoryPermissionService.HasPermission(Guid.Empty, repoId, RepositoryAccessLevel.Pull))
                    {
                        return;
                    }
                    // Anonymous user has no access to this repo — redirect to login
                    filterContext.Result = new RedirectResult(urlhelper.Action("LogOn", "Account"));
                    return;
                }

                // For authenticated users, apply standard auth check first
                base.OnAuthorization(filterContext);
                if (filterContext.Result is HttpUnauthorizedResult)
                {
                    return;
                }

                if (RepositoryPermissionService.HasPermission(userId, repoId, requiredAccess))
                {
                    return;
                }

                filterContext.Result = new RedirectResult(urlhelper.Action("Unauthorized", "Home"));
            }
            else
            {
                // No valid repo id — apply standard auth check
                base.OnAuthorization(filterContext);
                if (filterContext.Result is HttpUnauthorizedResult)
                {
                    return;
                }

                var rd = filterContext.RequestContext.RouteData;
                var action = rd.GetRequiredString("action");
                var controller = rd.GetRequiredString("controller");
                if (action.Equals("index", StringComparison.OrdinalIgnoreCase) && controller.Equals("repository", StringComparison.OrdinalIgnoreCase))
                {
                    filterContext.Result = new RedirectResult(urlhelper.Action("Unauthorized", "Home"));
                }
                else
                {
                    filterContext.Controller.TempData["RepositoryNotFound"] = true;
                    filterContext.Result = new RedirectResult(urlhelper.Action("Index", "Repository"));
                }
            }
        }
    }
}
