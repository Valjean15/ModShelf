using ModShelf.Domain.Mods;

namespace ModShelf.Application.Tests.Fakes;

/// <summary>
/// What the scheduler asked a <see cref="ScriptedResolver"/>.
/// </summary>
internal sealed record ResolveCall(ModFileRef File, Uri? Handoff);
