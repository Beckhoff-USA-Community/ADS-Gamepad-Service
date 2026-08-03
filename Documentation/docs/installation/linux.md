# Installation on Beckhoff RT Linux

The service runs on Beckhoff RT Linux as a systemd service and reads a PlayStation 5 DualSense controller. The ADS side is identical to Windows, so the PLC library works unchanged, and the compiled TcCOM module ships builds for both the x64 and the ARM systems.

Linux is DualSense only, and the reason is worth understanding when you plan a machine. The DualSense speaks plain USB HID, a standard the kernel serves out of the box, so the service reads it directly with no driver at all. Xbox controllers speak a proprietary USB protocol that needs a kernel driver, and Bluetooth of any kind needs the kernel Bluetooth stack; the standard Beckhoff kernel ships with neither, and a service alone cannot replace kernel support. On a stock x86 system, plan on a DualSense with a cable. On the ARM controllers one rebuilt kernel module is currently needed before even the wired pad can be read; the raw HID layer section below has the steps. The service itself also speaks Bluetooth and uses it on a kernel that provides the stack; the Bluetooth section below has the details.

## Requirements

* A Beckhoff RT Linux system with the TwinCAT runtime installed and running. The service registers its ADS port with the runtime's router.
* For the build path below, the .NET SDK, version 10 or later, on the machine you build on. The target itself needs no .NET install, and the Debian package needs no build machine at all.
* A DualSense controller. For use on the cable, unpair it from every phone, laptop or PlayStation first: a pad that still holds a Bluetooth pairing sends its buttons to that device even while the cable is plugged in here, and from the PLC that looks like a connected pad that never reacts.

## Install from the Debian package

The simplest install is the Debian package, attached to each release on the GitHub Releases page of the repository. Releases from 2.12.0 on carry two packages: amd64 for the x86 controllers and arm64 for the ARM controllers such as the CX8200 and CX9240 series. The package can also be built locally on any Linux machine, from the repository root:

```
dotnet publish src/AdsGamepadService -c Release -r linux-x64 --self-contained -o linux/publish
sh linux/build-deb.sh
```

For an ARM target, publish with `-r linux-arm64` and name the architecture as the third argument: `sh linux/build-deb.sh linux/publish linux arm64`.

Copy the package matching the target and install it:

```
sudo apt install ./ads-gamepad-service_*.deb
```

apt may print a notice that the download is performed unsandboxed because the package file in your home directory is not readable by the _apt system user, and on a minimal image some debconf frontend warnings. Both are harmless and the install completes normally.

The package performs the same setup as the install script below: it creates the service account, grants it access to the DualSense through a udev rule and to the TwinCAT router through its access group, and enables and starts the systemd unit. The shipped configuration maps controller slot one to the DualSense, so the pad works with no edit. Settings live in /opt/ads-gamepad-service/appsettings.json and survive upgrades, and installing the package over an earlier script install takes it over in place and keeps the configuration. After editing settings, apply them with `sudo systemctl restart adsgamepad`. Upgrades are the same apt line with the newer package file. Removing the package with apt remove keeps appsettings.json for a later install; apt purge removes it as well.

## Build and install with the scripts

On your build machine, from the repository root, with `-r linux-arm64` in place of `-r linux-x64` for an ARM target:

```
dotnet publish src/AdsGamepadService -c Release -r linux-x64 --self-contained -o linux/publish
```

Copy the linux directory to the target, then on the target:

```
cd linux
sudo sh ./install.sh
```

The install creates a service account, grants it access to the DualSense through a udev rule and to the TwinCAT router through its access group, and starts the systemd unit. Settings live in /opt/ads-gamepad-service/appsettings.json and survive upgrades; the default maps controller slot one to the DualSense.

Watch the service:

```
systemctl status adsgamepad
journalctl -u adsgamepad -f
```

Upgrades are the same two steps again: publish, then rerun the install script. To remove the service, run `sudo sh ./uninstall.sh` from the same directory.

## The ARM controllers and the raw HID layer

