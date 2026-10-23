using Microsoft.Extensions.Http;

using ModShelf.Application.Accounts;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Http;

namespace ModShelf.Infrastructure.Sources;

/// <summary>
/// Sets up every client named by <see cref="SourceHttpClients.NameFor"/>: its requests get the source's linked account
/// through a <see cref="LinkedAccountHandler"/>. Other clients are left as they are.
/// </summary>
/// <remarks>
/// A filter rather than one registration per client, because sources are registered without Infrastructure knowing
/// them: any client a source asks for under its name is covered.
/// </remarks>
internal sealed class SourceHttpClientFilter(ICredentialProvider credentials) : IHttpMessageHandlerBuilderFilter
{
    public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next)
    {
        ArgumentNullException.ThrowIfNull(next);

        return builder =>
        {
            next(builder);
            if (SourceHttpClients.TryGetSource(builder.Name, out SourceId? source))
            {
                // First in line, so handlers further in (such as retries) send the credentials already added, and a
                // 401 is handled once for the whole request.
                builder.AdditionalHandlers.Insert(0, new LinkedAccountHandler(source, credentials));
            }
        };
    }
}
