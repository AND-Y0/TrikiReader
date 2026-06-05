namespace TrikiReader.Tests;

public sealed class ImuStatsTests
{
    [Fact]
    public void NotificationReceived_TracksLatestAndMaximumNotificationGap()
    {
        var stats = new ImuStats();
        var first = DateTimeOffset.Parse("2026-05-27T10:15:30+00:00");

        stats.NotificationReceived(first);
        stats.NotificationReceived(first.AddMilliseconds(12.5));
        stats.NotificationReceived(first.AddMilliseconds(50.0));

        Assert.Equal(3, stats.NotificationCount);
        Assert.Equal(37.5, stats.LastNotificationGapMilliseconds);
        Assert.Equal(37.5, stats.MaxNotificationGapMilliseconds);
    }
}
