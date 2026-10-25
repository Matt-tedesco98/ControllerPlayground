#pragma once
#include <stdint.h>

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

// Additive ABI v2. IDs are opaque GameInput bytes, not GUIDs or VID/PID pairs.
// Structures have fixed 8-byte packing on both sides of the DLL boundary.
#pragma pack(push, 8)
struct ControllerDeviceIdV2 { uint8_t bytes[32]; };

struct ControllerDeviceSnapshotV2 {
    uint32_t structSize;
    uint32_t connected;
    ControllerDeviceIdV2 deviceId;
    ControllerDeviceIdV2 deviceRootId;
    uint64_t connectionGeneration;
    uint64_t timestamp; // GameInput microseconds, not wall-clock time.
    uint16_t vendorId;
    uint16_t productId;
    uint32_t readingAvailable; // False is NOT a disconnect (e.g. focus loss).
    ControllerState state; // Actual held state, without synthesized press bits.
    uint32_t pressedButtons; // Transitions observed during the most recent poll.
    uint32_t releasedButtons;
    uint32_t guidePressed; // Latched Guide edge since the previous v2 poll.
    uint32_t historyDiscontinuity;
};

enum ControllerCaptureEventKindV2 : uint32_t {
    ControllerCaptureConnectedV2 = 1,
    ControllerCaptureDisconnectedV2 = 2,
    ControllerCaptureInputV2 = 3,
    ControllerCaptureGuideV2 = 4,
    ControllerCaptureHistoryResetV2 = 5
};

struct ControllerCaptureEventV2 {
    uint32_t structSize;
    uint32_t kind;
    uint64_t sequence;
    ControllerDeviceSnapshotV2 device;
};

struct ControllerCaptureDiagnosticsV2 {
    uint32_t structSize;
    uint32_t gameInputApiVersion;
    int32_t initializationResult;
    int32_t deviceCallbackResult;
    int32_t guideCallbackResult;
    uint32_t connectedDevices;
    uint64_t droppedEvents;
    uint64_t unattributedRawGuidePresses;
    uint64_t historyResets;
};
#pragma pack(pop)

static_assert(sizeof(ControllerDeviceIdV2) == 32);
static_assert(sizeof(ControllerState) == 32);
static_assert(sizeof(ControllerDeviceSnapshotV2) == 144);
static_assert(sizeof(ControllerCaptureEventV2) == 160);
static_assert(sizeof(ControllerCaptureDiagnosticsV2) == 48);

extern "C"
{
    CONTROLLERINPUT_API bool ControllerInput_Initialize();
	CONTROLLERINPUT_API bool ControllerInput_GetState(ControllerState* state);
	CONTROLLERINPUT_API bool ControllerInput_GetDeviceInfo(ControllerDeviceInfo* info);
	CONTROLLERINPUT_API void ControllerInput_SetWindowHandle(void* windowHandle);

    CONTROLLERINPUT_API uint32_t ControllerInput_GetApiVersion();
    // HRESULT as int32_t. Poll first, then copy all known devices (including
    // disconnected tombstones). A count query / insufficient buffer does not poll.
    // Retry with *required entries and the exact structure stride. Poll and drain
    // belong to ONE v2 consumer per process; legacy polling has independent cursors.
    CONTROLLERINPUT_API int32_t ControllerInput_PollDevicesV2(
        ControllerDeviceSnapshotV2* devices, uint32_t capacity,
        uint32_t stride, uint32_t* required);
    CONTROLLERINPUT_API int32_t ControllerInput_DrainEventsV2(
        ControllerCaptureEventV2* events, uint32_t capacity,
        uint32_t stride, uint32_t* written);
    // Increment 2 additive query: the last v2 poll's short-tap/stick-flick
    // projection for this exact connection. Does not poll or consume input.
    CONTROLLERINPUT_API int32_t ControllerInput_CopyNavigationProjectionV2(
        const ControllerDeviceIdV2* deviceId, uint64_t generation, ControllerState* state);
    CONTROLLERINPUT_API int32_t ControllerInput_GetDiagnosticsV2(
        ControllerCaptureDiagnosticsV2* diagnostics, uint32_t size);
    CONTROLLERINPUT_API void ControllerInput_SetDiagnosticsEnabledV2(bool enabled);
    // Call on the window's owning thread, before destroying its HWND. Not DllMain.
    CONTROLLERINPUT_API void ControllerInput_Shutdown();
}
