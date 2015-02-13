using System;
using System.Linq;
using System.Security.Principal;
using System.Web;
using System.Web.Mvc;

namespace Aliencube.AuthorizeAttribute.Extended.Tests
{
    /// <summary>
    /// This represents the attribute entity for authentication.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true, AllowMultiple = true)]
    public class AuthenticateAttribute : FilterAttribute, IAuthorizationFilter
    {
        private static readonly char[] _splitParameter = new[] { ',' };
        private readonly object _typeId = new object();

        private string _users;
        private string[] _usersSplit = new string[0];

        /// <summary>
        /// Gets the unique identifier for this attribute.
        /// </summary>
        public override object TypeId
        {
            get { return _typeId; }
        }

        /// <summary>
        /// Gets or sets the users that are authenticated.
        /// </summary>
        public string Users
        {
            get { return _users ?? String.Empty; }
            set
            {
                _users = value;
                _usersSplit = SplitString(value);
            }
        }

        /// <summary>
        /// Gets the value that specified whether to be authenticated or not.
        /// </summary>
        protected internal bool IsAuthenticated { get; private set; }

        /// <summary>
        /// When overridden, provides an entry point for custom authentication checks.
        /// </summary>
        /// <param name="httpContext">The HTTP context, which encapsulates all HTTP-specific information about an individual HTTP request.</param>
        /// <returns>Returns <c>True</c>, if the user is authenticated; otherwise returns <c>False</c>.</returns>
        /// <remarks>This method must be thread-safe since it is called by the thread-safe OnCacheAuthorization() method.</remarks>
        protected virtual bool AuthenticateCore(HttpContextBase httpContext)
        {
            if (httpContext == null)
            {
                throw new ArgumentNullException("httpContext");
            }

            IPrincipal user = httpContext.User;
            if (!user.Identity.IsAuthenticated)
            {
                return false;
            }

            if (_usersSplit.Length > 0 && !_usersSplit.Contains(user.Identity.Name, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }

        private void CacheValidateHandler(HttpContext context, object data, ref HttpValidationStatus validationStatus)
        {
            validationStatus = OnCacheAuthorization(new HttpContextWrapper(context));
        }

        /// <summary>
        /// Called when a process requests authorization.
        /// </summary>
        /// <param name="filterContext">The filter context, which encapsulates information for using System.Web.Mvc.AuthorizeAttribute.</param>
        public virtual void OnAuthorization(AuthorizationContext filterContext)
        {
            if (filterContext == null)
            {
                throw new ArgumentNullException("filterContext");
            }

            if (OutputCacheAttribute.IsChildActionCacheActive(filterContext))
            {
                // If a child action cache block is active, we need to fail immediately, even if authorization
                // would have succeeded. The reason is that there's no way to hook a callback to rerun
                // authorization before the fragment is served from the cache, so we can't guarantee that this
                // filter will be re-run on subsequent requests.
                throw new InvalidOperationException("AuthorizeAttribute cannot be used within a child action caching block.");
            }

            bool skipAuthorization = filterContext.ActionDescriptor.IsDefined(typeof(AllowAnonymousAttribute), inherit: true)
                                     || filterContext.ActionDescriptor.ControllerDescriptor.IsDefined(typeof(AllowAnonymousAttribute), inherit: true);

            if (skipAuthorization)
            {
                return;
            }

            if (this.AuthenticateCore(filterContext.HttpContext))
            {
                this.IsAuthenticated = true;

                // ** IMPORTANT **
                // Since we're performing authorization at the action level, the authorization code runs
                // after the output caching module. In the worst case this could allow an authorized user
                // to cause the page to be cached, then an unauthorized user would later be served the
                // cached page. We work around this by telling proxies not to cache the sensitive page,
                // then we hook our custom authorization code into the caching mechanism so that we have
                // the final say on whether a page should be served from the cache.

                HttpCachePolicyBase cachePolicy = filterContext.HttpContext.Response.Cache;
                cachePolicy.SetProxyMaxAge(new TimeSpan(0));
                cachePolicy.AddValidationCallback(CacheValidateHandler, null /* data */);
            }
            else
            {
                this.IsAuthenticated = false;

                HandleUnauthorizedRequest(filterContext);
            }
        }

        /// <summary>
        /// Processes HTTP requests that fail authentication.
        /// </summary>
        /// <param name="filterContext"><c>AuthorizationContext</c> that encapsulates the information for using <c>System.Web.Mvc.AuthorizeAttribute</c>. This contains the controller, HTTP context, request context, action result, and route data.</param>
        protected virtual void HandleUnauthorizedRequest(AuthorizationContext filterContext)
        {
            // Returns HTTP 401 - see comment in HttpUnauthorizedResult.cs.
            filterContext.Result = new HttpUnauthorizedResult();
        }

        /// <summary>
        /// Called when the caching module requests authentication.
        /// </summary>
        /// <param name="httpContext">The HTTP context, which encapsulates all HTTP-specific information about an individual HTTP request.</param>
        /// <returns>Returns a reference to the validation status.</returns>
        /// <remarks>This method must be thread-safe since it is called by the caching module.</remarks>
        protected virtual HttpValidationStatus OnCacheAuthorization(HttpContextBase httpContext)
        {
            if (httpContext == null)
            {
                throw new ArgumentNullException("httpContext");
            }

            bool isAuthorized = this.AuthenticateCore(httpContext);
            return (isAuthorized) ? HttpValidationStatus.Valid : HttpValidationStatus.IgnoreThisRequest;
        }

        internal static string[] SplitString(string original)
        {
            if (String.IsNullOrEmpty(original))
            {
                return new string[0];
            }

            var split = from piece in original.Split(_splitParameter)
                        let trimmed = piece.Trim()
                        where !String.IsNullOrEmpty(trimmed)
                        select trimmed;
            return split.ToArray();
        }
    }
}