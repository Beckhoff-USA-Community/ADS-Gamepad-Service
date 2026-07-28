# Migration Guide

This page is for users of the XboxControllerUtilities PLC library. The library continues as AdsGamepad, starting at version 2.0.0. TwinCAT identifies a library by its title and company, so the renamed library is a new library as far as TwinCAT and its library repository are concerned. The company stays Beckhoff Community. The final XboxControllerUtilities release, version 1.5, stays available and unchanged, so existing machines can keep running it without any action.

## Why the helper blocks were removed

The old library shipped function blocks that jogged NC axes and XPlanar movers directly from controller input. Version 2.0.0 drops them. The library now does plain gamepad input and output only: it reads buttons, sticks, triggers, and status from the service, and it sends rumble commands back. What a stick or trigger value means for your machine is an application decision, and the application is where the machine knowledge lives. The section below shows how little code the same jog takes in your own project.

## Renamed objects

Every type lost the Xbox prefix, because the service also supports PlayStation controllers. The mapping is mechanical:

| Old name | New name |
| --- | --- |
| FB_Xbox_Controller | FB_Gamepad_Controller |
| ST_Xbox_Controller_ADS_Inputs | ST_Gamepad_ADS_Inputs |
| ST_Xbox_Controller_Rumble | ST_Gamepad_Rumble |
| ST_Xbox_Class_Command_Status | ST_Gamepad_Command_Status |
| ST_Xbox_Controller_Buttons | ST_Gamepad_Buttons |
| ST_Xbox_Controller_DPad | ST_Gamepad_DPad |
| ST_Xbox_Controller_Status | ST_Gamepad_Status |
| ST_Xbox_Controller_Joystick | ST_Gamepad_Joystick |
| ST_Xbox_Controller_Buttons_Bits | ST_Gamepad_Buttons_Bits |
| ST_Xbox_Controller_State_Bits | ST_Gamepad_State_Bits |
| E_Xbox_Controller_Battery_Types | E_Gamepad_Battery_Types |
| E_Xbox_Controller_Battery_Levels | E_Gamepad_Battery_Levels |

## Names that did not change

The members of the controller function block kept their names, so after the type rename your calling code compiles as before:

* Methods: Cycle, SetRumble
* FB_init inputs: NetID, iControllerNumber
* Properties: P_Buttons, P_Left_Joystick, P_Right_Joystick, P_Left_Trigger, P_Right_Trigger, P_SetRumble, P_Status

## Removed objects

These objects are gone and have no replacement in the library:

* The six NC jog function blocks: FB_Xbox_Single_Button_NC, FB_Xbox_Dual_Button_NC, FB_Xbox_1D_Joystick_NC, FB_Xbox_2D_Joystick_NC, FB_Xbox_Single_Trigger_NC, FB_Xbox_Dual_Trigger_NC
* Their five option types: ST_Xbox_Button_NC_Options, ST_Xbox_1D_Joystick_NC_Options, ST_Xbox_2D_Joystick_NC_Options, ST_Xbox_Trigger_NC_Options, ST_Xbox_NC_Halt_Dynamics
* FB_Xbox_2D_Joystick_XPlanar and its two types: ST_Xbox_2D_Joystick_XPlanar_Options, E_Xbox_XPlanar_JogMode

## Jogging an axis from application code

The same behavior the NC helpers provided fits in a few lines with MC_Jog from Tc2_MC2. This is an example sketch, adapt the deadband, velocity, and dynamics to your machine. Stick values run from -100 to 100.

```iecst
VAR
    fbPad  : FB_Gamepad_Controller(NetID := '', iControllerNumber := 1);
    fbJog  : MC_Jog;
    Axis   : AXIS_REF;
    fStick : REAL;
END_VAR

fbPad.Cycle();
fStick := fbPad.P_Left_Joystick.fX;

fbJog(Axis         := Axis,
      JogForward   := fStick > 20,
      JogBackwards := fStick < -20,
      Mode         := MC_JOGMODE_CONTINOUS,
      Velocity     := ABS(fStick));
```

## New in 2.0.0: the version handshake

The function block now reads a 32 byte info block from the service once at startup, from IndexGroup 16#F000. Two new properties expose the result:

* P_Handshake_State returns E_Gamepad_Handshake_State with the values NotStarted, Busy, Compatible, Unsupported, and Mismatch.
* P_Service_Info returns ST_Gamepad_Service_Info, which holds the wire contract version numbers, the service version numbers, and capability bits.

Services older than 2.1.0 do not serve the info block, so against them the handshake reports Unsupported and everything else keeps working. Mismatch means the service speaks a newer contract than the library expects; data exchange still runs, and you should update the library. The wire format of the controller blocks themselves is byte for byte unchanged, so old and new pairs of library and service interoperate.

## Choosing between the PLC library and the TcCOM module

The repository also offers a TcCOM module, described in its own readme, tccom/README.md in the repository and TcComModule.md in the installed documentation set. The PLC library is the simple path: it works everywhere TwinCAT PLC runs, and one function block serves one controller. The TcCOM module suits projects that want gamepad data as linkable process data without any PLC code and are comfortable building C++.
