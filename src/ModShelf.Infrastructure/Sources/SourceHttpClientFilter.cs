using Microsoft.Extensions.DependencyInjection;
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
/// them: any client a source asks for under its name is covered. The client factory creates every filter when it
/// starts, for any client, so the credential provider (and with it the secret store) is fetched only when a source's
/// client is set up.
/// </remarks>
internal sealed class SourceHttpClientFilter : IHttpMessageHandlerBuilderFilter
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
                ICredentialProvider credentials = builder.Services.GetRequiredService<ICredentialProvider>();
                builder.AdditionalHandlers.Insert(0, new LinkedAccountHandler(source, credentials));
            }
        };
    }
}
