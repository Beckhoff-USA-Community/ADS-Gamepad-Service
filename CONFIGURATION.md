# Configuration

The service reads its settings from appsettings.json in the same directory as the executable. Edit the file, then restart the service to apply the changes. If a value is invalid the service refuses to start and writes the reason to the Windows Event Log, so a typo cannot silently run with wrong settings.

## Settings

The Service section holds the values specific to this application.

| Setting | Default | Meaning |
| --- | --- | --- |
| AmsPort | 25733 | The ADS port the server registers with the local ADS router. The PLC library connects to this port, so only change it if you also change the port on the PLC side. |
| ServerName | XboxAdsServer | The name of the registration. It appears in ADS router diagnostics and has no effect on the PLC connection. |
| MaxControllers | 4 | How many controller slots are polled, from 1 to 4. Slots above this count still answer PLC reads but always report as disconnected. Four is the most the underlying Microsoft XInput API supports. |

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
    "MaxControllers": 2
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  }
}
```

This example polls only controllers one and two and keeps the standard port and name.