On the ARM controllers such as the CX8200 and CX9240 series, the standard Beckhoff kernel currently ships without the raw HID layer, the hidraw interface the service reads the pad through. The x86 image includes it; the ARM image does not yet. The service installs and runs and the PLC sees the fail safe zero state, but no pad can appear until the layer is present. The check is quick: if `ls /sys/class/hidraw` reports no such directory, the running kernel has no raw HID layer. The procedure below was verified on a CX9240 and a CX8290.

The fix is one rebuilt module. The raw HID layer is part of hid.ko rather than a module of its own, so hid.ko is rebuilt once with the layer enabled and installed as an override next to the stock modules. The first steps are shared with the Bluetooth walkthrough below: install the build tools and the headers for the running kernel (BlueZ and the firmware are not needed for this), and fetch the kernel source at the exact commit of the running kernel. Then configure, build and install the override:

```
cp /boot/config-$(uname -r) .config
scripts/config --enable HIDRAW
make olddefconfig
make modules_prepare
cp /usr/src/linux-headers-$(uname -r)/Module.symvers .
make -j$(nproc) M=drivers/hid modules
sudo install -D -m 0644 -t /lib/modules/$(uname -r)/updates drivers/hid/hid.ko
sudo depmod -a
sudo reboot
```

After the reboot the DualSense device node appears and the udev rule the package installed grants the service access; the pad comes onto the wire with no further step. The kernel upgrade caveat from the Bluetooth section applies here the same way: a kernel package upgrade brings a stock kernel without the layer again, and the module must be rebuilt for the new release or the pad silently disappears after the reboot.

## Bluetooth

The service reads a DualSense over Bluetooth exactly as on Windows, with the cable preferred when both transports are present. Two things are needed that the standard Beckhoff kernel does not ship: the kernel Bluetooth stack and a Bluetooth adapter. This section walks through both. It is the advanced path; a wired DualSense needs none of it.

The quick way to check a system is `sudo systemctl status bluetooth`: the message `unmet condition check ConditionPathIsDirectory=/sys/class/bluetooth` means the running kernel has no Bluetooth support, and `bluetoothctl` failing with `Unable to open mgmt_socket` means the same. In that state the kernel modules must be built first, as described next. The whole procedure was verified on a CX2043.

### The adapter

The TP-Link UB500 is the adapter this project tests with on both platforms, an inexpensive USB adapter with a Realtek chip whose firmware ships in the Debian firmware-realtek package. Other adapters can work when the kernel and the firmware archive know their chip, but the UB500 is the verified path.

### Building the kernel Bluetooth modules

The Beckhoff kernel is built without the Bluetooth stack, so the modules are built once from the matching kernel source and installed alongside the stock modules. Kernel modules only load when they match the running kernel exactly, so the build must use the same source, the same configuration, and the same symbol versions. Beckhoff publishes the kernel source at https://github.com/Beckhoff/linux, and the commit the running kernel was built from is the last part of the kernel release string.

Install the build tools, the headers for the running kernel, BlueZ and the firmware:

```
sudo apt install linux-headers-$(uname -r) build-essential git flex bison bc libssl-dev libelf-dev dwarves python3 bluez firmware-realtek
```

Fetch the kernel source at the exact commit of the running kernel. The kernel release string only carries the first characters of the commit id and GitHub serves fetches for full ids only, so the first command resolves the full id through the GitHub API; the echo should print a 40 character id that starts with the characters from the kernel release:

```
mkdir -p ~/btbuild && cd ~/btbuild
kr=$(uname -r)
commit=$(curl -s "https://api.github.com/repos/Beckhoff/linux/commits/${kr##*-}" | grep -m 1 '"sha"' | cut -d '"' -f 4)
echo "$commit"
git init linux-bhf && cd linux-bhf
git remote add origin https://github.com/Beckhoff/linux.git
git fetch --depth 1 origin "$commit"
git checkout --detach FETCH_HEAD
```

