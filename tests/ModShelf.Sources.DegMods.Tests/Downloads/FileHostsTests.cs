using ModShelf.Sources.DegMods.Downloads;

namespace ModShelf.Sources.DegMods.Tests.Downloads;

public sealed class FileHostsTests
{
    private const string Sha256 = "550a7a5da7893d23b7e9e5562b1c69b6576476f72f6ef3a9afde9cb27837f08a";

    [Theory]
    [InlineData("https://brs.degmods.com/" + Sha256 + ".zip")]
    [InlineData("https://blossom.primal.net/" + Sha256)]
    [InlineData("https://blossom.primal.net/550A7A5DA7893D23B7E9E5562B1C69B6576476F72F6EF3A9AFDE9CB27837F08A.7z")]
    public void AFileServersLinkNamesItsFileBySha256(string link)
    {
        Assert.Equal(Sha256, FileHosts.BlossomHash(new Uri(link)));
    }

    [Theory]
    [InlineData("https://example.com/files/" + Sha256 + ".zip")]
    [InlineData("https://example.com/" + Sha256 + "/file.zip")]
    [InlineData("https://example.com/550a7a5da7893d23.zip")]
    [InlineData("https://example.com/")]
    [InlineData("https://files.catbox.moe/wa4qcn.7z")]
    public void AnotherLinkNamesNone(string link)
    {
        Assert.Null(FileHosts.BlossomHash(new Uri(link)));
    }
}
