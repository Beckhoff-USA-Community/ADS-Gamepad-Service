# The PLC Library

AdsGamepad is the TwinCAT 3 PLC library for the service. It ships with the engineering workload and lands in the XAE library repository, ready to add as a reference under the name AdsGamepad from Beckhoff Community. The library covers plain controller I/O; how a stick value drives an axis or a mover is application code, which keeps the library small and your motion logic explicit.

## First program

```
PROGRAM MAIN
VAR
    // An empty NetID means the service on this machine, controller slot 1
    Gamepad  : FB_Gamepad_Controller('', 1);
    fStickX  : LREAL;
    fStickY  : LREAL;
END_VAR
```

```
Gamepad.Cycle();

fStickX := Gamepad.P_Left_Joystick.fX;
fStickY := Gamepad.P_Left_Joystick.fY;

IF Gamepad.P_Buttons.bA_Button THEN
    // jog, latch, whatever the application needs
END_IF
```

Call Cycle() once per PLC cycle. It issues the ADS read for the controller block and, on the first cycles, a one time handshake read against the service info block. Everything else is properties on the function block.

## What the block offers

* P_Left_Joystick and P_Right_Joystick with fX and fY in the range -100 to 100. A stick inside the deadzone reads exactly zero.
* P_Left_Trigger and P_Right_Trigger in the range 0 to 100.
* P_Buttons with one BOOL per button: the four face buttons, DPad directions, shoulder buttons, stick clicks, Start and Back.
* P_Status with the connected state and battery information.
* SetRumble() to drive the two rumble motors, values 0 to 100 per motor.
* ReadExtended() as an optional second call per cycle for the extended data of a PlayStation pad: P_Ext_Buttons, P_Touchpad, P_Motion and P_Sequence. The block behind it is described on the [versions page](versions.md). Programs that never call it behave exactly as before.
* P_Handshake_State and P_Service_Info with the result of the version handshake, described on the [versions page](versions.md).

With a DualSense in the slot the values arrive in the same ranges and on the same properties: Cross, Circle, Square and Triangle map onto A, B, X and Y, Create onto Back and Options onto Start.

## Failure behavior

On a failed ADS read the block zeroes its whole input image, and the property getters return zeros while the controller is not connected. A lost service, a pulled cable or a crashed machine therefore reads as a released controller, never as stale commands. Check P_Status to tell a released controller from a missing one.
