# ADS Gamepad Service

ADS Gamepad Service brings game controllers into TwinCAT projects as plain process data. ADS is the messaging protocol every TwinCAT system speaks; a read is a small addressed request for a block of bytes. A system service reads the controllers and answers those requests from the PLC, so the controller state arrives in your program every cycle without any driver work on the PLC side. The typical use is jogging axes and movers during commissioning with a device every technician already knows how to hold.

## What it supports

* Xbox controllers through the Microsoft XInput interface, on Windows.
* The PlayStation 5 DualSense controller over USB, on Windows and on Beckhoff RT Linux.
* Rumble commands from the PLC back to the controller.

Three ways to consume the data:

* The **AdsGamepad PLC library**: one function block per controller with buttons, sticks, triggers and battery state as properties.
* The **TcCOM module**: controller data as linkable process data with no PLC code involved, built from source in your own environment.
* **Raw ADS reads** against the documented wire contract, from any ADS client you like.

## Where to start

Install the service on the machine that has the controllers attached: on Windows through the [TwinCAT Package Manager](installation/windows.md), on a Beckhoff RT Linux controller with the [install script](installation/linux.md). Then add the PLC library to your project and read your first controller with a few lines, shown on the [PLC library page](plc-library.md).

This project is the continuation of the TC_XboxController project under a new home and a new name. If you come from the old service or the XboxControllerUtilities library, the [migration page](migration.md) walks you through the change.
