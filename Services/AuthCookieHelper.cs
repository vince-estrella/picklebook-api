using Microsoft.AspNetCore.Http;

namespace PickleballApi.Services
{
    public static class AuthCookieHelper
    {
        public static void SetAuthCookie(HttpResponse response, string name, string token)
        {
            response.Cookies.Append(name, token, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.None,
                Expires = DateTimeOffset.UtcNow.AddDays(7),
                Path = "/"
            });
        }

        public static void ClearAuthCookie(HttpResponse response, string name)
        {
            response.Cookies.Delete(name, new CookieOptions
            {
                Secure = true,
                SameSite = SameSiteMode.None,
                Path = "/"
            });
        }
    }
}
