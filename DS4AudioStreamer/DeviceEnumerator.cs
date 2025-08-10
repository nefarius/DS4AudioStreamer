using DS4Windows;

namespace DS4AudioStreamer;

internal static class DeviceEnumerator
{
    private const int SonyVid = 0x054C;

    public static List<HidDevice> FindDevices()
    {
        // TODO: filter on supported PIDs
        IEnumerable<HidDevice> hDevices = HidDevices.Enumerate(SonyVid);
        return hDevices.ToList();
    }
}