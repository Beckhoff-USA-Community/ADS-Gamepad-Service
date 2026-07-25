# ADS Gamepad Service

ADS Gamepad Service connects Xbox and PlayStation gamepads attached to PCs, IPCs, and CX devices, then hosts the input data over ADS for the PLC to access. It runs as a system service on TwinCAT Windows systems and includes a PLC library for reading gamepad data in your project.

### Installing with the TwinCAT Package Manager

The service ships as TwinCAT packages on the GitHub package feed of this repository. GitHub requires a login for its package feeds, even for public packages, so you need a GitHub account and a personal access token. Create a token of the classic type with the read:packages scope under Developer settings in your GitHub account settings.

Add the feed from the command line on the target system, then install the workload. Enter the token when prompted for a password.

```powershell
tcpkg source add -n AdsGamepad -s https://nuget.pkg.github.com/Beckhoff-USA-Community/index.json -u <your GitHub user name> --take 100
tcpkg config unset -n VerifySignatures
tcpkg install Beckhoff-USA-Community.AdsGamepad.XAR -y
```

Two settings in these commands are required, not optional:

* `--take 100` limits search requests to 100 results per page. GitHub rejects anything larger, while the TwinCAT Package Manager asks for 500 by default. Without this option, adding the feed fails with the error "Failed to retrieve metadata from source".
* `tcpkg config unset -n VerifySignatures` allows packages that are not signed by Beckhoff. Community packages carry no Beckhoff signature, so installs fail while signature verification is on.

If adding the feed still fails, confirm the token is the classic type (it starts with ghp_), that it has the read:packages scope, and that it has not expired.

### Coming from TC_XboxController?

This is the same project under a new name. The TC_XboxController repository has been retired and archived, and it will receive no further updates. All releases, fixes, and documentation now live here.
