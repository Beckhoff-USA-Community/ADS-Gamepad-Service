# Roadmap

ADS Gamepad Service is the continuation of the TC_XboxController project under a new home and a new name. The code arrived here as a direct migration of the last working state of that project, with the old product names removed and the folder layout reorganized. This page lists the current state of the repository and the work planned to modernize it. The order below is the intended order of delivery, and details may shift as testing happens on real hardware.

## Where things stand today

* The Windows application lives in src/AdsGamepadService and targets .NET 10, the current long term support release, with the Beckhoff ADS packages on the 7.0 line.
* The application registers itself as an ADS server on port 25733. The PLC polls that server every cycle to read controller data and to write rumble commands. This wire format is treated as frozen. Any change to it will be versioned so that existing PLC programs keep working or fail loudly, never silently. That promise has been used once already: the PlayStation extra data arrived as a separate versioned extension block, and the original 32 byte block never changed shape.
* Xbox controllers are read directly from C# through the Microsoft XInput API. The old C++ helper library is gone, so the project needs only the .NET SDK to build. Wireless Xbox controllers connect through the official Xbox Wireless Adapter and appear as normal XInput controllers, so the service needs no change; pairing Xbox pads with generic Bluetooth adapters proved unreliable in testing and is not a supported path.
* A second input backend reads a PlayStation 5 DualSense controller over raw HID, selected per slot in the configuration file. On Windows the pad connects over USB or Bluetooth with the same slot setting, and the cable wins when both are present. On Linux it connects over USB. The backend has been verified with a real controller on Beckhoff hardware, on Windows and on Linux.
* A test suite under tests locks the exact numeric behavior of the controller math, including the deadzone handling and axis mapping the old helper shipped with, so a future change cannot silently alter what the PLC receives.
* The PLC library sources are under plc. The library is called AdsGamepad since version 2.0.0 and covers plain controller I/O: buttons, sticks, triggers, battery state and rumble. Since 2.1.0 an optional second call per cycle reads the extended PlayStation data, and 2.2.1 adds the real battery percent and charging state. The helper blocks of the old library are gone, and MIGRATION.md at the repository root explains the move from XboxControllerUtilities, whose final release stays available under the old name.
* A TwinCAT C++ module under tccom exposes a controller as plain process data. You add an instance, assign a task and link the variables, with no PLC code involved. It ships compiled and signed in the engineering workload, so it is ready to add to a project right after the install. The source stays in the repository for anyone who prefers to build and sign it themselves, and the README under tccom covers the build.
* The content under Documentation is the source of the documentation site, built with Zensical and covering installation on Windows and Linux, service configuration, the PLC library, the TcCOM module, migration from the old project, and the version and wire contract reference. The engineering workload installs the same pages as offline documentation.

## Phase 1: Move to current .NET (complete)

* The service was retargeted from .NET 6 to .NET 10 and the Beckhoff ADS packages moved from 6.0 to the 7.0 line.
* The C++ XInput helper was replaced with direct calls from C# and the native project was deleted. The exact numeric behavior of the old helper was captured in tests first, because details such as deadzone handling and axis mapping are part of how existing machines feel to operate.
* Hosting, shutdown, logging, and error handling were brought up to current .NET patterns. The service now logs controller connect and disconnect events and restarts cleanly under service recovery if it fails.

## Phase 2: Run as a Windows service (complete)

* The application now runs as a real Windows service that starts with the machine and needs no user session. Install and uninstall scripts live under deploy/windows, and upgrades keep your edited configuration.
* Every value that was fixed in code moved to appsettings.json next to the service executable, documented in CONFIGURATION.md. Invalid settings stop the service with a clear explanation in the Windows Event Log instead of silently running with wrong values.
* The open question about reading controllers from the isolated service session was settled by testing on real Beckhoff hardware. XInput sees Xbox controllers from a system service, and PlayStation controllers stream their reports over plain HID in the same context, so the service needs no helper process. The same test showed that the Xbox Adaptive Joystick appears as a plain HID device rather than an XInput controller, which the input backend work in a later phase will pick up.

## Phase 3: Package for the TwinCAT Package Manager (complete)

* The service ships as two TwinCAT packages built under packaging: the service package that installs the Windows service, and a workload package that groups it in the TwinCAT Package Manager user interface, following the same conventions as other Beckhoff USA Community projects. Upgrades keep an edited appsettings.json, including when the package takes over an installation made with the manual scripts.
* A GitHub Actions workflow builds and tests the project on every push to the main branch and on every pull request, and packs both packages. Pushing a version tag publishes them to the GitHub package feed after checking that the tag, the project file, and both package manifests agree on the version number.
* The GitHub package feed works as a TwinCAT Package Manager source with a personal access token, and installs from it were verified on a real controller. Two limitations are inherent to GitHub: its feed requires a login even for public packages, and its search endpoint accepts at most 100 results per page while the package manager requests 500 by default. The README documents the working setup, which adds the feed with a token and a page size of 100. With those settings, listing, workload grouping, and installs all work, though GitHub reports package ids in place of display titles so listings look plain.

