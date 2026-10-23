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
    public static Func<HttpResponseMessage> Whole(
        byte[] file,
        int? stopAfter = null,
        StreamEnding ending = StreamEnding.End,
        int chunkSize = int.MaxValue)
        => () => Body(HttpStatusCode.OK, file, 0, stopAfter ?? file.Length, ending, chunkSize);

    /// <summary>
    /// 206 with the file from byte <paramref name="from"/> on.
    /// </summary>
    public static Func<HttpResponseMessage> Rest(byte[] file, int from, int? stopAfter = null, StreamEnding ending = StreamEnding.End)
        => () =>
        {
            HttpResponseMessage response = Body(HttpStatusCode.PartialContent, file, from, stopAfter ?? file.Length, ending, int.MaxValue);
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, file.Length - 1, file.Length);
            return response;
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

    private static HttpResponseMessage Body(HttpStatusCode status, byte[] file, int from, int stopAfter, StreamEnding ending, int chunkSize)
    {
        byte[] slice = file[from..];
        StreamContent content = new(new ScriptedStream(slice, stopAfter - from, ending, chunkSize));
        content.Headers.ContentLength = slice.Length;
        return new HttpResponseMessage(status) { Content = content };
    }
}
