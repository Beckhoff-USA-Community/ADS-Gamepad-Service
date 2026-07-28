# AdsGamepad PLC Library

AdsGamepad is the TwinCAT 3 PLC library for the ADS Gamepad Service. FB_Gamepad_Controller reads the controller state over ADS every PLC cycle and exposes buttons, sticks, triggers, battery state and rumble through properties. On any read error the block zeroes its inputs, so a lost connection never leaves stale commands in your program.

The package installs the library into the XAE library repository, ready to add as a reference. A copy of the library file lands under C:\Program Files\Beckhoff USA Community\ADS Gamepad\Library for setups that move it around by hand.

Version 2.0.0 renames the library from XboxControllerUtilities and removes the NC and XPlanar helper blocks. MIGRATION.md in the project repository describes the move.

Project home and documentation: https://github.com/Beckhoff-USA-Community/ADS-Gamepad-Service
