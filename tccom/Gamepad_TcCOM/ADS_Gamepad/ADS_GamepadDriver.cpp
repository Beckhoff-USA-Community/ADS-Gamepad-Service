///////////////////////////////////////////////////////////////////////////////
// ADS_GamepadDriver.cpp
#include "TcPch.h"
#pragma hdrstop

#include "ADS_GamepadDriver.h"
#include "ADS_GamepadClassFactory.h"

DECLARE_GENERIC_DEVICE(ADS_GAMEPADDRV)

IOSTATUS CADS_GamepadDriver::OnLoad( )
{
	TRACE(_T("CObjClassFactory::OnLoad()\n") );
	m_pObjClassFactory = new CADS_GamepadClassFactory();

	return IOSTATUS_SUCCESS;
}

VOID CADS_GamepadDriver::OnUnLoad( )
{
	delete m_pObjClassFactory;
}

unsigned long _cdecl CADS_GamepadDriver::ADS_GAMEPADDRV_GetVersion( )
{
	return( (ADS_GAMEPADDRV_Major << 8) | ADS_GAMEPADDRV_Minor );
}

