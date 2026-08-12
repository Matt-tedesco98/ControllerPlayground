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

	reading->Release(); // Release the reading object

	return success;

}