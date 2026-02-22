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
            var readmeDir = string.IsNullOrEmpty(readmePath)
                ? string.Empty
                : readmePath.Contains("/")
                    ? readmePath.Substring(0, readmePath.LastIndexOf('/') + 1)
                    : string.Empty;

            html = Regex.Replace(html, @"(<img\b[^>]*\ssrc="")((?!https?://|/|data:)[^""]+)("")", match =>
            {
                var prefix = match.Groups[1].Value;
                var src    = match.Groups[2].Value;
                var suffix = match.Groups[3].Value;

                // Build the full in-repo path relative to the README's directory
                var imagePath = readmeDir + src;

                var rawUrl = urlHelper.Action("Raw", "Repository", new
                {
                    id          = repoId,
                    encodedName = PathEncoder.Encode(branch),
                    encodedPath = PathEncoder.Encode(imagePath, allowSlash: true),
                    display     = true
                });

                return prefix + rawUrl + suffix;
            }, RegexOptions.IgnoreCase);

            // Handle absolute repo-root-relative paths (e.g. /readme/image.png).
            // These start with a single "/" and were skipped by the regex above.
            // Protocol-relative URLs (//) are left untouched.
            html = Regex.Replace(html, @"(<img\b[^>]*\ssrc="")(/(?!/)[^""]+)("")", match =>
            {
                var prefix    = match.Groups[1].Value;
                var src       = match.Groups[2].Value; // e.g. "/readme/image.png"
                var suffix    = match.Groups[3].Value;

                // Strip the leading "/" to obtain the in-repo path from the root.
                var imagePath = src.TrimStart('/');

                var rawUrl = urlHelper.Action("Raw", "Repository", new
                {
                    id          = repoId,
                    encodedName = PathEncoder.Encode(branch),
                    encodedPath = PathEncoder.Encode(imagePath, allowSlash: true),
                    display     = true
                });

                return prefix + rawUrl + suffix;
            }, RegexOptions.IgnoreCase);

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
