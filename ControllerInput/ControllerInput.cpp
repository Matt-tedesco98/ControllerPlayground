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

static volatile LONG g_pendingSystemButtons = 0;
static  GameInputCallbackToken g_guideCallbackToken{};

static HWND g_windowHandle = nullptr;

static WNDPROC g_previousWindowProc = nullptr;

static bool g_dualSensePsPressed = false;

static IGameInput* g_gameInput = nullptr;
static IGameInputReading* g_currentReading = nullptr;
static IGameInputReading* g_previousReading = nullptr;

constexpr uint16_t SonyVendorId = 0x054C;
constexpr uint16_t DualSenseProductId = 0x0CE6;

constexpr uint8_t DualSensePsButton = 12;
constexpr uint8_t DualSenseTouchpadButton = 13;
constexpr uint8_t DualSenseMuteButton = 14;

constexpr unsigned int ControllerSystemButtonGuide = 0x00000001;

static bool IsDualSensePsPressed(
	const BYTE* report,
	UINT reportSize)
{
	if (report == nullptr ||
		reportSize == 0)
	{
		return false;
	}

	// USB full report.
	if (report[0] == 0x01 &&
		reportSize >= 64)
	{
		return (report[10] & 0x01) != 0;
	}

	// Bluetooth full report.
	if (report[0] == 0x31 &&
		reportSize >= 78)
	{
		return (report[11] & 0x01) != 0;
	}

	// Bluetooth minimal report.
	if (report[0] == 0x01 &&
		reportSize >= 8)
	{
		return (report[7] & 0x01) != 0;
	}

	return false;
}

static void CALLBACK OnSystemButton(
	GameInputCallbackToken callbackToken,
	void* context,
	IGameInputDevice* device,
	uint64_t timestamp,
	GameInputSystemButtons currentButtons,
	GameInputSystemButtons previousButtons) {

	bool guidePressed = (currentButtons & GameInputSystemButtonGuide) != 0;

	bool guideWasPressed = (previousButtons & GameInputSystemButtonGuide) != 0;

	if (guidePressed && !guideWasPressed) {
		InterlockedOr(&g_pendingSystemButtons, ControllerSystemButtonGuide);
	}
}

bool ControllerInput_Initialize() {
	if (g_gameInput != nullptr)
		return true; // Already initialized

	HRESULT result = GameInputCreate(&g_gameInput);

	if (FAILED(result) || g_gameInput == nullptr)
		return false; // Failed to create GameInput instance

	GameInputFocusPolicy focusPolicy = static_cast<GameInputFocusPolicy>(GameInputExclusiveForegroundInput | GameInputEnableBackgroundGuideButton);

	g_gameInput->SetFocusPolicy(focusPolicy);

	HRESULT callbackResult = g_gameInput->RegisterSystemButtonCallback(
		nullptr,
		GameInputSystemButtonGuide,
		nullptr,
		OnSystemButton,
		&g_guideCallbackToken
	);

	if (FAILED(callbackResult))
	{
		OutputDebugStringA(
			"ControllerInput: Guide callback registration FAILED\n");
		}

	return SUCCEEDED(callbackResult);
}