## Phase 4: Deploy to a test controller (complete)

* The runtime workload was installed on a real Beckhoff controller straight from the GitHub package feed, upgrading the running service in place while keeping its configuration.
* Recovery behavior was verified on hardware. The service restarts on its own through the Windows service recovery settings, comes back about two minutes after a reboot thanks to the delayed start, and the PLC library and the TcCOM module hold their fail safe zero state while it is away and reconnect without intervention.
* Disconnect and reconnect were exercised with all three supported controllers, including rumble and the PlayStation specific buttons. A wired PlayStation controller reconnects on its own after a cable pull. Wired Xbox controllers may need a nudge after a machine reboot; the service page of the documentation describes it.

## Phase 5: PLC library cleanup (complete)

* The library was cut down to plain controller I/O and rebuilt under the new name AdsGamepad. The NC jog and XPlanar helper blocks are gone. How a stick value drives an axis is application code now, and the migration guide shows an example of the change.
* A renamed library is a new identity as far as TwinCAT is concerned, so the last XboxControllerUtilities release stays available under the old name and MIGRATION.md describes the move.
* The version handshake landed on both sides. The library reads an info block from the service once at startup and reports a clear state for a mismatched pair instead of misreading data. The service side of the handshake ships with release 2.1.0.
* The phase also produced the TwinCAT C++ module under tccom, which reads the same wire format from a task cycle without any PLC involvement.

## Phase 5.5: Installation through the TwinCAT Package Manager (complete)

* The project output is regrouped into two workloads. The engineering workload installs the documentation, the TcCOM module source and the PLC library, and puts the library into the XAE library repository so it is ready to reference after the install. The runtime workload installs the Windows service. Both show up as one card with an engineering and a runtime row in the package manager.
* All components install under one product directory below C:\Program Files\Beckhoff USA Community, the way normal applications do. Upgrades keep an edited configuration, even when they move an installation from the old default location.
* Service release 2.1.0 rides this restructuring. One release carries the version handshake and the new package layout.

## Phase 6: PlayStation support and controller testing (complete)

* PlayStation support: the service reads a wired PlayStation 5 DualSense controller over raw HID as a second input backend. The slot that uses it is chosen in the configuration file, and a configuration without that setting behaves exactly as before. The report layout was verified byte by byte against a real controller before the decoder was written.
* Structured testing with physical Xbox and PlayStation controllers ran on real hardware and covered connect, disconnect, battery reporting, rumble, and input accuracy, including a soak with all three supported controllers live at once.

## Phase 7: Linux support (complete)

* The service runs on Beckhoff RT Linux as a systemd service. The simplest install is the Debian package, which every release build produces and apt installs on the target. The linux directory also documents the manual path: publish, copy the directory over, and run the install script. Upgrades keep an edited configuration either way, and the ADS wire contract is unchanged, so the PLC library and the TcCOM module work exactly as on Windows.
* Linux is DualSense over USB only. The service reads the pad over the hidraw interface with the same report decoding as on Windows. Xbox controllers need a kernel driver and Bluetooth of any kind needs the kernel Bluetooth stack; the Beckhoff kernel includes neither, so on Linux plan on a wired DualSense.
* The install runs the service under its own system account with device access granted through a udev rule, and registration with the TwinCAT router through membership in its access group.

## Extended controller data (complete)

* The data a DualSense offers beyond a classic gamepad ships as a separate versioned 96 byte block: the PS, Mute and touchpad click buttons, both touchpad contacts, the raw motion sensors and a report counter. The original 32 byte block is untouched, so existing programs never notice the addition. Service 2.5.0 serves the block, PLC library 2.1.0 reads it through an optional second call per cycle, and the versions page of the documentation holds the byte layout.
* Battery detail followed with service 2.6.0 and library 2.2.1: the real charge percent and charging state of the pad, verified against a complete charge cycle. The classic battery bits in the controller block are unchanged.
* Service 2.7.0 adds DualSense over Bluetooth on Windows. Pair the pad once through the Windows Bluetooth settings and the same slot setting covers both transports; the cable wins when both are present. The wire contract is unchanged.

## Later: Bluetooth on Linux

* On Linux the DualSense stays on USB for now. Bluetooth needs the kernel Bluetooth stack, which the Beckhoff kernel does not include, so this waits on a kernel change rather than on service code; the Bluetooth handling in the service is already shared between the platforms.

## TwinCAT/BSD

Support for TwinCAT/BSD was considered and set aside; Windows and Beckhoff RT Linux are the supported platforms. The wire format is documented well enough that a native service could be built for TwinCAT/BSD on the open source ADS library Beckhoff publishes, if demand appears.
