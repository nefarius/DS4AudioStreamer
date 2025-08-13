using DS4Windows;

namespace DS4AudioStreamer;

internal static class DeviceEnumerator
{
    private const int SonyVid = 0x054C;

    public static List<HidDevice> FindDevices()
    {
        return HidDevices
            .Enumerate(SonyVid)
            .Where(d => d.Attributes.ProductId is 0x05C4 /* DS4 v.1 */ or 0x09CC /* DS4 v.2 */)
            .ToList();
    }
}