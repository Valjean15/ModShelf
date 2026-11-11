using System.Text;
using System.Text.Json;

using ModShelf.Sources.DegMods.Api;

namespace ModShelf.Sources.DegMods.Tests.Api;

public sealed class NostrFilterTests
{
    [Fact]
    public void WritesEveryPartAsNip01Does()
    {
        NostrFilter filter = new()
        {
            Kinds = [31142, 30402],
            Authors = ["ab"],
            Tags = new Dictionary<char, IReadOnlyList<string>> { ['d'] = ["x", "y"], ['t'] = ["GameMod"] },
            Since = 10,
            Until = 20,
            Limit = 500,
        };

        Assert.Equal("""{"kinds":[31142,30402],"authors":["ab"],"#d":["x","y"],"#t":["GameMod"],"since":10,"until":20,"limit":500}""", Written(filter));
    }

    [Fact]
    public void LeavesOutWhatItDoesntAsk()
    {
        Assert.Equal("{}", Written(new NostrFilter()));
        Assert.Equal("""{"kinds":[1]}""", Written(new NostrFilter { Kinds = [1] }));
    }

    private static string Written(NostrFilter filter)
    {
        using MemoryStream buffer = new();
        using (Utf8JsonWriter writer = new(buffer))
        {
            filter.Write(writer);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
