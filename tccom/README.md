# Gamepad TcCOM Module

This directory holds a TwinCAT C++ module (TcCOM) that reads gamepad data from the ADS Gamepad Service and exposes it as process data. You add an instance to your TwinCAT project, assign it to a task, and link its variables like any other process data, with no PLC code involved. The module polls the service over ADS (AMS port 25733) once per task cycle and zeroes all outputs when the service stops answering.

## Source only

The module is distributed as source only. You build and sign it yourself. TwinCAT loads a C++ module, a TMX file, on Windows and TwinCAT/BSD only when it is signed with a certificate the target trusts, and there is no way to sign someone else's finished binary. Building from source is therefore not a workaround, it is the only path.

## Requirements

* TwinCAT XAE 4026.21 or newer with the TC1300 TwinCAT C/C++ workload installed.
* Visual Studio with the Desktop development with C++ workload plus the MSBuild support for LLVM (clang cl) individual component.
* A TwinCAT user certificate for signing. Either an official Beckhoff OEM certificate (order number TC0008, free of charge, issued to Beckhoff customers) or a self created test signing certificate with the purpose Sign TwinCAT C++ executable. Test signing also requires test mode on the target: run bcdedit /set testsigning yes on Windows and reboot.
* The TC1300 runtime license on the target that runs the module.

## Building

* Open Gamepad_TcCOM.slnx from the project folder, tccom/Gamepad_TcCOM in this repository. An installed copy from the source package lives under C:\Program Files\Beckhoff USA Community\ADS Gamepad\TcCOM and must be copied to a writable folder first.
* Run the TwinCAT TMC Code Generator once on the ADS_Gamepad project.
* Build for the target platform.

## Using the module

* Add a task to your TwinCAT project.
* Add an instance of the Gamepad module class.
* Assign the task under Context. Afterwards check the Interface Pointer tab: CyclicCaller must hold the object id of that task. XAE normally fills this in when you set the context, and the module refuses to reach OP while it is 0.
* Check the parameters. The defaults connect to controller 1 on the local gamepad service, so on a single machine setup you change nothing.
* Link the output variables, and link the rumble inputs if you want force feedback.

## Parameters

| Name | Default | Meaning | Online adjustable |
|---|---|---|---|
| ControllerNumber | 1 | Controller slot on the gamepad service, 1 to 4. One module instance serves one controller. | no |
| ServiceAmsNetId | 127.0.0.1.1.1 | AMS Net ID of the machine that runs the gamepad service. 127.0.0.1.1.1 and all zeros both mean the local system. The module then resolves the real local address automatically, so the default just works. | no |
| TimeoutCycles | 1000 | Task cycles without an ADS answer before the outputs are zeroed and the read is retried. | no |
| StickDeadzonePercent | 10.0 | Stick deadzone in percent of full deflection, applied range 0 to 90. Deflection below the deadzone reads as zero, above it the value rises smoothly from zero. | no |
| StickCurve | 1.0 | Exponent on the stick deflection after the deadzone, applied range 0.25 to 8. A value of 1.0 is linear, 2.0 gives fine control near the center for jogging. | no |
| TriggerDeadzonePercent | 0.0 | Trigger deadzone in percent of full travel, applied range 0 to 90. | no |
| TriggerCurve | 1.0 | Exponent on the trigger travel after the deadzone, applied range 0.25 to 8. | no |
| OnlineStickDeadzonePercent | seeded | Live copy of StickDeadzonePercent. | yes |
| OnlineStickCurve | seeded | Live copy of StickCurve. | yes |
| OnlineTriggerDeadzonePercent | seeded | Live copy of TriggerDeadzonePercent. | yes |
| OnlineTriggerCurve | seeded | Live copy of TriggerCurve. | yes |
| bReadExtended | FALSE | When TRUE the module also reads the extended data block. Needs service 2.5.0 or newer. | no |

The tuning values come in two sets. The init set lives on the Parameter (Init) tab, is stored in the project, and survives every activation. When the module starts it copies the init set into the online set, and the shaping math reads only the online set. During commissioning you adjust the Online values on the Parameter (Online) tab and feel the result on the next task cycle, no restart needed. Online changes reset to the init values at the next activation, so when a value feels right, copy it into its init twin to make it permanent.

## Outputs

The module decodes the raw service data into plain variables that link directly to PLC or motion logic:

* bConnected is TRUE while the gamepad is connected to the service.
* stButtons holds one BOOL per button: DPad up, down, left and right, Start, Back, both stick clicks, both shoulder buttons, A, B, X and Y.
* fLeftStickX, fLeftStickY, fRightStickX and fRightStickY are the stick axes with deadzone and curve applied, range -100 to 100.
* fLeftTrigger and fRightTrigger are the trigger travels with deadzone and curve applied, range 0 to 100.
* nButtonsRaw and nStatesRaw carry the unmodified wire words for power users. nStatesRaw holds the battery info in bits 1 to 9.
* eCommState, nErrorCount and nDataAgeCycles are communication diagnostics.
* stServiceInfo shows contract and service versions, served by gamepad services from release 2.1.0 on.

On any communication error the module zeroes all decoded outputs, bConnected included, and reports the reason in eCommState.

## Extended data

With bReadExtended set the module also reads the extended block that gamepad services serve from release 2.5.0 on. The reads alternate with the gamepad read, so each block updates at half the task rate. Plan for that before enabling it on a fast task: the gamepad data then refreshes half as often as with the setting off, and nDataAgeCycles shows it. A read that gets no answer at all fails safe exactly like a gamepad read timeout, so enabling the extended block does not lengthen the fail safe reaction time. Against an older service the module notices the missing capability during its startup handshake and skips the extended reads entirely.

The extended values arrive in their own output area, ExtOutputs:

* bExtDataPresent is TRUE while the service supplies extended data for this slot. A slot with an Xbox controller reads all zero with the flag clear.
* bPS, bMute and bTouchpadClick are the PlayStation buttons beyond the classic surface.
* nGyroX through nAccelZ are the raw motion sensor values. The units are the sensor's own; interpreting or filtering them is application code.
* stTouch0 and stTouch1 are the two touchpad contacts with an active flag, a contact counter and the position on the pad.
* nSequence is the report counter of the pad, widened by the service. A value that keeps moving proves the input stream is alive, so it makes a good watchdog.
* nExtDataAgeCycles counts task cycles since the last successful extended read, separate from the gamepad age.

An error on the extended read zeroes only the extended values; the gamepad exchange keeps running untouched.

## Beckhoff RT Linux

On Beckhoff RT Linux targets the build result is a TME file instead of a TMX file. The operating system loads it without signing.
