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

usedDevice.OpenDevice(false);

if (!usedDevice.IsOpen)
{
    Console.WriteLine("Could not open device exclusively :(");
    usedDevice.OpenDevice(false);
}

using NewCaptureWorker captureWorker = new NewCaptureWorker(usedDevice);
captureWorker.Start();

while (usedDevice.IsConnected)
{
    if (Console.KeyAvailable)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Escape)
        {
            Console.WriteLine("ESC pressed, exiting...");
            break;
        }
    }
    
    Thread.Sleep(200);
}

