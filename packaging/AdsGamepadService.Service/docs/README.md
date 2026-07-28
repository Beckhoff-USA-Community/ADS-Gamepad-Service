# ADS Gamepad Service

This package installs ADS Gamepad Service as a Windows service on a TwinCAT system. The service reads Xbox controllers through XInput and serves their state to the PLC over ADS. It registers ADS port 25733 with the local router and answers the cyclic reads issued by the companion PLC library. PlayStation DualSense support is planned for a later release.

The service installs to C:\Program Files\Beckhoff USA Community\ADS Gamepad\Service and starts automatically with a delayed start so the TwinCAT router is up first. An upgrade from an older package or from the manual install scripts moves an existing installation there. Settings live in appsettings.json next to the executable and survive package upgrades, including the move. See the configuration reference in the project repository for every setting.

Project home, documentation, and the PLC library: https://github.com/Beckhoff-USA-Community/ADS-Gamepad-Service
