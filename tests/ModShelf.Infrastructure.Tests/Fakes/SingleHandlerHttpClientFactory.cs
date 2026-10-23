namespace ModShelf.Infrastructure.Tests.Fakes;

/// <summary>
/// An <see cref="IHttpClientFactory"/> whose clients all talk to one handler, without any pipeline in between.
/// </summary>
internal sealed class SingleHandlerHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}
