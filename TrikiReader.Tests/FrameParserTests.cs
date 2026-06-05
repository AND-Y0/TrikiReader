namespace TrikiReader.Tests;

public sealed class FrameParserTests
{
    [Fact]
    public void Push_ReturnsCompleteFrame()
    {
        var parser = new FrameParser();
        var frame = Frame(1);

        var frames = parser.Push(frame).ToList();

        Assert.Single(frames);
        Assert.Equal(frame, frames[0]);
        Assert.Equal(0, parser.DroppedByteCount);
    }

    [Fact]
    public void Push_ReturnsFrameSplitAcrossNotifications()
    {
        var parser = new FrameParser();
        var frame = Frame(2);

        Assert.Empty(parser.Push(frame[..5]));
        var frames = parser.Push(frame[5..]).ToList();

        Assert.Single(frames);
        Assert.Equal(frame, frames[0]);
    }

    [Fact]
    public void Push_DropsGarbageBeforeHeader()
    {
        var parser = new FrameParser();
        var frame = Frame(3);
        var bytes = new byte[] { 0x99, 0x88, 0x77 }.Concat(frame).ToArray();

        var frames = parser.Push(bytes).ToList();

        Assert.Single(frames);
        Assert.Equal(frame, frames[0]);
        Assert.Equal(3, parser.DroppedByteCount);
    }

    [Fact]
    public void Push_KeepsTrailingHeaderByteForNextNotification()
    {
        var parser = new FrameParser();
        var frameTail = Frame(4)[1..];

        Assert.Empty(parser.Push(new byte[] { 0x10, 0x22 }));
        var frames = parser.Push(frameTail).ToList();

        Assert.Single(frames);
        Assert.Equal(new byte[] { 0x22 }.Concat(frameTail), frames[0]);
        Assert.Equal(1, parser.DroppedByteCount);
    }

    private static byte[] Frame(byte seed)
    {
        return new byte[]
        {
            0x22, 0x00,
            seed, 0x00,
            (byte)(seed + 1), 0x00,
            (byte)(seed + 2), 0x00,
            (byte)(seed + 3), 0x00,
            (byte)(seed + 4), 0x00,
            (byte)(seed + 5), 0x00
        };
    }
}
