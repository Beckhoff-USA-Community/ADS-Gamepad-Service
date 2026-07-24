# ADS Gamepad Service Workload

This workload installs the ADS Gamepad Service on a TwinCAT runtime system through its dependency packages. The service reads Xbox controllers through XInput and serves their state to the PLC over ADS on port 25733, where the companion PLC library picks it up. PlayStation DualSense support is planned for a later release.

Project home, documentation, and the PLC library: https://github.com/Beckhoff-USA-Community/ADS-Gamepad-Service
