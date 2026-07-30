# Installation on Windows

The project ships as TwinCAT packages on the GitHub package feed of the repository. Two workloads carry everything: the runtime workload installs the Windows service on the machine with the controllers, the engineering workload installs the PLC library, this documentation, and the TcCOM module source on the machine that runs XAE, the TwinCAT engineering environment. Both show up as one card named ADS Gamepad in the TwinCAT Package Manager.

## Adding the feed

GitHub requires a login for its package feeds, even for public packages, so you need a GitHub account and a personal access token. Create a token of the classic type with the read:packages scope under Developer settings in your GitHub account settings, then from an administrator PowerShell:

```powershell
tcpkg source add -n AdsGamepad -s https://nuget.pkg.github.com/Beckhoff-USA-Community/index.json -u <your GitHub user name> --take 100
tcpkg config unset -n VerifySignatures
```

Both settings are required. GitHub rejects search requests larger than 100 results per page while the package manager asks for 500 by default, so without `--take 100` adding the feed fails with the error "Failed to retrieve metadata from source". Community packages carry no Beckhoff signature, so installs fail while signature verification is on.

## Installing

Run the line that matches the system, or both on a machine that does both jobs:

```powershell
tcpkg install Beckhoff-USA-Community.AdsGamepad.XAR -y
tcpkg install Beckhoff-USA-Community.AdsGamepad.XAE -y
```

The runtime workload registers the service with delayed automatic start. The engineering workload puts the AdsGamepad library into the XAE library repository, ready to reference, and installs everything under C:\Program Files\Beckhoff USA Community\ADS Gamepad.

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
tcpkg uninstall Beckhoff-USA-Community.AdsGamepad.XAE Beckhoff-USA-Community.XAE.PLC.Lib.AdsGamepad Beckhoff-USA-Community.XAE.Documentation.AdsGamepad Beckhoff-USA-Community.XAE.TcComSource.AdsGamepad -y
```

The service keeps its appsettings.json so a later install finds the configuration again. Delete the ADS Gamepad folder under C:\Program Files\Beckhoff USA Community by hand if that should go too.

## Wireless Xbox controllers

Wireless Xbox controllers connect through the official Xbox Wireless Adapter, a small USB dongle from Microsoft. On a machine with internet access Windows fetches its driver on first plug in. Industrial machines are usually offline, so fetch the driver package for the Xbox Wireless Adapter from the Microsoft Update Catalog on any connected PC, copy it over, extract the .cab file, and install it from an administrator PowerShell:

```powershell
expand -F:* .\<driver package>.cab C:\Temp\XboxAdapterDriver
pnputil /add-driver C:\Temp\XboxAdapterDriver\*.inf /install
```

Then press the pairing button on the adapter and hold the sync button on the controller until its Xbox button stays lit. The pad appears as a normal XInput controller and the service needs no configuration change. Pairing Xbox controllers with generic Bluetooth adapters proved unreliable in testing; the official adapter is the supported path.
