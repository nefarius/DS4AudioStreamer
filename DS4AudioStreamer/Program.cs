using DS4AudioStreamer;
using DS4AudioStreamer.Sound;

using DS4Windows;

List<HidDevice> hidDevices = DeviceEnumerator.FindDevices();

HidDevice? usedDevice = hidDevices.FirstOrDefault();

if (null == usedDevice)
{
    Console.WriteLine("No device found");
    return;
}

usedDevice.OpenDevice(true);

if (!usedDevice.IsOpen)
{
    Console.WriteLine("Could not open device exclusively :(");
    usedDevice.OpenDevice(false);
}

NewCaptureWorker captureWorker = new NewCaptureWorker(usedDevice);
captureWorker.Start();

while (usedDevice.IsConnected)
{
    Thread.Sleep(1000);
}