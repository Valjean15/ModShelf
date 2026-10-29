namespace ModShelf.Sources.Nexus.Api;

/// <summary>
/// Reads each answer's body in full before handing the answer on, so the attempt's timeout covers the body as well as
/// the headers: a connection that drops silently part-way is a timed-out attempt, retried and then reported, instead
/// of a read that waits for good.
/// </summary>
/// <remarks>Sits inside the retry pipeline's timeout, next to the network.</remarks>
internal sealed class WholeAnswerHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        try
        {
            await response.Content.LoadIntoBufferAsync(cancellationToken).ConfigureAwait(false);
            return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }
}