Configure with the running kernel's own configuration plus the Bluetooth options, prepare the tree, and take the symbol versions from the headers package so the built modules match the stock kernel:

```
cp /boot/config-$(uname -r) .config
scripts/config --module BT --module BT_HIDP --module BT_HCIBTUSB --module UHID
make olddefconfig
make modules_prepare
cp /usr/src/linux-headers-$(uname -r)/Module.symvers .
```

Build the Bluetooth subsystem, the USB adapter driver and the userspace HID module, then install and load them. The adapter driver uses symbols the subsystem build exports, so its build line names the symbol list the first build wrote:

```
make -j$(nproc) M=net/bluetooth modules
make -j$(nproc) M=drivers/bluetooth KBUILD_EXTRA_SYMBOLS=$PWD/net/bluetooth/Module.symvers modules
make -j$(nproc) M=drivers/hid modules
sudo install -D -m 0644 -t /lib/modules/$(uname -r)/extra net/bluetooth/bluetooth.ko net/bluetooth/hidp/hidp.ko drivers/bluetooth/btusb.ko drivers/bluetooth/btrtl.ko drivers/bluetooth/btintel.ko drivers/bluetooth/btbcm.ko drivers/hid/uhid.ko
sudo depmod -a
echo uhid | sudo tee /etc/modules-load.d/uhid.conf
sudo modprobe uhid
sudo modprobe btusb
```

On the ARM controllers two additions are needed, verified on a CX9240. First, the raw HID layer section above applies before any of this; Bluetooth needs that rebuilt module as well. Second, the ARM kernel configuration also lacks the elliptic curve cryptography that Bluetooth pairing uses, so two crypto modules join the build: build the crypto directory after copying Module.symvers, hand the Bluetooth build their symbol list in place of the plain net/bluetooth line above, and install the two extra modules along with the others:

```
make -j$(nproc) M=crypto modules
make -j$(nproc) M=net/bluetooth KBUILD_EXTRA_SYMBOLS=$PWD/crypto/Module.symvers modules
sudo install -D -m 0644 -t /lib/modules/$(uname -r)/extra crypto/ecc.ko crypto/ecdh_generic.ko
```

The configuration step needs no extra option for this; enabling Bluetooth selects the crypto entries on its own, and the build lines above pick them up.

The builds print "Skipping BTF generation" for each module because the build has no vmlinux; that only skips optional debug information and the modules are complete. The drivers/hid build also produces a few extra HID modules that the configuration enables; only uhid.ko is installed. After loading, dmesg reports the out of tree modules as unsigned; that is expected and harmless. With the adapter plugged in, `sudo systemctl restart bluetooth` should now leave the Bluetooth service active, and `bluetoothctl show` lists the adapter. The bluetoothd log lines about missing bnep protocol support and the sap server failing with Operation not permitted are expected: those are networking and phone profiles that were deliberately not built, and the pad does not use them. After a reboot the modules load on their own when the adapter is present; BlueZ serves the pad through the uhid module, which the modules-load entry keeps loading at boot.

One caveat to plan around: the modules live under the exact kernel version they were built for. A kernel package upgrade brings a new kernel without Bluetooth again, and the build above must be repeated for the new release, or Bluetooth silently disappears after the reboot.

### Allowing the pad to connect

The DualSense opens its input channel faster than BlueZ stores the bond, and BlueZ rejects that by default. Set `ClassicBondedOnly=false` in /etc/bluetooth/input.conf and restart the bluetooth service. Be aware this relaxes a spoofing protection for input devices on this system; it is required for the pad to reconnect reliably.

### Pairing

Pair the pad once. Start `bluetoothctl` and register an agent before pairing; without one no bond is stored and the pad drops right back off:

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

Trusting the pad lets it reconnect on its own: it sleeps when idle, and a press of the PS button brings it back. A rejected input connection in the bluetooth service log means the ClassicBondedOnly setting from the section above is still on its default. The udev rule shipped with the service covers the Bluetooth device node, so the service needs no extra permissions, and the pad appears through the same DualSense slot as on the cable.