static bool ReadControllerState(IGameInputReading* reading, ControllerState* state) {
	if (reading == nullptr || state == nullptr)
		return false; // Invalid parameters

	*state = {}; // Clear the state structure

	IGameInputDevice* device = nullptr;
	reading->GetDevice(&device);

	if (device == nullptr) 
		return false; // Failed to get device
	
	GameInputDeviceStatus status = device->GetDeviceStatus();

	if ((status & GameInputDeviceConnected) != GameInputDeviceConnected) {
		device->Release();
		return false; // Device is not connected
	}

	GameInputGamepadState gamepadState{};

	bool success = reading->GetGamepadState(&gamepadState);

	if (success) {
		state->buttons = static_cast<unsigned int>(gamepadState.buttons);
		state->leftTrigger = gamepadState.leftTrigger;
		state->rightTrigger = gamepadState.rightTrigger;
		state->leftStickX = gamepadState.leftThumbstickX;
		state->leftStickY = gamepadState.leftThumbstickY;
		state->rightStickX = gamepadState.rightThumbstickX;
		state->rightStickY = gamepadState.rightThumbstickY;
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

		if (SUCCEEDED(
			device->GetExtraButtonCount(
				GameInputKindGamepad,
				&extraButtonCount)) &&
			extraButtonCount > 0)
		{
			uint8_t* extraButtonIndexes =
				new uint8_t[extraButtonCount];

			if (SUCCEEDED(
				device->GetExtraButtonIndexes(
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

				for (uint32_t i = 0;
					i < extraButtonCount;
					i++)
				{
					uint8_t index =
						extraButtonIndexes[i];

					if (index < buttonCount &&
						buttonStates[index])
					{
						if (index ==
							DualSenseTouchpadButton)
						{
							state->buttons |=
								static_cast<unsigned int>(
									GameInputGamepadView);
						}

						if (index ==
							DualSensePsButton)
						{
							state->systemButtons |=
								ControllerSystemButtonGuide;
						}
					}
				}

				delete[] buttonStates;
			}

			delete[] extraButtonIndexes;
		}
	}

	device->Release();

	return success;

}

bool ControllerInput_GetState(
	ControllerState* State)
{
	if (g_gameInput == nullptr ||
		State == nullptr)
	{
		return false;
	}

	*State = {};

	LONG pendingSystemButtons =
		InterlockedExchange(
			&g_pendingSystemButtons,
			0);

	if (pendingSystemButtons != 0)
	{
		State->systemButtons |=
			static_cast<unsigned int>(
				pendingSystemButtons);

		return true;
	}

	// First poll establishes our position
	// in GameInput's reading history.
	if (g_previousReading == nullptr)
	{
		IGameInputReading* reading = nullptr;

		HRESULT result =
			g_gameInput->GetCurrentReading(
				GameInputKindGamepad,
				nullptr,
				&reading);

		if (FAILED(result) ||
			reading == nullptr)
		{
			return false;
		}

		if (!ReadControllerState(
			reading,
			State))
		{
			reading->Release();
			return false;
		}

		// Keep this reference so GetNextReading
		// knows where we left off next time.
		g_previousReading = reading;

		return true;
	}

	ControllerState latestState{};

	if (!ReadControllerState(
		g_previousReading,
		&latestState))
	{
		g_previousReading->Release();
		g_previousReading = nullptr;

		return false;
	}

	float strongestLeftStickX =
    latestState.leftStickX;

float strongestLeftStickY =
    latestState.leftStickY;

float strongestLeftStickMagnitude =
    (strongestLeftStickX * strongestLeftStickX) +
    (strongestLeftStickY * strongestLeftStickY);

	unsigned int pressedButtons = 0;
	unsigned int pressedSystemButtons = 0;

	while (true)
	{
		IGameInputReading* nextReading = nullptr;

		HRESULT result =
			g_gameInput->GetNextReading(
				g_previousReading,
				GameInputKindGamepad,
				nullptr,
				&nextReading);

		if (FAILED(result) ||
			nextReading == nullptr)
		{
			// No newer reading is normal.
			if (result !=
				GAMEINPUT_E_READING_NOT_FOUND)
			{
				// Something invalidated our position
				// in the history. Re-sync next poll.
				g_previousReading->Release();
				g_previousReading = nullptr;
			}

			break;
		}

		ControllerState nextState{};

		if (!ReadControllerState(
			nextReading,
			&nextState))
		{
			nextReading->Release();
			break;
		}

		// Capture every up -> down transition,
		// even if the button was released again
		// before the WinUI timer polls us.
		pressedButtons |=
			nextState.buttons &
			~latestState.buttons;

		pressedSystemButtons |=
			nextState.systemButtons &
			~latestState.systemButtons;

		float stickMagnitude =
			(nextState.leftStickX * nextState.leftStickX) +
			(nextState.leftStickY * nextState.leftStickY);

		if (stickMagnitude >
			strongestLeftStickMagnitude)
		{
			strongestLeftStickMagnitude =
				stickMagnitude;

			strongestLeftStickX =
				nextState.leftStickX;

			strongestLeftStickY =
				nextState.leftStickY;
		}

		latestState = nextState;

		// Move our history cursor forward.
		g_previousReading->Release();
		g_previousReading = nextReading;
	}

	/*
		Return the newest actual state, but temporarily
		include any presses that occurred between polls.

		Example:

		UI poll      A = up
					 ↓
				A pressed
				A released
					 ↓
		UI poll      normally A = up again

		pressedButtons makes that second poll report
		A as pressed once, so C# cannot miss it.
	*/

	float latestStickMagnitude =
		(latestState.leftStickX * latestState.leftStickX) +
		(latestState.leftStickY * latestState.leftStickY);

	// If the stick moved strongly between polls but is already
	// centered again, preserve that movement for this poll.
	constexpr float PressThresholdSquared =
		0.65f * 0.65f;

	constexpr float ReleaseThresholdSquared =
		0.30f * 0.30f;

	if (latestStickMagnitude <=
		ReleaseThresholdSquared &&
		strongestLeftStickMagnitude >=
		PressThresholdSquared)
	{
		latestState.leftStickX =
			strongestLeftStickX;

		latestState.leftStickY =
			strongestLeftStickY;
	}

	latestState.buttons |=
		pressedButtons;

	latestState.systemButtons |=
		pressedSystemButtons;

	*State = latestState;

	return true;
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

static LRESULT CALLBACK ControllerInput_WindowProc(
	HWND hWnd,
	UINT message,
	WPARAM wParam,
	LPARAM lParam)
{
	if (message == WM_INPUT &&
		GET_RAWINPUT_CODE_WPARAM(wParam) == RIM_INPUTSINK)
	{
		UINT size = 0;

		GetRawInputData(
			reinterpret_cast<HRAWINPUT>(lParam),
			RID_INPUT,
			nullptr,
			&size,
			sizeof(RAWINPUTHEADER));

		if (size > 0)
		{
			BYTE* buffer = new BYTE[size];

			if (GetRawInputData(
				reinterpret_cast<HRAWINPUT>(lParam),
				RID_INPUT,
				buffer,
				&size,
				sizeof(RAWINPUTHEADER)) == size)
			{
				RAWINPUT* raw =
					reinterpret_cast<RAWINPUT*>(buffer);

				RID_DEVICE_INFO deviceInfo{};
				deviceInfo.cbSize =
					sizeof(RID_DEVICE_INFO);

				UINT deviceInfoSize =
					sizeof(RID_DEVICE_INFO);

				if (GetRawInputDeviceInfo(
					raw->header.hDevice,
					RIDI_DEVICEINFO,
					&deviceInfo,
					&deviceInfoSize) !=
					static_cast<UINT>(-1))
				{
					if (deviceInfo.dwType == RIM_TYPEHID &&
						deviceInfo.hid.dwVendorId == SonyVendorId &&
						deviceInfo.hid.dwProductId ==
						DualSenseProductId)
					{
						UINT reportSize =
							raw->data.hid.dwSizeHid;

						const BYTE* reportData =
							raw->data.hid.bRawData;

						for (UINT i = 0;
							i < raw->data.hid.dwCount;
							i++)
						{
							const BYTE* report =
								reportData + (i * reportSize);

							bool psPressed =
								IsDualSensePsPressed(
									report,
									reportSize);

							if (psPressed &&
								!g_dualSensePsPressed)
							{
								InterlockedOr(
									&g_pendingSystemButtons,
									ControllerSystemButtonGuide);
							}

							g_dualSensePsPressed =
								psPressed;
						}
					}
				}
			}

			delete[] buffer;
		}
	}

	return CallWindowProc(
		g_previousWindowProc,
		hWnd,
		message,
		wParam,
		lParam);
}

void ControllerInput_SetWindowHandle(
	void* windowHandle)
{
	g_windowHandle =
		static_cast<HWND>(windowHandle);

	if (g_windowHandle == nullptr)
		return;

	RAWINPUTDEVICE devices[2]{};

	devices[0].usUsagePage = 0x01;
	devices[0].usUsage = 0x04;
	devices[0].dwFlags = RIDEV_INPUTSINK;
	devices[0].hwndTarget = g_windowHandle;

	devices[1].usUsagePage = 0x01;
	devices[1].usUsage = 0x05;
	devices[1].dwFlags = RIDEV_INPUTSINK;
	devices[1].hwndTarget = g_windowHandle;

	if (!RegisterRawInputDevices(
		devices,
		2,
		sizeof(RAWINPUTDEVICE)))
	{
		OutputDebugStringA(
			"ControllerInput: Raw Input registration FAILED\n");
	}

	g_previousWindowProc =
		reinterpret_cast<WNDPROC>(
			SetWindowLongPtr(
				g_windowHandle,
				GWLP_WNDPROC,
				reinterpret_cast<LONG_PTR>(
					ControllerInput_WindowProc)));
}