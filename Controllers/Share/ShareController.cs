using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ping.Services.Pings;
using Ping.Services.Reviews;

namespace Ping.Controllers.Share;

/// <summary>
/// Public, unversioned web routes that back shareable links (e.g. links shared
/// from the mobile app to Instagram, iMessage, etc.).
///
/// Each content route returns a small HTML page carrying Open Graph tags (so
/// link unfurlers like iMessage/WhatsApp render a rich preview) plus a redirect
/// that opens the native app when installed and falls back to the app store.
///
/// The /.well-known routes serve the Apple App Site Association and Android
/// Asset Links files required for true universal links / app links. These must
/// be served from the SAME host the links point at (see WEB_BASE_URL on the
/// client) over HTTPS with no redirect.
/// </summary>
[AllowAnonymous]
public class ShareController(IReviewService reviewService, IPingService pingService) : ControllerBase
{
    // --- App identifiers ---------------------------------------------------
    // iOS: "<TeamID>.<bundleId>". Team id comes from the Apple Developer account
    // (visible in EAS credentials output); bundle id is net.ping-app.
    private const string AppleAppId = "6RP9UA7Q98.net.ping-app";
    private const string AppStoreId = "6760599199"; // ascAppId from eas.json

    // Android: package + the signing cert SHA-256 fingerprint. Fill the
    // fingerprint from `eas credentials` (Android > Keystore > SHA-256) before
    // Android App Links will verify. Until then iOS works and Android falls
    // back to the store via the redirect page.
    private const string AndroidPackage = "net.ping-app";
    private const string AndroidSha256Fingerprint = "REPLACE_WITH_SHA256_FINGERPRINT";

    private const string AppScheme = "pingapp";

    // GET /review/{id}
    [HttpGet("/review/{id:int}")]
    public async Task<IActionResult> Review(int id)
    {
        var review = await reviewService.GetReviewByIdAsync(id, null);
        if (review is null || review.IsPingDeleted)
            return NotFoundPage();

        var title = string.IsNullOrWhiteSpace(review.PingName) ? "A review on Ping" : review.PingName;
        var desc = !string.IsNullOrWhiteSpace(review.Content)
            ? review.Content!
            : $"{review.Rating}★ · reviewed by {review.UserName} on Ping";

        return SharePage(
            title: title,
            description: desc,
            imageUrl: review.ImageUrl,
            path: $"/review/{id}");
    }

    // GET /ping/{id}
    [HttpGet("/ping/{id:int}")]
    public async Task<IActionResult> PingPlace(int id)
    {
        var ping = await pingService.GetPingByIdAsync(id, null);
        if (ping is null || ping.IsPingDeleted)
            return NotFoundPage();

        var desc = !string.IsNullOrWhiteSpace(ping.PingGenreName)
            ? $"{ping.PingGenreName} · {ping.Address}"
            : ping.Address;

        return SharePage(
            title: ping.Name,
            description: desc,
            imageUrl: ping.ThumbnailUrl,
            path: $"/ping/{id}");
    }

    // GET /.well-known/apple-app-site-association
    [HttpGet("/.well-known/apple-app-site-association")]
    [Produces("application/json")]
    public IActionResult AppleAppSiteAssociation()
    {
        var payload = new
        {
            applinks = new
            {
                apps = Array.Empty<string>(),
                details = new[]
                {
                    new { appID = AppleAppId, paths = new[] { "/review/*", "/ping/*" } }
                }
            }
        };
        // Must be served as application/json with no file extension.
        return new JsonResult(payload) { ContentType = "application/json" };
    }

    // GET /.well-known/assetlinks.json
    [HttpGet("/.well-known/assetlinks.json")]
    [Produces("application/json")]
    public IActionResult AssetLinks()
    {
        var payload = new[]
        {
            new
            {
                relation = new[] { "delegate_permission/common.handle_all_urls" },
                target = new
                {
                    @namespace = "android_app",
                    package_name = AndroidPackage,
                    sha256_cert_fingerprints = new[] { AndroidSha256Fingerprint }
                }
            }
        };
        return new JsonResult(payload) { ContentType = "application/json" };
    }

