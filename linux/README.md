# ADS Gamepad Service on Beckhoff RT Linux

The service runs on Beckhoff RT Linux as a systemd service and reads a
PlayStation 5 DualSense controller over the hidraw interface. Xbox
controllers are not supported on Linux: the Beckhoff kernel does not include
the driver they need, so Linux is DualSense only. The pad connects over USB;
on a kernel that provides the Bluetooth stack, which the standard Beckhoff
kernel does not, the service also reads it over Bluetooth with the cable
preferred. The ADS side is unchanged, the service registers port 25733 with
the local TwinCAT router and the PLC library and TcCOM module work exactly
as on Windows.

## Requirements

* A Beckhoff RT Linux system with the TwinCAT runtime installed and running
  (the tc31-xar-um package; the service registers with its router).
* The .NET SDK, version 10 or later, on the machine you build on. The
  target needs no .NET install, the publish output is self contained.
* A DualSense controller. For use on the cable, unpair it from every
  phone, laptop or PlayStation first: a pad that still holds a Bluetooth
  pairing sends its buttons there even while the cable is plugged in here.
  The Bluetooth pairing steps live on the Linux installation page of the
  documentation.

## Install from the Debian package

The simplest install is the Debian package. Every release build produces it
as a workflow artifact named debian-package, and it can be built locally on
any Linux machine, from the repository root:

```
dotnet publish src/AdsGamepadService -c Release -r linux-x64 --self-contained -o linux/publish
sh linux/build-deb.sh
```

Copy the package to the target and install it:

```
sudo apt install ./ads-gamepad-service_*_amd64.deb
```

The package performs the same setup as the install script below: it creates
the adsgamepad service account and the gamepad device access group, installs
the udev rule for the DualSense, and enables and starts the systemd unit.
Settings live in /opt/ads-gamepad-service/appsettings.json and survive
upgrades. Installing the package over an earlier script install takes it
over in place and keeps the configuration.

Removing the package keeps appsettings.json for a later install; a purge
removes it as well:

```
sudo apt remove ads-gamepad-service
sudo apt purge ads-gamepad-service
```

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

The install creates the adsgamepad service account and the gamepad device
access group, installs a udev rule for the DualSense, and starts the
systemd unit. Settings live in /opt/ads-gamepad-service/appsettings.json
and survive upgrades; the Linux default maps controller slot one to the
DualSense. See CONFIGURATION.md in the repository root for every setting.

Watch the service:

```
systemctl status adsgamepad
journalctl -u adsgamepad -f
```

To remove the service:

```
sudo sh ./uninstall.sh
```

## Notes

* The service registers under the AMS Net ID of the Linux system. A PLC on
  the same machine reaches it with an empty NetID string in
  FB_Gamepad_Controller, exactly like on Windows.
* Rumble works over the same wire contract; the service writes it to the
  pad through hidraw.
