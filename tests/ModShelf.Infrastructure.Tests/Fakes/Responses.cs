using System.Net;
using System.Net.Http.Headers;

namespace ModShelf.Infrastructure.Tests.Fakes;

/// <summary>
/// Builds the HTTP responses a download server sends.
/// </summary>
internal static class Responses
{
    /// <summary>
    /// 200 with the whole file. With <paramref name="stopAfter"/>, the body ends that way after that many bytes.
    /// </summary>
    /// <param name="etag">The file's entity tag, quoted, such as <c>"v1"</c>; <c>W/"v1"</c> for a weak one.</param>
    /// <param name="lastModified">When the server says the file last changed.</param>
    public static Func<HttpResponseMessage> Whole(
        byte[] file,
        int? stopAfter = null,
        StreamEnding ending = StreamEnding.End,
        int chunkSize = int.MaxValue,
        string? etag = null,
        DateTimeOffset? lastModified = null)
        => () => Validated(Body(HttpStatusCode.OK, file, 0, stopAfter ?? file.Length, ending, chunkSize), etag, lastModified);

    /// <summary>
    /// 206 with the file from byte <paramref name="from"/> on.
    /// </summary>
    public static Func<HttpResponseMessage> Rest(
        byte[] file,
        int from,
        int? stopAfter = null,
        StreamEnding ending = StreamEnding.End,
        string? etag = null)
        => () =>
        {
            HttpResponseMessage response = Body(HttpStatusCode.PartialContent, file, from, stopAfter ?? file.Length, ending, int.MaxValue);
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, file.Length - 1, file.Length);
            return Validated(response, etag, lastModified: null);
        };

    /// <summary>
    /// 200 with a web page, as a sign-in page, a captive portal or a file host's "view" page answers.
    /// </summary>
    public static Func<HttpResponseMessage> Page(string html)
        => () =>
        {
            ByteArrayContent content = new(System.Text.Encoding.UTF8.GetBytes(html));
            content.Headers.ContentType = new MediaTypeHeaderValue("text/html") { CharSet = "utf-8" };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        };

    /// <summary>
    /// 416 for a file of <paramref name="length"/> bytes ("bytes */length").
    /// </summary>
    public static Func<HttpResponseMessage> RangeNotSatisfiable(long length)
        => () =>
        {
            HttpResponseMessage response = new(HttpStatusCode.RequestedRangeNotSatisfiable) { Content = new ByteArrayContent([]) };
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(length);
            return response;
        };

    public static Func<HttpResponseMessage> Status(HttpStatusCode status)
        => () => new HttpResponseMessage(status) { Content = new ByteArrayContent([]) };

    /// <summary>
    /// The bytes of a sample file. Every position has a distinct-looking value, so misplaced bytes show.
    /// </summary>
    public static byte[] SampleFile(int length)
        => [.. Enumerable.Range(0, length).Select(i => (byte)((i * 31) % 251))];

    private static HttpResponseMessage Validated(HttpResponseMessage response, string? etag, DateTimeOffset? lastModified)
    {
        if (etag is not null)
        {
            response.Headers.ETag = EntityTagHeaderValue.Parse(etag);
        }

        response.Content.Headers.LastModified = lastModified;
        return response;
    }

    private static HttpResponseMessage Body(HttpStatusCode status, byte[] file, int from, int stopAfter, StreamEnding ending, int chunkSize)
    {
        byte[] slice = file[from..];
        StreamContent content = new(new ScriptedStream(slice, stopAfter - from, ending, chunkSize));
        content.Headers.ContentLength = slice.Length;
        return new HttpResponseMessage(status) { Content = content };
    }
}
