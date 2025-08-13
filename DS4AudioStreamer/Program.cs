using DS4AudioStreamer;
using DS4AudioStreamer.Sound;

using DS4Windows;

using Nefarius.Utilities.DeviceManagement.PnP;

List<HidDevice> hidDevices = DeviceEnumerator.FindDevices();

using HidDevice? usedDevice = hidDevices.FirstOrDefault();

if (usedDevice is null)
{
    Console.WriteLine("No compatible DS4 device found");
    return;
}

PnPDevice? pnpDevice = PnPDevice.GetDeviceByInterfaceId(usedDevice.DevicePath);

if (pnpDevice is null)
{
    Console.WriteLine("Failed to lookup PNP device details");
    return;
}

Console.WriteLine($"Found controller device {pnpDevice}");

string? enumerator = pnpDevice.Parent?.GetProperty<string>(DevicePropertyKey.Device_EnumeratorName);

if (enumerator is not null && !enumerator.Contains("BTH"))
{
    Console.WriteLine($"Device {pnpDevice} is not connected via Bluetooth");
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

Console.WriteLine("Started sending audio from default output device. Press F3 to exit...");

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