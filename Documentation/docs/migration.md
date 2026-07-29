# Migration

This project continues the TC_XboxController project. The wire format never changed, so old and new parts interoperate, but the product names did. This page covers moving from the old service and the old library. The complete type name mapping lives in MIGRATION.md in the repository, which also installs with the documentation package.

## From the TwinCAT Xbox Controller Service

The old application installed per user under C:\Program Files\Beckhoff Community\TwinCAT Xbox Controller Service and was started by hand rather than running as a service. Remove it, or at least make sure it is not running, before installing this service: both register ADS port 25733 with the router, and the second one to start fails. Uninstall it from the Windows installed applications list. If you ever registered it as a service yourself, note that its service name shipped with a spelling mistake, Contoller instead of Controller, so search for both spellings when cleaning up.

The new service reads the same controllers and answers the same reads, so existing PLC programs keep working without changes the moment the new service runs.

## From the XboxControllerUtilities library

AdsGamepad 2.0.0 is the successor of the XboxControllerUtilities library. Because a renamed library is a new identity as far as TwinCAT is concerned, the move is a deliberate step in your project:

1. Install the AdsGamepad library, which the engineering workload does for you.
2. Replace the library reference in your PLC project.
3. Rename the types: the Xbox prefix became Gamepad, for example FB_Xbox_Controller is now FB_Gamepad_Controller and ST_Xbox_Controller_Buttons is now ST_Gamepad_Buttons. The byte layouts are identical.

The NC jog and XPlanar helper blocks of the old library were removed on purpose. They decided too much about how a stick maps to motion; that decision belongs in the application. The old library stays available unchanged as a final release named XboxControllerUtilities 1.5 on the GitHub releases page of the repository, for machines that should keep running exactly as they are.
