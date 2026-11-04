using Polly.Timeout;

namespace ModShelf.Infrastructure.Sources;

/// <summary>
/// Reports a request whose last attempt took too long as the site not answering: an <see cref="HttpRequestException"/>
/// without a status, holding a <see cref="TimeoutException"/>, as the sources' interfaces say. A source never sees the
/// retry pipeline's own exception.
/// </summary>
/// <remarks>Sits just outside the retry pipeline, so only the request's end is reported.</remarks>
internal sealed class LateAnswerHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutRejectedException exception)
        {
            const string Message = "The site didn't answer in time.";
            throw new HttpRequestException(Message, new TimeoutException(Message, exception));
        }
    }
}
