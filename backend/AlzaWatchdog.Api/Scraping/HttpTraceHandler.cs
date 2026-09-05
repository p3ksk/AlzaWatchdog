using System.Net;
using System.Text;

namespace AlzaWatchdog.Api.Scraping;

/// <summary>
/// Logs the whole conversation with alza.sk, behind <c>Watchdog:LogRequests</c>.
///
/// It sits above SocketsHttpHandler, so it sees what we asked for rather than the
/// literal bytes: cookies are read from the container and the transport's own
/// Accept-Encoding is reported from configuration.
/// </summary>
public sealed class HttpTraceHandler(
    CookieContainer cookies,
    DecompressionMethods decompression,
    ILogger<HttpTraceHandler> logger) : DelegatingHandler
{
    /// <summary>Enough of the body to recognise a Cloudflare interstitial.</summary>
    private const int BodyPreview = 1200;

    /// <summary>
    /// ToString() rather than joining the values: a User-Agent enumerates as six
    /// product tokens, and joining those prints a header that was never sent.
    /// </summary>
    private static string Indent(string headers) =>
        string.IsNullOrEmpty(headers)
            ? string.Empty
            : "  " + headers.ReplaceLineEndings("\n").TrimEnd('\n').Replace("\n", "\n  ") + "\n";

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        logger.LogInformation("→ {Request}", Describe(request));

        var response = await base.SendAsync(request, ct);

        // Buffered first, so reading it here does not consume the stream the
        // scraper is about to parse.
        await response.Content.LoadIntoBufferAsync(ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        logger.LogInformation("← {Response}", Describe(response, body));
        return response;
    }

    private string Describe(HttpRequestMessage request)
    {
        var text = new StringBuilder()
            .AppendLine()
            .Append(request.Method).Append(' ').Append(request.RequestUri)
            .Append(" HTTP/").AppendLine(request.Version.ToString());

        text.Append(Indent(request.Headers.ToString()));

        // Added by the transport below this handler, so reported rather than read.
        text.Append("  [transport] Accept-Encoding: ")
            .AppendLine(decompression == DecompressionMethods.None
                ? "(none — automatic decompression is off)"
                : decompression.ToString());

        var jar = request.RequestUri is null
            ? []
            : cookies.GetCookies(request.RequestUri).Select(c => $"{c.Name}={c.Value}").ToArray();

        text.Append("  [transport] Cookie: ")
            .AppendLine(jar.Length == 0 ? "(none)" : string.Join("; ", jar));

        return text.ToString().TrimEnd();
    }

    private static string Describe(HttpResponseMessage response, string body)
    {
        var text = new StringBuilder()
            .AppendLine()
            .Append("HTTP/").Append(response.Version).Append(' ')
            .Append((int)response.StatusCode).Append(' ').AppendLine(response.ReasonPhrase);

        text.Append(Indent(response.Headers.ToString()))
            .Append(Indent(response.Content.Headers.ToString()));

        text.Append("  body (").Append(body.Length).AppendLine(" chars):");

        var preview = body.Length > BodyPreview ? body[..BodyPreview] + " …" : body;
        text.Append("  ").Append(preview.ReplaceLineEndings("\n  "));

        return text.ToString().TrimEnd();
    }
}
