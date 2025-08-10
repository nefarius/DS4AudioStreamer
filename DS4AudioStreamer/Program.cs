using DS4AudioStreamer;
using DS4AudioStreamer.Sound;

using DS4Windows;

List<HidDevice> hidDevices = DeviceEnumerator.FindDevices();

using HidDevice? usedDevice = hidDevices.FirstOrDefault();

if (null == usedDevice)
{
    Console.WriteLine("No device found");
    return;
}

usedDevice.OpenDevice(true);

if (!usedDevice.IsOpen)
{
    Console.WriteLine("Could not open HID device exclusively, opening in shared mode");
    usedDevice.OpenDevice(false);

    if (!usedDevice.IsOpen)
    {
        throw new InvalidOperationException("Could not open HID device");
    }
}

using HidAudioRouterWorker captureWorker = new(usedDevice);
captureWorker.Start();

Console.WriteLine("Press F3 to exit");

while (usedDevice.IsConnected)
{
    if (Console.KeyAvailable)
    {
        ConsoleKeyInfo key = Console.ReadKey(true);
        if (key.Key == ConsoleKey.F3)
        {
            Console.WriteLine("F3 pressed, exiting...");
            break;
        }
    }

    Thread.Sleep(200);
}