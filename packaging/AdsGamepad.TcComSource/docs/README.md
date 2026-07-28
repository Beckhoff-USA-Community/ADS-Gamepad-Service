# ADS Gamepad TcCOM Module Source

This package installs the source of the Gamepad TcCOM module under C:\Program Files\Beckhoff USA Community\ADS Gamepad\TcCOM. The module is a TwinCAT C++ module that reads a controller from the ADS Gamepad Service and exposes it as process data, with no PLC code involved.

TwinCAT only loads C++ modules that are signed with a certificate the target trusts, and there is no way to sign a finished binary from someone else. The module therefore ships as source, and you build and sign it with your own TwinCAT user certificate. Copy the project to a writable folder before building, since Program Files is not writable for a normal build. The readme inside the project covers the requirements and the build steps.

Project home and documentation: https://github.com/Beckhoff-USA-Community/ADS-Gamepad-Service
