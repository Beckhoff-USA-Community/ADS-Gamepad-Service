# Configuration

The service reads its settings from appsettings.json in the same directory as the executable. Edit the file, then restart the service to apply the changes. If a value is invalid the service refuses to start and writes the reason to the Windows Event Log, so a typo cannot silently run with wrong settings.

## Settings

The Service section holds the values specific to this application.

| Setting | Default | Meaning |
| --- | --- | --- |
| AmsPort | 25733 | The ADS port the server registers with the local ADS router. The PLC library connects to this port, so only change it if you also change the port on the PLC side. |
| ServerName | XboxAdsServer | The name of the registration. It appears in ADS router diagnostics and has no effect on the PLC connection. |
| MaxControllers | 4 | How many controller slots are polled, from 1 to 4. Slots above this count still answer PLC reads but always report as disconnected. |
| SlotSources | all XInput | The input backend for each of the four slots, in slot order. XInput reads the slot number as XInput controller index, the behavior of all releases before 2.2.0. DualSense reads one PlayStation 5 controller, over USB or Bluetooth; at most one slot can use it. Missing entries mean XInput. |

## PlayStation controllers

From version 2.2.0 a slot can read a PlayStation 5 DualSense controller instead of an Xbox controller. Set the slot's entry in SlotSources to DualSense. Prefer a high slot for it: every XInput slot is tied to the Xbox controller with the same number, so a DualSense on slot one would leave the first Xbox controller unread. The pad connects over USB or Bluetooth, and the same DualSense entry covers both: pair the pad once with the system and the service finds it, with the cable preferred when both transports are present. On Linux, Bluetooth additionally needs a kernel that includes the Bluetooth stack; the standard Beckhoff RT Linux kernel does not ship it, so plan on the cable there unless yours does. Sticks, triggers and buttons arrive at the PLC in the same value ranges as from an Xbox pad, with Cross, Circle, Square and Triangle on the A, B, X and Y bits, Create on Back and Options on Start. Create and Options additionally appear on the previously unused button bits 10 and 11, so a program that wants to tell the two pad families apart can.

One thing to watch: a DualSense that is still paired with a phone, laptop or PlayStation sends its buttons to that device even while the cable is plugged in here, and from this side that looks like a connected pad that never reacts. The service logs a warning to the Event Log when it detects that state. Unpair the controller from every other device before using it.

## Logging

The Logging section uses the standard .NET logging configuration. When the application runs as a Windows service its log messages go to the Windows Event Log under the source name ADS Gamepad Service. When you start the executable from a console the messages appear in that console instead.

The default level is Information, which includes service startup, shutdown, and controller connect and disconnect events. Set the level to Warning if you only want to hear about problems.

## Upgrading from the TwinCAT Xbox Controller Service

The predecessor of this project installed as TwinCAT Xbox Controller Service under C:\Program Files\Beckhoff Community and was started by hand from the Start Menu rather than running as a service. Remove it, or at least make sure it is not running, before installing this service: both programs register ADS port 25733 with the router, and the second one to start will fail. Uninstall the old version from the Windows installed applications list. If you ever registered the old application as a service yourself, be aware that its service name shipped with a spelling mistake, Contoller instead of Controller, so search for both spellings when cleaning up.

## Example

```json
{
  "Service": {
    "AmsPort": 25733,
    "ServerName": "XboxAdsServer",
    "MaxControllers": 2,
    "SlotSources": [ "XInput", "DualSense" ]
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  }
}
```

This example polls two slots, an Xbox controller on slot one and a DualSense on slot two, and keeps the standard port and name.
