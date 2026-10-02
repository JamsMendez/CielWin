using CielWin.App.Alerts;

namespace CielWin.App.Tests.Alerts;

/// <summary>The exact JSON the scene page's host-message handler parses.</summary>
public sealed class AlertLayerMessagesTests
{
    [Fact]
    public void Show_CarriesTilesGridGapWorkAreaAndDuration()
    {
        var request = new AlertShowRequest(["failed", "warning"], 2, 1, 8, 5000, 10, 20, 1700, 1000);

        Assert.Equal(
            "{\"type\":\"show\",\"tiles\":[\"failed\",\"warning\"],\"columns\":2,\"rows\":1,\"gap\":8,"
            + "\"workArea\":{\"left\":10,\"top\":20,\"width\":1700,\"height\":1000},\"duration\":5000}",
            AlertLayerMessages.Show(request));
    }

    [Fact]
    public void Show_ClampsANegativeWorkAreaToZero()
    {
        var request = new AlertShowRequest(["warning"], 1, 1, 8, 1000, -5, -6, -7, -8);

        Assert.Contains("\"workArea\":{\"left\":0,\"top\":0,\"width\":0,\"height\":0}", AlertLayerMessages.Show(request));
    }

    [Fact]
    public void HideAndPauseAndResume_HaveTheShapeTheScenePageHandles()
    {
        Assert.Equal("{\"type\":\"hide\"}", AlertLayerMessages.Hide);
        Assert.Equal("{\"type\":\"pause\"}", AlertLayerMessages.Pause);
        Assert.Equal("{\"type\":\"resume\"}", AlertLayerMessages.Resume);
    }
}
