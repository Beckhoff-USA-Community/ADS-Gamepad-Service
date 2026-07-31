# The Service

The service is the bridge between the controllers and ADS. It has no cycle of its own: every ADS read from the PLC triggers a fresh poll of the controller hardware, so the effective update rate is the PLC cycle time and the data is always as fresh as the request. On Windows it runs as a Windows service with delayed automatic start, on Beckhoff RT Linux as a systemd unit. It registers ADS port 25733 with the local TwinCAT router.

## Configuration

Settings live in appsettings.json next to the service executable. On Windows the package installs it at C:\Program Files\Beckhoff USA Community\ADS Gamepad\Service\appsettings.json; on Linux it lives at /opt/ads-gamepad-service/appsettings.json. Edit the file, then restart the service to apply it: `Restart-Service AdsGamepadService` from an administrator PowerShell on Windows, `sudo systemctl restart adsgamepad` on Linux. An invalid value stops the service with a clear explanation in the log instead of silently running with wrong settings.

| Setting | Default | Meaning |
| --- | --- | --- |
| AmsPort | 25733 | The ADS port the server registers with the local router. The PLC library and the TcCOM module connect to this port, so only change it if you also change it on the consuming side. |
| ServerName | XboxAdsServer | The name of the registration. It appears in router diagnostics and has no effect on the connection. The name is part of the frozen wire identity and predates the project rename. |
| MaxControllers | 1 | How many controller slots are polled, from 1 to 4. Slots above this count still answer reads but always report as disconnected. |
| SlotSources | DualSense on slot one | The input backend for each polled slot, in slot order. XInput polls the Xbox controller with the same number as the slot. DualSense reads one PlayStation 5 controller, over USB or Bluetooth; at most one slot can use it. Missing entries mean XInput. |

The shipped configuration reads one DualSense and needs no edit for it. For Xbox controllers, set every polled slot to XInput:

```json
{
  "Service": {
    "AmsPort": 25733,
    "ServerName": "XboxAdsServer",
    "MaxControllers": 4,
    "SlotSources": [ "XInput", "XInput", "XInput", "XInput" ]
  }
}
```

Mixing works as well, an Xbox controller on slot one and a DualSense on slot two:

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

When mixing pad families, prefer a high slot for the DualSense: every XInput slot is tied to the Xbox controller with the same number, so a DualSense on slot one leaves the first Xbox controller unread.

## Reaching the service from another machine

The service answers requests through the TwinCAT router of the machine it runs on. When the PLC or the TcCOM module runs on that same machine, the defaults already point at it: an empty NetID on the function block, the default ServiceAmsNetId on the module. Nothing to set up.

When the consumer runs on a different machine, two things are needed:

* An ADS route between the two machines. A route is a trust entry that two TwinCAT routers store about each other; without it the routers refuse to talk. Add it the same way you would to reach any remote PLC, for example through the route dialog in the engineering environment or from the TwinCAT icon on the target.
* The AMS Net ID of the service machine on the consuming side: pass it as the NetID argument of the function block, or set it in the ServiceAmsNetId parameter of the TcCOM module. The Net ID is shown in the same dialogs where the route is added.

The service itself needs no route configuration. It never opens connections on its own, so only the consuming side and its router need to know the way.

The route does not have to be a direct network connection. TwinCAT can carry ADS over MQTT through a broker, which machines use to reach each other across separated networks and cloud boundaries. To the service nothing changes: the consumer still addresses the AMS Net ID of the service machine, and the routers handle the transport. Setting up ADS over MQTT is router configuration, covered by the Beckhoff documentation; the service needs no setting for it.

## Xbox notes

After a machine reboot a wired Xbox controller may not report as connected right away. An Elite controller sleeps until its Xbox button is pressed, and the Xbox Adaptive Joystick starts in a generic input mode that XInput does not see until it is unplugged and plugged back in. Both behaviors live in the controller, not in the service; once the pad reports, data flows normally.

Wireless Xbox controllers connect through the official Xbox Wireless Adapter and then appear to the service like any other XInput pad; the same XInput slot setting covers wired and wireless. Xbox controllers failed to pair with Realtek based Bluetooth adapters in testing; the pairing handshake itself fails, so this is not a settings problem. Use the official adapter. A sleeping pad, a drained battery or a radio drop all look the same on the wire: the connected bit clears and every input reads zero. That fail safe is deliberate, and a program driving motion from a wireless pad should watch the connected state every cycle and decide what the machine does when it drops.

## PlayStation notes

The DualSense is the controller this project recommends. One USB cable is the whole setup, the pad reconnects on its own after cable pulls and reboots, and it is the only pad that reports a real battery percentage and charging state, served through the extended data block along with the touchpad and the motion sensors.

The DualSense connects over USB or Bluetooth. Pair it once with the system, on Windows through the Bluetooth settings and on Linux with bluetoothctl, and the service picks it up through the same DualSense slot entry, so switching between cable and Bluetooth needs no configuration change; when both transports are present the cable wins. The log states which transport a pad connected over. Industrial PCs often ship without a Bluetooth radio; the installation pages cover adding one. On Linux, Bluetooth additionally needs a kernel that includes the Bluetooth stack, which the standard Beckhoff kernel does not ship; the Linux installation page has the details.

A pad that is still paired with a phone, laptop or PlayStation sends its buttons to that device even while the cable is plugged in, which from this side looks like a connected pad that never reacts. The service detects that state and logs a warning. Unpair the controller from every other device before wired use, and expect a Bluetooth pad to go to sleep when idle; the PS button wakes it and the service reconnects on its own.

## Logging

On Windows the service writes to the Windows Event Log under the source name ADS Gamepad Service. On Linux it writes to the systemd journal. The default level is Information, which includes startup, shutdown, and controller connect and disconnect events; set the Logging level to Warning in appsettings.json if you only want to hear about problems.

## Failure behavior

The service never invents data. A disconnected controller reads as all zeroes with the connected bit clear, and if the service or the ADS connection goes away entirely, the PLC library zeroes its inputs on the failed read. When the service process fails it exits nonzero, and both the Windows service recovery settings and the systemd unit restart it automatically.
