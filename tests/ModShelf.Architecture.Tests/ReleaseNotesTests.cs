using System.Diagnostics;

using ModShelf.Testing;

namespace ModShelf.Architecture.Tests;

/// <summary>
/// <c>build/release-notes.sh</c>, which the release workflow writes each release's notes with (ADR 0039), over a
/// repository of its own with the commits and tags each test makes. Bash on Windows may be WSL's, which can't see the
/// test's folder, so these run on Linux and macOS, where CI runs them with macOS's own bash 3.2 too.
/// </summary>
public sealed class ReleaseNotesTests : IDisposable
{
    private static readonly string Script = Path.Combine(Repository.Root, "build", "release-notes.sh");

    private readonly TempFolder _repository = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose() => _repository.Dispose();

    [Fact]
    public async Task TheFirstReleaseSaysSo()
    {
        await StartAsync();
        await CommitAsync("feat: download mods");

        Assert.Equal("The first release of ModShelf.\n", await NotesAsync());
    }

    [Fact]
    public async Task ListsWhatsNewAndFixedSinceTheReleaseBefore()
    {
        await StartAsync();
        await CommitAsync("feat: download mods");
        await TagAsync("v0.9.0");
        await CommitAsync("feat(settings): look for a newer ModShelf");
        await CommitAsync("docs: record the installers (ADR 0038)");
        await CommitAsync("fix(platform): keep mimeapps.list's line endings");
        await CommitAsync("test: cover the AppImage's path");
        await CommitAsync("feat!: drop the old feed");
        await CommitAsync("featured: not a feature");

        Assert.Equal(
            "## New\n\n- Look for a newer ModShelf\n- Drop the old feed\n\n## Fixed\n\n- Keep mimeapps.list's line endings\n",
            await NotesAsync());
    }

    [Fact]
    public async Task OnlyFixesHaveNoNewPart()
    {
        await StartAsync();
        await TagAsync("v0.9.0");
        await CommitAsync("fix: keep mimeapps.list's line endings");

        Assert.Equal("## Fixed\n\n- Keep mimeapps.list's line endings\n", await NotesAsync());
    }

    [Fact]
    public async Task AReleaseWithNothingToSeeSaysSo()
    {
        await StartAsync();
        await TagAsync("v0.9.0");
        await CommitAsync("docs: record the release workflow");
        await CommitAsync("build: pack with vpk");

        Assert.Equal("Changes behind the scenes only: nothing you'd see in ModShelf is new or fixed since 0.9.0.\n", await NotesAsync());
    }

    [Fact]
    public async Task ATaggedReleaseStartsAfterTheVersionTagBeforeItsOwn()
    {
        await StartAsync();
        await TagAsync("v0.9.0");
        await CommitAsync("feat: in 0.9.1");
        await TagAsync("checklist-run");
        await CommitAsync("fix: also in 0.9.1");
        await TagAsync("v0.9.1");
        await CommitAsync("feat: after 0.9.1");

        Assert.Equal("## New\n\n- In 0.9.1\n\n## Fixed\n\n- Also in 0.9.1\n", await NotesAsync("v0.9.1"));
        Assert.Equal("## New\n\n- After 0.9.1\n", await NotesAsync());
    }

    private async Task StartAsync()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Bash on Windows may not see the test's folder; CI runs these on Linux and macOS.");
        await RunAsync("git", "init", "--quiet");
        await CommitAsync("chore: start");
    }

    private Task<string> CommitAsync(string message)
        => RunAsync("git", "-c", "user.name=Test", "-c", "user.email=test@example.com", "-c", "commit.gpgSign=false", "commit", "--quiet", "--allow-empty", "-m", message);

    private Task<string> TagAsync(string tag) => RunAsync("git", "-c", "tag.gpgSign=false", "tag", tag);

    private Task<string> NotesAsync(params string[] release) => RunAsync("bash", [Script, .. release]);

    private async Task<string> RunAsync(string program, params string[] arguments)
    {
        ProcessStartInfo start = new(program) { WorkingDirectory = _repository.Path, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync(Cancellation);
        string error = await process.StandardError.ReadToEndAsync(Cancellation);
        await process.WaitForExitAsync(Cancellation);
        Assert.True(process.ExitCode == 0, $"{program} {string.Join(' ', arguments)} exited with {process.ExitCode}: {error}");
        return await output;
    }
}
