using DS4Windows;

namespace DS4AudioStreamer;

internal static class DeviceEnumerator
{
    private const int SonyVid = 0x054C;

    public static List<HidDevice> FindDevices()
    {
        IEnumerable<HidDevice> hDevices = HidDevices.Enumerate(SonyVid);
        List<HidDevice> tempList = hDevices.ToList();
        return tempList;
    }
}