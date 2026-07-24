# Roadmap

ADS Gamepad Service is the continuation of the TC_XboxController project under a new home and a new name. The code arrived here as a direct migration of the last working state of that project, with the old product names removed and the folder layout reorganized. This page lists the current state of the repository and the work planned to modernize it. The order below is the intended order of delivery, and details may shift as testing happens on real hardware.

## Where things stand today

* The Windows application lives in src/AdsGamepadService and targets .NET 10, the current long term support release, with the Beckhoff ADS packages on the 7.0 line.
* The application registers itself as an ADS server on port 25733. The PLC polls that server every cycle to read controller data and to write rumble commands. This wire format is treated as frozen. Any change to it will be versioned so that existing PLC programs keep working or fail loudly, never silently.
* Xbox controllers are read directly from C# through the Microsoft XInput API. The old C++ helper library is gone, so the project needs only the .NET SDK to build.
* A test suite under tests locks the exact numeric behavior of the controller math, including the deadzone handling and axis mapping the old helper shipped with, so a future change cannot silently alter what the PLC receives.
* The PLC library sources are under plc. The library keeps its original name, XboxControllerUtilities, so that projects built against earlier releases keep resolving. A carefully managed rename is planned in a later phase.
* The content under Documentation is the old documentation site. It still refers to the old project names and will be rewritten as the phases below land. Until then, treat it as historical reference.

## Phase 1: Move to current .NET (complete)

* The service was retargeted from .NET 6 to .NET 10 and the Beckhoff ADS packages moved from 6.0 to the 7.0 line.
* The C++ XInput helper was replaced with direct calls from C# and the native project was deleted. The exact numeric behavior of the old helper was captured in tests first, because details such as deadzone handling and axis mapping are part of how existing machines feel to operate.
* Hosting, shutdown, logging, and error handling were brought up to current .NET patterns. The service now logs controller connect and disconnect events and restarts cleanly under service recovery if it fails.

## Phase 2: Run as a Windows service

* Today the application must be started by a logged in user and left running. The goal of this phase is a real Windows service that starts with the machine and needs no user session.
* A configuration file next to the service will hold every value that is currently fixed in code, such as the ADS port, the number of controllers, and log levels.
* One technical question gets settled first. Windows runs services in an isolated session with no access to the desktop, and it is not documented whether XInput works from there. Reading a PlayStation controller directly over HID from a service is a proven pattern, but the Xbox path may need a small helper process that starts at user logon and forwards controller data to the service. A short experiment on real hardware will decide the design before the rest of the phase is built.

## Phase 3: Package for the TwinCAT Package Manager

* Build the service as a package that the TwinCAT Package Manager can install with a single command, using the same NuGet based format Beckhoff uses.
* Add a GitHub Actions workflow so every push builds the package automatically.
* Decide where the package feed lives. GitHub can host NuGet packages, but its feed requires a token even for public packages, which is unfriendly for consumers. The candidates are the GitHub feed with a documented token setup, a static feed served from GitHub Pages, or nuget.org. This choice needs a round of testing against a real TwinCAT Package Manager install.

## Phase 4: Deploy to a test controller

* Install the package on a real Beckhoff controller over SSH and verify service startup, ADS traffic, and recovery behavior after reboots and cable pulls.

## Phase 5: PLC library cleanup

* Use the TwinCAT Automation Interface to rebuild the PLC library project cleanly: regenerate the project information, retire leftovers from old TwinCAT versions, and fix small inconsistencies in file naming.
* Decide and execute the library naming strategy. A renamed library is a new identity as far as TwinCAT is concerned, so this ships together with a migration guide and a final frozen release under the old name.
* Add a version handshake between the service and the library so a mismatched pair reports a clear error instead of misreading data.

## Phase 6: Controller testing

* Structured testing with physical Xbox and PlayStation 5 controllers on real hardware, covering connect, disconnect, battery reporting, rumble, and input accuracy.
* PlayStation support lands here. The DualSense controller speaks standard HID, and the service gains a second input backend for it.

## Phase 7: Linux support (stretch goal)

* Beckhoff offers a real time Linux runtime on its newer controllers, and the service should run there as a systemd unit.
* The repository will gain a directory with build and install instructions so a user can clone the repository, change into that directory, and run a build followed by an install.
* Controller input on Linux uses the standard evdev and hidraw interfaces, which cover both Xbox and PlayStation pads with mainline kernel drivers.

## TwinCAT/BSD

Support for TwinCAT/BSD was considered and set aside. Windows comes first and the Linux runtime is the stretch goal. The wire format is documented well enough that a native service could be built for TwinCAT/BSD on the open source ADS library Beckhoff publishes, if demand appears.
