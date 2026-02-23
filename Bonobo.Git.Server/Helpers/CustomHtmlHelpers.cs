using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.Web.Mvc.Html;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Routing;
using System.Linq.Expressions;
using Bonobo.Git.Server.Models;
using System.ComponentModel.DataAnnotations;

namespace Bonobo.Git.Server.Helpers
{
    public static class CustomHtmlHelpers
    {
        public static IHtmlString AssemblyVersion(this HtmlHelper helper)
        {
            return MvcHtmlString.Create(Assembly.GetExecutingAssembly().GetName().Version.ToString());
        }

        public static IHtmlString MarkdownToHtml(this HtmlHelper helper, string markdownText)
        {
            return MvcHtmlString.Create(CommonMark.CommonMarkConverter.Convert(markdownText));
        }

        public static IHtmlString MarkdownToHtml(this HtmlHelper helper, string markdownText, Guid repoId, string branch, string readmePath)
        {
            var html = CommonMark.CommonMarkConverter.Convert(markdownText);

            // Rewrite relative image src attributes to use the Raw action so images
            // embedded in README.md render correctly (e.g. ![alt](image.png)).
            // readmePath is the path of the README file itself (e.g. "readme.md" or
            // "docs/readme.md"), so the directory containing it is the base for
            // resolving relative image references.
            var urlHelper = new UrlHelper(helper.ViewContext.RequestContext);

            // Compute the app root (handles virtual directory deployments, e.g. "/myapp/").
            // urlHelper.Content("~/") always returns the correct application-relative root.
            var appRoot = urlHelper.Content("~/").TrimEnd('/');

            var readmeDir = string.IsNullOrEmpty(readmePath)
                ? string.Empty
                : readmePath.Contains("/")
                    ? readmePath.Substring(0, readmePath.LastIndexOf('/') + 1)
                    : string.Empty;

            // Builds the canonical Raw URL for a given in-repo image path.
            // Pattern: {appRoot}/Repository/{repoId}/{branch}/Raw/{imagePath}
            // This avoids RouteUrl() which can return null on route-name mismatches.
            Func<string, string> buildRawUrl = imagePath =>
                string.Format("{0}/Repository/{1}/{2}/Raw/{3}",
                    appRoot,
                    repoId,
                    Uri.EscapeDataString(branch),
                    string.Join("/", imagePath.Split('/').Select(Uri.EscapeDataString)));

            // Single-pass rewrite of ALL image src attributes that need fixing.
            // Handles two cases in one regex to avoid the double-rewrite bug that
            // occurs when two sequential passes are used: the first pass rewrites
            // relative paths to "/Repository/..." and then the second pass
            // incorrectly matches and re-rewrites those already-fixed URLs.
            //
            // Captured group 2 matches either:
            //   (a) a leading "/" followed by a non-"/" char  →  absolute repo-root path, e.g. /readme/rep/img.png
            //   (b) any src that does NOT start with https?:// or / or data:  →  relative path, e.g. image.png
            //
            // Already-rewritten URLs (/Repository/...), external URLs (https://...),
            // data URIs (data:...), and protocol-relative URLs (//...) are all skipped.
            html = Regex.Replace(
                html,
                @"(<img\b[^>]*\ssrc="")(/(?!/)[^""]+ | (?!https?://|/|data:)[^""]+)("")",
                match =>
                {
                    var prefix = match.Groups[1].Value;
                    var src = match.Groups[2].Value.Trim(); // Trim() removes whitespace added by regex alternation spaces
                    var suffix = match.Groups[3].Value;

                    string imagePath;
                    if (src.StartsWith("/"))
                    {
                        // Absolute repo-root-relative: strip leading "/" to get the in-repo path.
                        imagePath = src.TrimStart('/');
                    }
                    else
                    {
                        // Relative: resolve against the directory containing the README file.
                        imagePath = readmeDir + src;
                    }

                    return prefix + buildRawUrl(imagePath) + suffix;
                },
                RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace);

            return MvcHtmlString.Create(html);
        }

        public static MvcHtmlString DisplayEnum(this HtmlHelper helper, Enum e)
        {
            string result = "[[" + e.ToString() + "]]";
            var memberInfo = e.GetType().GetMember(e.ToString()).FirstOrDefault();
            if (memberInfo != null)
            {
                var display = memberInfo.GetCustomAttributes(false)
                    .OfType<DisplayAttribute>()
                    .LastOrDefault();

                if (display != null)
                {
                    result = display.GetName();
                }
            }

            return MvcHtmlString.Create(result);
        }
    }
}