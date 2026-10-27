using ModShelf.Application.Notifications.Ports;
using ModShelf.Platform.Notifications;

namespace ModShelf.Platform.Tests.Notifications;

public sealed class ToastContentTests
{
    [Fact]
    public void PutsTheTitleAndMessageInTheGenericTemplate()
    {
        string xml = ToastContent.For(new Notification("2 new updates", "SkyUI and SMAPI"));

        Assert.Equal(
            """<toast><visual><binding template="ToastGeneric"><text>2 new updates</text><text>SkyUI and SMAPI</text></binding></visual></toast>""",
            xml);
    }

    [Fact]
    public void EscapesWhatXmlWouldRead()
    {
        string xml = ToastContent.For(new Notification("1 new update", "Bits & <Pieces>"));

        Assert.Contains("<text>Bits &amp; &lt;Pieces&gt;</text>", xml, StringComparison.Ordinal);
    }
}
