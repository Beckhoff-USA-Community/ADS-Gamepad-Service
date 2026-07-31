# Installation on Windows

The project ships as TwinCAT packages on the GitHub package feed of the Beckhoff USA Community organization. Two workloads carry everything: the runtime workload installs the Windows service on the machine with the controllers, the engineering workload installs the PLC library, this documentation, and the compiled Gamepad TcCOM module on the machine that runs XAE, the TwinCAT engineering environment. Both show up as one card named ADS Gamepad in the TwinCAT Package Manager.

## Adding the feed

GitHub requires a login for its package feeds, even for public packages, so you need a GitHub account and a personal access token. Create a token of the classic type with the read:packages scope under Developer settings in your GitHub account settings, then from an administrator PowerShell:

```powershell
tcpkg config unset -n VerifySignatures
tcpkg source add -n "Beckhoff USA Community" -s https://nuget.pkg.github.com/Beckhoff-USA-Community/index.json -u <your GitHub user name> --take 100
```

Both settings are required, and the order matters. Community packages carry no Beckhoff signature, including the disclaimer package the feed presents while it is being added, so verification must be off before the feed is added. GitHub rejects search requests larger than 100 results per page while the package manager asks for 500 by default, so without `--take 100` adding the feed fails with the error "Failed to retrieve metadata from source". Adding the feed shows the community disclaimer; accept it to continue.

## Installing

Run the line that matches the system, or both on a machine that does both jobs:

```powershell
tcpkg install Beckhoff-USA-Community.AdsGamepad.XAR -y
tcpkg install Beckhoff-USA-Community.AdsGamepad.XAE -y
```

The runtime workload registers the service with delayed automatic start. The engineering workload puts the AdsGamepad library into the XAE library repository ready to reference, puts the compiled Gamepad TcCOM module into the TwinCAT module repository ready to add to a project, and installs the documentation under C:\Program Files\Beckhoff USA Community\ADS Gamepad.

## Updating

Upgrade a workload and its component packages follow, since every workload pins its components at exact versions. The service keeps an edited appsettings.json across upgrades, even when the upgrade moves the installation to a new location.

```powershell
tcpkg upgrade Beckhoff-USA-Community.AdsGamepad.XAR -y
tcpkg upgrade Beckhoff-USA-Community.AdsGamepad.XAE -y
```

## Uninstalling

Uninstalling a workload removes only the grouping package itself, so list the component packages with it:

```powershell
tcpkg uninstall Beckhoff-USA-Community.AdsGamepad.XAR Beckhoff-USA-Community.XAR.Service.AdsGamepad -y
tcpkg uninstall Beckhoff-USA-Community.AdsGamepad.XAE Beckhoff-USA-Community.XAE.PLC.Lib.AdsGamepad Beckhoff-USA-Community.XAE.Documentation.AdsGamepad Beckhoff-USA-Community.XAE.TcCom.AdsGamepad -y
```

The service keeps its appsettings.json so a later install finds the configuration again. Delete the ADS Gamepad folder under C:\Program Files\Beckhoff USA Community by hand if that should go too.

## Wireless Xbox controllers

Wireless Xbox controllers connect through the official Xbox Wireless Adapter, a small USB dongle from Microsoft. On a machine with internet access Windows fetches its driver on first plug in. Industrial machines are usually offline, so fetch the driver package for the Xbox Wireless Adapter from the Microsoft Update Catalog on any connected PC, copy it over, extract the .cab file, and install it from an administrator PowerShell:

```powershell
expand -F:* .\<driver package>.cab C:\Temp\XboxAdapterDriver
pnputil /add-driver C:\Temp\XboxAdapterDriver\*.inf /install
```

Then press the pairing button on the adapter and hold the sync button on the controller until its Xbox button stays lit. The pad appears as a normal XInput controller and the service needs no configuration change. Xbox controllers failed to pair with Realtek based Bluetooth adapters in testing, including the adapter the next section recommends for the DualSense; the pairing handshake itself fails, so it is not a settings problem. The official adapter is the supported path.

## Bluetooth for the DualSense

A wireless DualSense connects over regular Bluetooth, but industrial PCs rarely include a Bluetooth radio, so most machines need a USB Bluetooth adapter first. The TP-Link UB500 is the adapter this project tests with. On a machine with internet access Windows fetches its driver on first plug in. On an offline machine, download the UB500 driver package from the TP-Link support site on any connected PC, copy it over, extract it, and install the driver from an administrator PowerShell:

```powershell
pnputil /add-driver C:\Temp\UB500Driver\*.inf /subdirs /install
```

Then plug in the adapter, put the controller into pairing mode by holding its Create and PS buttons until the light bar flashes, and pair it under the Windows Bluetooth settings. The service picks the pad up with no configuration change; the service page describes the pairing behavior in detail. This adapter serves the DualSense only. Xbox controllers do not pair with it, as described above.
