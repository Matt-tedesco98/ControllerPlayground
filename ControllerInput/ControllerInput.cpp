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