///////////////////////////////////////////////////////////////////////////////
// ADS_GamepadDriver.h

#ifndef __ADS_GAMEPADDRIVER_H__
#define __ADS_GAMEPADDRIVER_H__

#if _MSC_VER > 1000
#pragma once
#endif // _MSC_VER > 1000

#include "TcBase.h"

#define ADS_GAMEPADDRV_NAME        "ADS_GAMEPAD"
#define ADS_GAMEPADDRV_Major       1
#define ADS_GAMEPADDRV_Minor       0

#define DEVICE_CLASS CADS_GamepadDriver

#include "ObjDriver.h"

class CADS_GamepadDriver : public CObjDriver
{
public:
	virtual IOSTATUS	OnLoad();
	virtual VOID		OnUnLoad();

	//////////////////////////////////////////////////////
	// VxD-Services exported by this driver
	static unsigned long	_cdecl ADS_GAMEPADDRV_GetVersion();
	//////////////////////////////////////////////////////
	
};

Begin_VxD_Service_Table(ADS_GAMEPADDRV)
	VxD_Service( ADS_GAMEPADDRV_GetVersion )
End_VxD_Service_Table


#endif // ifndef __ADS_GAMEPADDRIVER_H__