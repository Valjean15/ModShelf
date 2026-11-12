using ModShelf.Platform.Installed;

namespace ModShelf.Platform.Tests.Installed;

public sealed class KeyValueTests
{
    [Fact]
    public void ReadsAManifestsKeysInAnyCase()
    {
        KeyValue file = KeyValue.Parse("""
            "AppState"
            {
            	"appid"		"413150"
            	"name"		"Stardew Valley"
            	"StateFlags"		"4"
            	"installdir"		"Stardew Valley"
            	"InstalledDepots"
            	{
            		"413151"
            		{
            			"manifest"		"8273947239847"
            		}
            	}
            }
            """);

        KeyValue state = Assert.IsType<KeyValue>(file["appstate"]);
        Assert.Equal(("413150", "Stardew Valley", "4", "Stardew Valley"), (state.Text("AppID"), state.Text("name"), state.Text("stateflags"), state.Text("InstallDir")));
        Assert.Null(state.Value);
        Assert.Equal("8273947239847", state["InstalledDepots"]?["413151"]?.Text("manifest"));
        Assert.Null(state.Text("InstalledDepots"));
        Assert.Null(state["missing"]);
    }

    [Fact]
    public void KeepsTheKeysInOrderTheFirstOfANameFound()
    {
        KeyValue file = KeyValue.Parse("""
            "libraryfolders" { "0" { "path" "/a" } "1" { "path" "/b" } "0" { "path" "/c" } }
            """);

        KeyValue folders = file["libraryfolders"]!;
        Assert.Equal(["0", "1", "0"], folders.Children.Select(child => child.Name));
        Assert.Equal("/a", folders["0"]?.Text("path"));
    }

    [Fact]
    public void ReadsEscapesInQuotes()
    {
        KeyValue file = KeyValue.Parse("""
            "path"  "C:\\Program Files (x86)\\Steam"
            "name"  "The \"Quoted\" Game\tTwo\nLines"
            "odd"   "a\qb"
            """);

        Assert.Equal(@"C:\Program Files (x86)\Steam", file.Text("path"));
        Assert.Equal("The \"Quoted\" Game\tTwo\nLines", file.Text("name"));
        Assert.Equal(@"a\qb", file.Text("odd"));
    }

    [Fact]
    public void ReadsUnquotedWordsCommentsAndConditions()
    {
        KeyValue file = KeyValue.Parse("""
            // A comment before the keys.
            AppState
            {
                appid 489830 // its app ID
                name "Skyrim" [$WIN32]
                "{brace}" "}"
                last x
            }
            """);

        KeyValue state = file["AppState"]!;
        Assert.Equal(["appid", "name", "{brace}", "last"], state.Children.Select(child => child.Name));
        Assert.Equal(("489830", "Skyrim", "}", "x"), (state.Text("appid"), state.Text("name"), state.Text("{brace}"), state.Text("last")));
    }

    [Fact]
    public void AnEmptyTextHasNoKeys() => Assert.Empty(KeyValue.Parse(" \n// nothing\n").Children);

    [Theory]
    [InlineData("\"AppState\" { \"appid\" \"1\"", "isn't closed")]
    [InlineData("\"name\" \"Skyrim", "isn't closed")]
    [InlineData("\"appid\" \"1\" }", "closes no group")]
    [InlineData("{ \"appid\" \"1\" }", "has no key")]
    [InlineData("\"AppState\" { \"appid\" }", "has no value")]
    [InlineData("\"appid\"", "has no value")]
    [InlineData("\"name\" \"Skyrim\" [$WIN32", "isn't closed")]
    public void RefusesWhatIsntSteamsFormat(string text, string says)
    {
        InvalidDataException refused = Assert.Throws<InvalidDataException>(() => KeyValue.Parse(text));

        Assert.Contains(says, refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesGroupsDeeperThanSteamsGo()
    {
        string deepest = string.Concat(Enumerable.Repeat("\"k\" { ", KeyValue.MostDepth)) + string.Concat(Enumerable.Repeat("} ", KeyValue.MostDepth));
        string deeper = string.Concat(Enumerable.Repeat("\"k\" { ", KeyValue.MostDepth + 1)) + string.Concat(Enumerable.Repeat("} ", KeyValue.MostDepth + 1));

        KeyValue read = KeyValue.Parse(deepest);
        InvalidDataException refused = Assert.Throws<InvalidDataException>(() => KeyValue.Parse(deeper));

        Assert.Single(read.Children);
        Assert.Contains("deeper", refused.Message, StringComparison.Ordinal);
    }
}
