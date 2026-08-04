# ADS Gamepad Service

ADS Gamepad Service brings game controllers into TwinCAT projects as plain process data. ADS is the messaging protocol every TwinCAT system speaks; a read is a small addressed request for a block of bytes. A system service reads the controllers and answers those requests from the PLC, so the controller state arrives in your program every cycle without any driver work on the PLC side. The typical use is jogging axes and movers during commissioning with a device every technician already knows how to hold.

## What it supports

* The PlayStation 5 DualSense controller over USB or Bluetooth, including its battery state, touchpad and motion sensors. The standard Beckhoff RT Linux kernel has no Bluetooth stack, so on a stock Linux system the pad connects over USB; the [Linux installation page](installation/linux.md) shows how to add Bluetooth.
* Xbox controllers through the Microsoft XInput interface, on Windows. Wireless operation works through the official Xbox Wireless Adapter.
* Rumble commands from the PLC back to the controller.

## Choosing a controller

Both families work, and a program written against the library or the TcCOM module behaves the same whichever pad fills a slot. When you get to pick, pick the DualSense. It reports a real battery percentage and charging state, it serves extra data such as the touchpad and motion sensors, it needs nothing beyond its USB cable, and it reconnects on its own after cable pulls and reboots. It is also the only controller the Linux side supports.

Xbox controllers earn their place where the Xbox form factor or the Adaptive Controller family is the requirement. Plan for their habits: wireless operation needs the official Xbox Wireless Adapter, a pad goes to sleep and wants its Xbox button pressed after a machine reboot, and the battery information in the classic status word is not reliable. A program using a wireless pad of any kind should watch the connected state every cycle; the service zeroes all inputs the moment a pad drops, and the program is the right place to decide what the machine does about it.

Three ways to consume the data:

* The **AdsGamepad PLC library**: one function block per controller with buttons, sticks, triggers and battery state as properties.
* The **TcCOM module**: controller data as linkable process data with no PLC code involved, installed compiled and signed, ready to add to a project.
* **Raw ADS reads** against the documented wire contract, from any ADS client you like.

## Where to start

Install the service on the machine that has the controllers attached: on Windows through the [TwinCAT Package Manager](installation/windows.md), on a Beckhoff RT Linux controller with the [Debian package](installation/linux.md). The service ships configured for one DualSense; the [service page](service.md) documents the configuration, including how to switch the slots to Xbox controllers. Then add the PLC library to your project and read your first controller with a few lines, shown on the [PLC library page](plc-library.md). The [applications page](applications.md) collects patterns for jogging axes, driving movers and hand held control, and the safety thinking that belongs with them.

This project is the continuation of the TC_XboxController project under a new home and a new name. If you come from the old service or the XboxControllerUtilities library, the [migration page](migration.md) walks you through the change.
