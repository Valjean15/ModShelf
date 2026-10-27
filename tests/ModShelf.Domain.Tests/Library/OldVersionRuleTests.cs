using ModShelf.Domain.Library;

namespace ModShelf.Domain.Tests.Library;

public sealed class OldVersionRuleTests
{
    [Fact]
    public void ByDefaultKeepsTheNewestAndTheOneBefore()
    {
        Assert.Equal((OldVersionAction.KeepLatest, 2), (OldVersionRule.Default.Action, OldVersionRule.Default.Count));
    }

    [Theory]
    [InlineData("all", OldVersionAction.KeepAll, 0)]
    [InlineData(" ALL ", OldVersionAction.KeepAll, 0)]
    [InlineData("archive", OldVersionAction.Archive, 0)]
    [InlineData("Archive", OldVersionAction.Archive, 0)]
    [InlineData("1", OldVersionAction.KeepLatest, 1)]
    [InlineData("2", OldVersionAction.KeepLatest, 2)]
    [InlineData("99", OldVersionAction.KeepLatest, 99)]
    public void ReadsARule(string text, OldVersionAction action, int count)
    {
        OldVersionRule rule = OldVersionRule.Parse(text);

        Assert.Equal((action, count), (rule.Action, rule.Count));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("100")]
    [InlineData("-1")]
    [InlineData("+2")]
    [InlineData("2.5")]
    [InlineData("keep")]
    [InlineData("")]
    public void RefusesWhatIsntARule(string text)
    {
        ArgumentException refused = Assert.Throws<ArgumentException>(() => OldVersionRule.Parse(text));

        Assert.StartsWith(
            $"'{text}' isn't a rule for old versions. Use all, archive, or how many versions to keep, from 1 to 99, such as 2.",
            refused.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("all")]
    [InlineData("archive")]
    [InlineData("3")]
    public void WritesWhatItReads(string text)
    {
        Assert.Equal(text, OldVersionRule.Parse(text).ToString());
    }

    [Fact]
    public void RulesAreEqualByWhatTheyDo()
    {
        Assert.Equal(OldVersionRule.KeepLatest(2), OldVersionRule.Default);
        Assert.NotEqual(OldVersionRule.KeepLatest(3), OldVersionRule.Default);
        Assert.Same(OldVersionRule.Archive, OldVersionRule.Parse("archive"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void KeepsFromOneTo99Versions(int count)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => OldVersionRule.KeepLatest(count));
    }
}
