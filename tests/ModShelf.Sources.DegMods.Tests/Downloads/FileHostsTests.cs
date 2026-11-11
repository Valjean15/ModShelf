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
    [InlineData("http://localhost:3000/mod.zip")]
    [InlineData("http://LOCALHOST/mod.zip")]
    [InlineData("http://files.localhost/mod.zip")]
    [InlineData("http://printer.local/mod.zip")]
    [InlineData("http://nas/mod.zip")]
    [InlineData("http://nas./mod.zip")]
    [InlineData("http://0.0.0.0/mod.zip")]
    [InlineData("http://10.1.2.3/mod.zip")]
    [InlineData("http://127.0.0.1/mod.zip")]
    [InlineData("http://169.254.169.254/latest")]
    [InlineData("http://172.16.0.1/mod.zip")]
    [InlineData("http://172.31.255.255/mod.zip")]
    [InlineData("http://192.168.1.10/mod.zip")]
    [InlineData("http://100.64.0.1/mod.zip")]
    [InlineData("http://100.127.255.255/mod.zip")]
    [InlineData("http://[::1]/mod.zip")]
    [InlineData("http://[::]/mod.zip")]
    [InlineData("http://[fe80::1]/mod.zip")]
    [InlineData("http://[fec0::1]/mod.zip")]
    [InlineData("http://[fd12:3456::1]/mod.zip")]
    [InlineData("http://[::ffff:192.168.0.1]/mod.zip")]
    public void ALinkToThisComputerOrItsNetworkIsOnIt(string link)
    {
        Assert.True(FileHosts.IsOnThisNetwork(new Uri(link)));
    }

    [Theory]
    [InlineData("https://f.rpghq.org/14Y3yg8yWsCn.7z")]
    [InlineData("https://localhost.example.com/mod.zip")]
    [InlineData("http://1.1.1.1/mod.zip")]
    [InlineData("http://11.0.0.1/mod.zip")]
    [InlineData("http://126.255.255.255/mod.zip")]
    [InlineData("http://128.0.0.1/mod.zip")]
    [InlineData("http://169.253.0.1/mod.zip")]
    [InlineData("http://169.255.0.1/mod.zip")]
    [InlineData("http://172.15.255.255/mod.zip")]
    [InlineData("http://172.32.0.1/mod.zip")]
    [InlineData("http://192.167.1.1/mod.zip")]
    [InlineData("http://192.169.1.1/mod.zip")]
    [InlineData("http://100.63.255.255/mod.zip")]
    [InlineData("http://100.128.0.1/mod.zip")]
    [InlineData("http://99.64.0.1/mod.zip")]
    [InlineData("http://[2606:4700:4700::1111]/mod.zip")]
    [InlineData("http://[::ffff:8.8.8.8]/mod.zip")]
    public void ALinkElsewhereIsntOnIt(string link)
    {
        Assert.False(FileHosts.IsOnThisNetwork(new Uri(link)));
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
