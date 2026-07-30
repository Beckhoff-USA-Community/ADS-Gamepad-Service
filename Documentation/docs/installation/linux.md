# Installation on Beckhoff RT Linux

The service runs on Beckhoff RT Linux as a systemd service and reads a PlayStation 5 DualSense controller. The ADS side is identical to Windows, so the PLC library and the TcCOM module work unchanged.

Linux is DualSense only, and the reason is worth understanding when you plan a machine. The DualSense speaks plain USB HID, a standard the kernel serves out of the box, so the service reads it directly with no driver at all. Xbox controllers speak a proprietary USB protocol that needs a kernel driver, and Bluetooth of any kind needs the kernel Bluetooth stack; the standard Beckhoff kernel ships with neither, and a service alone cannot replace kernel support. On a stock system, plan on a DualSense with a cable. The service itself also speaks Bluetooth and uses it on a kernel that provides the stack; the Bluetooth section below has the details.

## Requirements

* A Beckhoff RT Linux system with the TwinCAT runtime installed and running. The service registers its ADS port with the runtime's router.
* For the build path below, the .NET SDK, version 10 or later, on the machine you build on. The target itself needs no .NET install, and the Debian package needs no build machine at all.
* A DualSense controller. For use on the cable, unpair it from every phone, laptop or PlayStation first: a pad that still holds a Bluetooth pairing sends its buttons to that device even while the cable is plugged in here, and from the PLC that looks like a connected pad that never reacts.

## Install from the Debian package

The simplest install is the Debian package. Every release build produces it as a workflow artifact named debian-package, and it can be built locally on any Linux machine from the repository root:

```
dotnet publish src/AdsGamepadService -c Release -r linux-x64 --self-contained -o linux/publish
sh linux/build-deb.sh
```

Copy the package to the target and install it:

```
sudo apt install ./ads-gamepad-service_*_amd64.deb
```

The package performs the same setup as the install script below: it creates the service account, grants it access to the DualSense through a udev rule and to the TwinCAT router through its access group, and enables and starts the systemd unit. Settings live in /opt/ads-gamepad-service/appsettings.json and survive upgrades, and installing the package over an earlier script install takes it over in place and keeps the configuration. Upgrades are the same apt line with the newer package file. Removing the package with apt remove keeps appsettings.json for a later install; apt purge removes it as well.

## Build and install with the scripts

On your build machine, from the repository root:

```
dotnet publish src/AdsGamepadService -c Release -r linux-x64 --self-contained -o linux/publish
```

Copy the linux directory to the target, then on the target:

```
cd linux
sudo sh ./install.sh
```

The install creates a service account, grants it access to the DualSense through a udev rule and to the TwinCAT router through its access group, and starts the systemd unit. Settings live in /opt/ads-gamepad-service/appsettings.json and survive upgrades; the Linux default maps controller slot one to the DualSense.

Watch the service:

```
systemctl status adsgamepad
journalctl -u adsgamepad -f
```

Upgrades are the same two steps again: publish, then rerun the install script. To remove the service, run `sudo sh ./uninstall.sh` from the same directory.

## Bluetooth

The service reads a DualSense over Bluetooth exactly as on Windows, with the cable preferred when both transports are present. This needs two things the standard Beckhoff kernel does not ship: the kernel Bluetooth stack and a Bluetooth adapter the kernel drives. On a kernel that provides them, install the BlueZ tools with `sudo apt install bluez`, then pair the pad once. Start `bluetoothctl` and register an agent before pairing; without one no bond is stored and the pad drops right back off:

```
agent NoInputNoOutput
default-agent
scan on
```

Hold the Create and PS buttons on the pad until the light bar flashes rapidly, wait for the controller to appear in the scan, then, with its address:

```
pair <address>
trust <address>
connect <address>
```

Trusting the pad lets it reconnect on its own: it sleeps when idle, and a press of the PS button brings it back. If the bluetooth service log shows a rejected input connection after pairing, set `ClassicBondedOnly=false` in /etc/bluetooth/input.conf and restart the bluetooth service; the pad opens its input channel faster than the bond lands, and BlueZ rejects that by default. The udev rule shipped with the service covers the Bluetooth device node, so the service needs no extra permissions.
