# The Service

The service is the bridge between the controllers and ADS. It has no cycle of its own: every ADS read from the PLC triggers a fresh poll of the controller hardware, so the effective update rate is the PLC cycle time and the data is always as fresh as the request. On Windows it runs as a Windows service with delayed automatic start, on Beckhoff RT Linux as a systemd unit. It registers ADS port 25733 with the local TwinCAT router.

## Configuration

Settings live in appsettings.json next to the service executable. Edit the file, then restart the service. An invalid value stops the service with a clear explanation in the log instead of silently running with wrong settings.

| Setting | Default | Meaning |
| --- | --- | --- |
| AmsPort | 25733 | The ADS port the server registers with the local router. The PLC library and the TcCOM module connect to this port, so only change it if you also change it on the consuming side. |
| ServerName | XboxAdsServer | The name of the registration. It appears in router diagnostics and has no effect on the connection. The name is part of the frozen wire identity and predates the project rename. |
| MaxControllers | 4 | How many controller slots are polled, from 1 to 4. Slots above this count still answer reads but always report as disconnected. |
| SlotSources | all XInput | The input backend for each of the four slots, in slot order. XInput polls the Xbox controller with the same number as the slot. DualSense reads one wired PlayStation 5 controller; at most one slot can use it. Missing entries mean XInput. |

Example with an Xbox controller on slot one and a DualSense on slot two:

```json
{
  "Service": {
    "AmsPort": 25733,
    "ServerName": "XboxAdsServer",
    "MaxControllers": 2,
    "SlotSources": [ "XInput", "DualSense" ]
  }
}
```

Prefer a high slot for the DualSense: every XInput slot is tied to the Xbox controller with the same number, so a DualSense on slot one would leave the first Xbox controller unread.

## Reaching the service from another machine

The service answers requests through the TwinCAT router of the machine it runs on. When the PLC or the TcCOM module runs on that same machine, the defaults already point at it: an empty NetID on the function block, the default ServiceAmsNetId on the module. Nothing to set up.

When the consumer runs on a different machine, two things are needed:

* An ADS route between the two machines. A route is a trust entry that two TwinCAT routers store about each other; without it the routers refuse to talk. Add it the same way you would to reach any remote PLC, for example through the route dialog in the engineering environment or from the TwinCAT icon on the target.
* The AMS Net ID of the service machine on the consuming side: pass it as the NetID argument of the function block, or set it in the ServiceAmsNetId parameter of the TcCOM module. The Net ID is shown in the same dialogs where the route is added.

The service itself needs no route configuration. It never opens connections on its own, so only the consuming side and its router need to know the way.

## Xbox notes

After a machine reboot a wired Xbox controller may not report as connected right away. An Elite controller sleeps until its Xbox button is pressed, and the Xbox Adaptive Joystick starts in a generic input mode that XInput does not see until it is unplugged and plugged back in. Both behaviors live in the controller, not in the service; once the pad reports, data flows normally.

## PlayStation notes

The DualSense connects over USB; Bluetooth is not supported. A pad that is still paired with a phone, laptop or PlayStation sends its buttons to that device even while the cable is plugged in, which from this side looks like a connected pad that never reacts. The service detects that state and logs a warning. Unpair the controller from every other device before use.

## Logging

On Windows the service writes to the Windows Event Log under the source name ADS Gamepad Service. On Linux it writes to the systemd journal. The default level is Information, which includes startup, shutdown, and controller connect and disconnect events; set the Logging level to Warning in appsettings.json if you only want to hear about problems.

## Failure behavior

The service never invents data. A disconnected controller reads as all zeroes with the connected bit clear, and if the service or the ADS connection goes away entirely, the PLC library zeroes its inputs on the failed read. When the service process fails it exits nonzero, and both the Windows service recovery settings and the systemd unit restart it automatically.
