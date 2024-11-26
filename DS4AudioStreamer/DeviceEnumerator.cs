using DS4AudioStreamer.HidLibrary;

using DS4Windows;

namespace DS4AudioStreamer;

internal static class DeviceEnumerator
{
    internal const int SonyVid = 0x054C;

    private static readonly VidPidInfo[] knownDevices =
    {
        new(SonyVid, 0x5C4, "DS4 v.1"), new(SonyVid, 0x09CC, "DS4 v.2")
    };

    public static List<HidDevice> FindDevices()
    {
        IEnumerable<HidDevice> hDevices = HidDevices.EnumerateDS4(knownDevices);
        List<HidDevice> tempList = hDevices.ToList();
        return tempList;
    }
}