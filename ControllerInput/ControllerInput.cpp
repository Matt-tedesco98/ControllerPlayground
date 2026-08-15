#include "pch.h"
#include "ControllerInput.h"
#include <GameInput.h>

#if GAMEINPUT_API_VERSION == 1
using namespace GameInput::v1;
#elif GAMEINPUT_API_VERSION == 2
using namespace GameInput::v2;
#elif GAMEINPUT_API_VERSION == 3
using namespace GameInput::v3;
#endif

static IGameInput* g_gameInput = nullptr;

constexpr uint16_t SonyVendorId = 0x054C;
constexpr uint16_t DualSenseProductId = 0x0CE6;

constexpr uint8_t DualSensePsButton = 12;
constexpr uint8_t DualSenseTouchpadButton = 13;
constexpr uint8_t DualSenseMuteButton = 14;


bool ControllerInput_Initialize() {
	if (g_gameInput != nullptr)
		return true; // Already initialized

	HRESULT result = GameInputCreate(&g_gameInput);

	return SUCCEEDED(result);
}

bool ControllerInput_GetState(ControllerState* State) {
	if (g_gameInput == nullptr || State == nullptr)
		return false; // Not initialized or invalid state pointer

	IGameInputReading* reading = nullptr;

	HRESULT result = g_gameInput->GetCurrentReading(
		GameInputKindGamepad,
		nullptr,
		&reading
	);

	if (FAILED(result) || reading == nullptr)
		return false; // Failed to get reading

	IGameInputDevice* device = nullptr;
	reading->GetDevice(&device);

	if (device == nullptr) {
		reading->Release();
		return false; // Failed to get device
	}

	GameInputDeviceStatus status = device->GetDeviceStatus();

	if ((status & GameInputDeviceConnected) != GameInputDeviceConnected) {
		device->Release();
		reading->Release();
		return false; // Device is not connected
	}

	GameInputGamepadState gamepadState{};

	bool success = reading->GetGamepadState(&gamepadState);

	if (success) {
		State->buttons = static_cast<unsigned int>(gamepadState.buttons);
		State->leftTrigger = gamepadState.leftTrigger;
		State->rightTrigger = gamepadState.rightTrigger;
		State->leftStickX = gamepadState.leftThumbstickX;
		State->leftStickY = gamepadState.leftThumbstickY;
		State->rightStickX = gamepadState.rightThumbstickX;
		State->rightStickY = gamepadState.rightThumbstickY;
	}

	const GameInputDeviceInfo* deviceInfo = nullptr;
	device->GetDeviceInfo(&deviceInfo);

	bool isDualSense =
		deviceInfo != nullptr &&
		deviceInfo->vendorId == SonyVendorId &&
		deviceInfo->productId == DualSenseProductId;

	if (success && isDualSense)
	{
		uint32_t extraButtonCount = 0;

		if (SUCCEEDED(device->GetExtraButtonCount(
			GameInputKindGamepad,
			&extraButtonCount)) &&
			extraButtonCount > 0)
		{
			uint8_t* extraButtonIndexes =
				new uint8_t[extraButtonCount];

			if (SUCCEEDED(device->GetExtraButtonIndexes(
				GameInputKindGamepad,
				extraButtonCount,
				extraButtonIndexes)))
			{
				uint32_t buttonCount =
					reading->GetControllerButtonCount();

				bool* buttonStates =
					new bool[buttonCount];

				reading->GetControllerButtonState(
					buttonCount,
					buttonStates);

				for (uint32_t i = 0; i < extraButtonCount; i++)
				{
					uint8_t index = extraButtonIndexes[i];

					if (index < buttonCount &&
						index == DualSenseTouchpadButton &&
						buttonStates[index])
					{
						State->buttons |=
							static_cast<unsigned int>(
								GameInputGamepadView);
					}
				}

				delete[] buttonStates;
			}

			delete[] extraButtonIndexes;
		}
	}

	device->Release(); // Release the device object
	reading->Release(); // Release the reading object

	return success;

}

bool ControllerInput_GetDeviceInfo(ControllerDeviceInfo* info) {
	if (g_gameInput == nullptr || info == nullptr)
		return false; // Not initialized or invalid info pointer

	IGameInputReading* reading = nullptr;

	HRESULT result = g_gameInput->GetCurrentReading(
		GameInputKindGamepad,
		nullptr,
		&reading
	);

	if (FAILED(result) || reading == nullptr)
		return false; // Failed to get reading

	IGameInputDevice* device = nullptr;
	reading->GetDevice(&device);

	if (device == nullptr) {
		reading->Release();
		return false; // Failed to get device
	}

	const GameInputDeviceInfo* deviceInfo = nullptr;
	result = device->GetDeviceInfo(&deviceInfo);

	if (SUCCEEDED(result) && deviceInfo != nullptr) {
		info->vendorId = deviceInfo->vendorId;
		info->productId = deviceInfo->productId;
	}

	device->Release(); // Release the device object
	reading->Release(); // Release the reading object

	return SUCCEEDED(result) && deviceInfo != nullptr;
}