    // --- HTML rendering ----------------------------------------------------

    private ContentResult SharePage(string title, string? description, string? imageUrl, string path)
    {
        var pageUrl = $"{Request.Scheme}://{Request.Host}{path}";
        var deepLink = $"{AppScheme}:/{path}"; // e.g. pingapp://review/123
        var appStoreUrl = $"https://apps.apple.com/app/id{AppStoreId}";
        var playStoreUrl = $"https://play.google.com/store/apps/details?id={AndroidPackage}";

        string E(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);
        string J(string? s) => System.Text.Json.JsonSerializer.Serialize(s ?? string.Empty);

        var safeImage = string.IsNullOrWhiteSpace(imageUrl) ? "" :
            $"<meta property=\"og:image\" content=\"{E(imageUrl)}\" />\n  <meta name=\"twitter:image\" content=\"{E(imageUrl)}\" />";

        var html = $@"<!DOCTYPE html>
<html lang=""en"">
<head>
  <meta charset=""utf-8"" />
  <meta name=""viewport"" content=""width=device-width, initial-scale=1"" />
  <title>{E(title)} · Ping</title>
  <meta property=""og:title"" content=""{E(title)}"" />
  <meta property=""og:description"" content=""{E(description)}"" />
  <meta property=""og:url"" content=""{E(pageUrl)}"" />
  <meta property=""og:type"" content=""website"" />
  <meta property=""og:site_name"" content=""Ping"" />
  {safeImage}
  <meta name=""twitter:card"" content=""summary_large_image"" />
  <meta name=""twitter:title"" content=""{E(title)}"" />
  <meta name=""twitter:description"" content=""{E(description)}"" />
  <style>
    body {{ font-family: -apple-system, system-ui, sans-serif; background:#0c0c0e; color:#fff;
           display:flex; min-height:100vh; margin:0; align-items:center; justify-content:center; text-align:center; }}
    .card {{ padding:32px; max-width:420px; }}
    h1 {{ font-size:22px; margin:16px 0 8px; }}
    p {{ color:rgba(255,255,255,0.6); font-size:15px; }}
    a.btn {{ display:inline-block; margin-top:20px; padding:12px 24px; border-radius:999px;
             background:#1965EF; color:#fff; text-decoration:none; font-weight:600; }}
  </style>
</head>
<body>
  <div class=""card"">
    <h1>{E(title)}</h1>
    <p>{E(description)}</p>
    <a class=""btn"" id=""open"" href=""{E(deepLink)}"">Open in Ping</a>
  </div>
  <script>
    (function() {{
      var deepLink = {J(deepLink)};
      var store = /android/i.test(navigator.userAgent) ? {J(playStoreUrl)} : {J(appStoreUrl)};
      // Try to open the app; if nothing handles the scheme, fall back to the store.
      var now = Date.now();
      var timer = setTimeout(function() {{
        if (Date.now() - now < 1600) window.location = store;
      }}, 1200);
      window.location = deepLink;
      window.addEventListener('pagehide', function() {{ clearTimeout(timer); }});
    }})();
  </script>
</body>
</html>";

        return Content(html, "text/html");
    }

    private ContentResult NotFoundPage()
    {
        var html = @"<!DOCTYPE html><html lang=""en""><head><meta charset=""utf-8"" />
<title>Not found · Ping</title>
<style>body{font-family:-apple-system,system-ui,sans-serif;background:#0c0c0e;color:#fff;display:flex;min-height:100vh;margin:0;align-items:center;justify-content:center;text-align:center}</style>
</head><body><div><h1>This content isn't available</h1><p>It may have been removed.</p></div></body></html>";
        Response.StatusCode = 404;
        return Content(html, "text/html");
    }
}
