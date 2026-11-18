using System.Collections.Concurrent;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Common;
using ModShelf.Sources.Abstractions.Endorsements;
using ModShelf.Sources.Abstractions.Links;

namespace ModShelf.Application.Tests.Fakes;

/// <summary>
/// A site that lets its account endorse, keeping what the account said in memory. It recognizes every link as a file of
/// <see cref="LinkedMod"/>. It records each endorsement given, with its version.
/// </summary>
internal sealed class FakeEndorsingSite(SourceId id) : ILinkRecognizer, IModEndorsements
{
    private readonly ConcurrentDictionary<ModRef, Endorsement> _said = new();
    private readonly ConcurrentQueue<(ModRef Mod, Endorsement Endorsement, string? Version)> _given = new();

    public SourceId Id { get; } = id;

    public string DisplayName => Id.Value;

    public SiteWords Words { get; } = new() { Approve = "endorse", Unapprove = "abstain" };

    public ModRef LinkedMod => TestData.File(Id, "35407").Mod;

    public IReadOnlyList<(ModRef Mod, Endorsement Endorsement, string? Version)> Given => [.. _given];

    public Task<RecognizedLink?> RecognizeAsync(Uri link, CancellationToken cancellationToken)
        => Task.FromResult<RecognizedLink?>(new RecognizedLink(TestData.File(Id, "35407"), "SkyUI_5_2_SE.7z"));

    public Task<IReadOnlyDictionary<ModRef, Endorsement>> GetEndorsementsAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyDictionary<ModRef, Endorsement>>(new Dictionary<ModRef, Endorsement>(_said));

    public Task EndorseAsync(ModRef modRef, Endorsement endorsement, string? version, CancellationToken cancellationToken)
    {
        if (modRef.Game.Source != Id)
        {
            throw new ArgumentException("That's another site's mod.", nameof(modRef));
        }

        _said[modRef] = endorsement;
        _given.Enqueue((modRef, endorsement, version));
        return Task.CompletedTask;
    }
}
