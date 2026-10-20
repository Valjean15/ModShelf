using Microsoft.Extensions.Hosting;

// Headless composition root. Phase 1 adds the `download` and `queue list` commands;
// for now it only builds the host, so DI and logging are wired the same way as the desktop app.
// Configuration is read next to the executable, not from whatever directory the shell is in.
HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

using IHost host = builder.Build();

return 0;
