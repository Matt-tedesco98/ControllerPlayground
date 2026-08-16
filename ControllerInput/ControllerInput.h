#pragma once

#ifdef CONTROLLERINPUT_EXPORTS
#define CONTROLLERINPUT_API __declspec(dllexport)
#else
#define CONTROLLERINPUT_API __declspec(dllimport)
#endif

struct ControllerState {
	unsigned int buttons; // Bitmask for button states
	unsigned int systemButtons; // Bitmask for system button states

	float leftTrigger; // Value for the left trigger
	float rightTrigger; // Value for the right trigger

	float leftStickX; // X-axis value for the left stick
	float leftStickY; // Y-axis value for the left stick

	float rightStickX; // X-axis value for the right stick
	float rightStickY; // Y-axis value for the right stick

};

struct ControllerDeviceInfo {
	unsigned short vendorId; // Vendor ID of the controller
	unsigned short productId; // Product ID of the controller
};

extern "C"
{
    CONTROLLERINPUT_API bool ControllerInput_Initialize();
	CONTROLLERINPUT_API bool ControllerInput_GetState(ControllerState* state);
	CONTROLLERINPUT_API bool ControllerInput_GetDeviceInfo(ControllerDeviceInfo* info);
}