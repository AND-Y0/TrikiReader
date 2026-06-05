namespace TrikiReader.Tests;

public sealed class TrikiDeviceInfoTests
{
    [Fact]
    public void DecodeBatteryLevel_ReturnsSingleBytePercent()
    {
        var percent = TrikiDeviceInfo.DecodeBatteryLevel(new byte[] { 0x64 });

        Assert.Equal(100, percent);
    }

    [Fact]
    public void DecodeText_TrimsNullTerminatorsAndWhitespace()
    {
        var text = TrikiDeviceInfo.DecodeText(new byte[] { 0x33, 0x2E, 0x30, 0x2E, 0x31, 0x00, 0x20 });

        Assert.Equal("3.0.1", text);
    }

    [Fact]
    public void DecodeSystemId_FormatsEightBytesAsHexPairs()
    {
        var systemId = TrikiDeviceInfo.DecodeSystemId(new byte[]
        {
            0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF
        });

        Assert.Equal("01 23 45 67 89 AB CD EF", systemId);
    }

    [Fact]
    public void DecodePnpId_DecodesStandardSevenByteValue()
    {
        var pnpId = TrikiDeviceInfo.DecodePnpId(new byte[]
        {
            0x02, 0x34, 0x12, 0x78, 0x56, 0xBC, 0x9A
        });

        Assert.Equal("USB vendor=0x1234 product=0x5678 version=0x9ABC", pnpId);
    }

    [Fact]
    public void ToDisplayText_UsesFirmwareAsSystemVersionAndOmitsMissingOptionalFields()
    {
        var info = new TrikiDeviceInfo(
            DeviceName: "Triki 224987000",
            BatteryLevelPercent: 100,
            FirmwareRevision: "3.0.1",
            HardwareRevision: null,
            SoftwareRevision: null,
            ManufacturerName: null,
            ModelNumber: null,
            SerialNumber: null,
            SystemId: null,
            PnpId: null);

        var display = info.ToDisplayText();

        Assert.Contains("Device: Triki 224987000", display);
        Assert.Contains("Battery: 100%", display);
        Assert.Contains("System/Firmware: 3.0.1", display);
        Assert.DoesNotContain("Software:", display);
        Assert.DoesNotContain("Hardware:", display);
    }
}
