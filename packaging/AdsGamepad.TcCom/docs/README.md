# ADS Gamepad TcCOM Module

This package installs the compiled and signed Gamepad TcCOM module into the TwinCAT module repository under C:\ProgramData\Beckhoff\TwinCAT\3.1\Repository. The module is a TwinCAT C++ module that reads a controller from the ADS Gamepad Service and exposes it as process data, with no PLC code involved. After the install it is ready to add to a project: right click TcCOM Objects under System, choose Add New Item, and pick ADS_Gamepad from the Beckhoff Community vendor.

Builds are included for TwinCAT RT x86, TwinCAT RT x64, TwinCAT OS x64 and Beckhoff RT Linux on x64 and ARM, where the build result is a TME file the target loads without signing. The source lives in the project repository on GitHub for anyone who prefers to build and sign the module with their own certificate; the TcCOM page of the documentation covers both paths.

Project home and documentation: https://github.com/Beckhoff-USA-Community/ADS-Gamepad-Service
