# ADS Gamepad Service

ADS Gamepad Service connects Xbox and PlayStation gamepads attached to PCs, IPCs, and CX devices, then hosts the input data over ADS for the PLC to access. It runs as a system service on TwinCAT Windows systems and on Beckhoff RT Linux, and includes a PLC library for reading gamepad data in your project. The full documentation lives under Documentation in this repository; for Linux setup see the linux directory.

### Installing with the TwinCAT Package Manager

The service ships as TwinCAT packages on the GitHub package feed of this repository. GitHub requires a login for its package feeds, even for public packages, so you need a GitHub account and a personal access token. Create a token of the classic type with the read:packages scope under Developer settings in your GitHub account settings.

Add the feed from an administrator PowerShell, then install the workload that matches the system. The runtime workload puts the service on the system that has the controllers attached. The engineering workload puts the PLC library, the documentation and the TcCOM module source on the system that runs XAE. Enter the token when prompted for a password. Run only the install line that matches the system, or both on a machine that does both jobs.

```powershell
tcpkg source add -n AdsGamepad -s https://nuget.pkg.github.com/Beckhoff-USA-Community/index.json -u <your GitHub user name> --take 100
tcpkg config unset -n VerifySignatures
tcpkg install Beckhoff-USA-Community.AdsGamepad.XAR -y
tcpkg install Beckhoff-USA-Community.AdsGamepad.XAE -y
```

Two settings in these commands are required, not optional:

* `--take 100` limits search requests to 100 results per page. GitHub rejects anything larger, while the TwinCAT Package Manager asks for 500 by default. Without this option, adding the feed fails with the error "Failed to retrieve metadata from source".
* `tcpkg config unset -n VerifySignatures` allows packages that are not signed by Beckhoff. Community packages carry no Beckhoff signature, so installs fail while signature verification is on.

If adding the feed still fails, confirm the token is the classic type (it starts with ghp_), that it has the read:packages scope, and that it has not expired.

### Updating

New releases appear on the same feed. Upgrade a workload from an administrator PowerShell and its component packages follow, since every workload pins its components at exact versions. The service keeps an edited appsettings.json across upgrades, even when the upgrade moves the installation to a new location.

```powershell
tcpkg upgrade Beckhoff-USA-Community.AdsGamepad.XAR -y
tcpkg upgrade Beckhoff-USA-Community.AdsGamepad.XAE -y
```

### Uninstalling

Uninstalling a workload removes only the grouping package itself, so list the component packages with it. The service keeps its appsettings.json so a later install finds the configuration again. Delete the ADS Gamepad folder under C:\Program Files\Beckhoff USA Community by hand if that should go too.

```powershell
tcpkg uninstall Beckhoff-USA-Community.AdsGamepad.XAR Beckhoff-USA-Community.XAR.Service.AdsGamepad -y
tcpkg uninstall Beckhoff-USA-Community.AdsGamepad.XAE Beckhoff-USA-Community.XAE.PLC.Lib.AdsGamepad Beckhoff-USA-Community.XAE.Documentation.AdsGamepad Beckhoff-USA-Community.XAE.TcComSource.AdsGamepad -y
```

### Coming from TC_XboxController?

This is the same project under a new name. The TC_XboxController repository has been retired and archived, and it will receive no further updates. All releases, fixes, and documentation now live here.
