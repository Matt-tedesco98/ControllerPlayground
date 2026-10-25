#include "pch.h"
#include "ControllerInput.h"
#include "ControllerCaptureState.h"
#include <GameInput.h>
#include <wrl/client.h>
#include <map>
#include <mutex>
#include <vector>
#include <algorithm>
#include <cstdio>

#if GAMEINPUT_API_VERSION == 1
using namespace GameInput::v1;
#elif GAMEINPUT_API_VERSION == 2
using namespace GameInput::v2;
#elif GAMEINPUT_API_VERSION == 3
using namespace GameInput::v3;
#endif
using Microsoft::WRL::ComPtr;
using namespace ControllerCapture;

namespace {
struct HistoryCursor {
    ComPtr<IGameInputReading> reading;
    ReadingBaseline baseline;
    void Reset() { reading.Reset(); baseline = {}; }
};
struct DeviceCapture {
    ComPtr<IGameInputDevice> device;
    HistoryCursor cursor;
    HistoryCursor legacyCursor;
    ConnectionState connection;
    ControllerDeviceSnapshotV2 snapshot{};
    ControllerState navigationProjection{};
    bool pendingGuide = false;
    bool pendingLegacyGuide = false;
};
// Public calls are serialized separately from callbacks. Never hold g_captureMutex
// while registering/unregistering callbacks (blocking enumeration can call back).
struct CaptureRuntime {
    std::mutex apiMutex;
    std::mutex captureMutex;
    ComPtr<IGameInput> input;
    std::map<DeviceKey, DeviceCapture> devices;
    EventQueue events;
    std::map<HANDLE, bool> rawPressed;
    std::map<HANDLE, DeviceKey> rawDevices;
};
// Legacy hosts do not call Shutdown yet. Keep the runtime's storage alive until
// process termination instead of destroying containers while callbacks can run
// during CRT/DLL teardown. Explicit Shutdown releases all device/reading refs.
CaptureRuntime& Runtime() { static auto* runtime = new CaptureRuntime; return *runtime; }
auto& g_apiMutex = Runtime().apiMutex;
auto& g_captureMutex = Runtime().captureMutex;
auto& g_gameInput = Runtime().input;
auto& g_devices = Runtime().devices;
auto& g_events = Runtime().events;
ControllerCaptureDiagnosticsV2 g_diagnostics{sizeof(ControllerCaptureDiagnosticsV2), GAMEINPUT_API_VERSION};
GameInputCallbackToken g_deviceCallbackToken{};
GameInputCallbackToken g_guideCallbackToken{};
bool g_deviceCallbackRegistered = false;
bool g_guideCallbackRegistered = false;
bool g_initialized = false;
bool g_diagnosticsEnabled = false;
volatile LONG g_pendingSystemButtons = 0; // Legacy ABI only; v2 retains attribution.
HWND g_windowHandle = nullptr;
WNDPROC g_previousWindowProc = nullptr;
auto& g_rawPressed = Runtime().rawPressed;
auto& g_rawDevices = Runtime().rawDevices;
DeviceKey g_legacyDevice{};
bool g_hasLegacyDevice = false;

constexpr uint16_t SonyVendorId = 0x054C;
constexpr uint16_t DualSenseProductId = 0x0CE6;
constexpr uint8_t DualSensePsButton = 12;
constexpr uint8_t DualSenseTouchpadButton = 13;
constexpr unsigned int ControllerSystemButtonGuide = 1;

ControllerDeviceIdV2 CopyId(const APP_LOCAL_DEVICE_ID& id) {
    static_assert(sizeof(id) == sizeof(ControllerDeviceIdV2));
    ControllerDeviceIdV2 result{};
    std::memcpy(result.bytes, &id, sizeof(result));
    return result;
}

void Emit(DeviceCapture& capture, uint32_t kind) {
    capture.snapshot.connected = capture.connection.connected ? 1u : 0u;
    capture.snapshot.connectionGeneration = capture.connection.generation;
    g_events.Push({sizeof(ControllerCaptureEventV2), kind, 0, capture.snapshot});
    if (!g_diagnosticsEnabled || kind == ControllerCaptureInputV2) return;
    char id[65]{};
    for (size_t i = 0; i < 32; ++i)
        sprintf_s(id + i * 2, 3, "%02X", capture.snapshot.deviceId.bytes[i]);
    char message[256]{};
    sprintf_s(message, "ControllerInput v2: kind=%u id=%s generation=%llu connected=%u\n",
        kind, id, capture.connection.generation, capture.snapshot.connected);
    OutputDebugStringA(message);
}

// Requires g_captureMutex; all identity and generation decisions are made here.
DeviceCapture* TrackDevice(IGameInputDevice* device, bool connected, uint64_t timestamp) {
    const GameInputDeviceInfo* info = nullptr;
    if (!device || FAILED(device->GetDeviceInfo(&info)) || !info) return nullptr;
    const auto id = CopyId(info->deviceId);
    auto& capture = g_devices[Key(id)];
    // A delayed callback from the old connection must not disconnect or revive
    // the replacement endpoint after a rapid unplug/replug with the same ID.
    if (timestamp < capture.connection.statusTimestamp) return nullptr;
    capture.snapshot.structSize = sizeof(capture.snapshot);
    capture.snapshot.deviceId = id;
    capture.snapshot.deviceRootId = CopyId(info->deviceRootId);
    capture.snapshot.vendorId = info->vendorId;
    capture.snapshot.productId = info->productId;
    if (capture.connection.SetConnected(connected, timestamp)) {
        capture.cursor.Reset();
        capture.legacyCursor.Reset();
        capture.pendingGuide = false;
        capture.pendingLegacyGuide = false;
        capture.snapshot.timestamp = timestamp;
        capture.snapshot.state = {};
        capture.navigationProjection = {};
        capture.snapshot.pressedButtons = capture.snapshot.releasedButtons = 0;
        capture.snapshot.guidePressed = capture.snapshot.historyDiscontinuity = 0;
        capture.snapshot.readingAvailable = 0;
        capture.device = connected ? device : nullptr;
        Emit(capture, connected ? ControllerCaptureConnectedV2 : ControllerCaptureDisconnectedV2);
        if (!connected) {
            for (auto entry = g_rawDevices.begin(); entry != g_rawDevices.end();) {
                if (entry->second == Key(id)) {
                    g_rawPressed.erase(entry->first);
                    entry = g_rawDevices.erase(entry);
                } else ++entry;
            }
        }
    }
    return &capture;
}

void GuideSourceChanged(DeviceCapture& capture, uint32_t source, bool pressed, uint64_t timestamp) {
    if (!capture.connection.SetGuide(source, pressed, timestamp)) return;
    capture.pendingGuide = true;
    capture.pendingLegacyGuide = true;
    capture.snapshot.guidePressed = 1;
    capture.snapshot.timestamp = timestamp;
    Emit(capture, ControllerCaptureGuideV2);
}

void CALLBACK OnDevice(GameInputCallbackToken, void*, IGameInputDevice* device,
    uint64_t timestamp, GameInputDeviceStatus current, GameInputDeviceStatus) {
    std::lock_guard lock(g_captureMutex);
    TrackDevice(device, (current & GameInputDeviceConnected) != 0, timestamp);
}

void CALLBACK OnSystemButton(GameInputCallbackToken, void*, IGameInputDevice* device,
    uint64_t timestamp, GameInputSystemButtons current, GameInputSystemButtons) {
    std::lock_guard lock(g_captureMutex);
    const bool connected = device && (device->GetDeviceStatus() & GameInputDeviceConnected) != 0;
    auto* capture = TrackDevice(device, connected, timestamp);
    if (capture) GuideSourceChanged(*capture, GuideSource::Callback,
        (current & GameInputSystemButtonGuide) != 0, timestamp);
}

bool IsDualSensePsPressed(const BYTE* report, UINT size) {
    if (!report || !size) return false;
    if (report[0] == 0x01 && size >= 64) return (report[10] & 1) != 0;
    if (report[0] == 0x31 && size >= 78) return (report[11] & 1) != 0;
    if (report[0] == 0x01 && size >= 8) return (report[7] & 1) != 0;
    return false;
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

// Poll one device's own history. Legacy and v2 have independent cursors, so a
// diagnostic consumer cannot steal brief presses from single-controller navigation.
bool PollHistory(DeviceCapture& capture, HistoryCursor& history,
    ControllerState& actual, ControllerState& projected, uint32_t& pressed,
    uint32_t& released, bool& discontinuity, uint64_t& timestamp, bool trackGuide) {
    actual = {}; projected = {}; pressed = released = 0; discontinuity = false;
    if (!capture.connection.connected || !capture.device) return false;

    // Query current state before touching retained history. Idle devices can keep
    // returning an unchanged sample that has already aged out of the history ring.
    ComPtr<IGameInputReading> latest;
    if (FAILED(g_gameInput->GetCurrentReading(GameInputKindGamepad, capture.device.Get(), &latest)) || !latest) return false;
    const auto latestTimestamp = latest->GetTimestamp();
    // Initial enumeration may legitimately expose an existing held state. After a
    // reconnect, however, no sample from before that connection is acceptable.
    const uint64_t floor = capture.connection.generation > 1 ? capture.connection.connectedAt : 0;
    const auto classification = history.baseline.Classify(latestTimestamp, floor);
    if (classification == LatestSample::Stale) return false;
    if (classification == LatestSample::Unchanged) {
        actual = projected = history.baseline.state;
        timestamp = history.baseline.timestamp;
        return true;
    }

    ReadingAccumulator accumulator(history.baseline.state);
    auto accept = [&](IGameInputReading* reading) {
        const auto sampleTimestamp = reading->GetTimestamp();
        if (sampleTimestamp < floor || (history.baseline.hasSample && sampleTimestamp <= history.baseline.timestamp)) return false;
        ControllerState state{};
        if (!ReadControllerState(reading, &state)) return false;
        accumulator.Add(state);
        history.baseline.Commit(state, sampleTimestamp);
        timestamp = sampleTimestamp;
        if (trackGuide) GuideSourceChanged(capture, GuideSource::ExtraButton,
            (state.systemButtons & ControllerSystemButtonGuide) != 0, sampleTimestamp);
        return true;
    };

    if (!history.reading || !history.baseline.hasSample) {
        // Seed against the neutral per-connection baseline so a fresh first
        // sample containing A is not silently omitted from pressedButtons.
        if (!accept(latest.Get())) return false;
        history.reading = latest;
    } else {
        constexpr uint32_t MaxReadingsPerPoll = 4096;
        bool reachedLatest = false;
        for (uint32_t count = 0; count < MaxReadingsPerPoll; ++count) {
            ComPtr<IGameInputReading> next;
            const HRESULT hr = g_gameInput->GetNextReading(history.reading.Get(), GameInputKindGamepad, capture.device.Get(), &next);
            if (FAILED(hr) || !next) {
                // We already know a newer current sample exists. If history has
                // expired, reconcile it against the last accepted state instead
                // of throwing away the edge baseline and replaying stale state.
                discontinuity = true;
                break;
            }
            if (!accept(next.Get())) { discontinuity = true; break; }
            history.reading = next;
            if (history.baseline.timestamp >= latestTimestamp) { reachedLatest = true; break; }
        }
        if (!reachedLatest) {
            discontinuity = true;
            if (history.baseline.Classify(latestTimestamp, floor) == LatestSample::New) {
                if (!accept(latest.Get())) return false;
                history.reading = latest;
            }
        }
    }
    actual = accumulator.actual;
    pressed = accumulator.pressed;
    released = accumulator.released;
    projected = accumulator.ProjectLegacy();
    return true;
}
void PollCapture(DeviceCapture& capture) {
    if (!capture.connection.connected) return;
    const auto previous = capture.snapshot.state;
    ControllerState actual{}, projected{};
    bool reset = false;
    uint64_t timestamp = capture.snapshot.timestamp;
    capture.snapshot.readingAvailable = PollHistory(capture, capture.cursor, actual, projected,
        capture.snapshot.pressedButtons, capture.snapshot.releasedButtons, reset, timestamp, true) ? 1u : 0u;
    capture.navigationProjection = projected;
    capture.snapshot.historyDiscontinuity = reset ? 1u : 0u;
    capture.snapshot.guidePressed = capture.pendingGuide ? 1u : 0u;
    capture.pendingGuide = false;
    capture.snapshot.timestamp = timestamp;
    capture.snapshot.state = actual;
    if (reset) { ++g_diagnostics.historyResets; Emit(capture, ControllerCaptureHistoryResetV2); }
    if (capture.snapshot.readingAvailable && (std::memcmp(&previous, &actual, sizeof(actual)) != 0 ||
        capture.snapshot.pressedButtons || capture.snapshot.releasedButtons)) {
        Emit(capture, ControllerCaptureInputV2);
    }
}

// API mutex held; callback unregister waits must happen without the capture mutex.
void ShutdownCapture() {
    if (g_gameInput) {
        if (g_guideCallbackRegistered) g_gameInput->UnregisterCallback(g_guideCallbackToken);
        if (g_deviceCallbackRegistered) g_gameInput->UnregisterCallback(g_deviceCallbackToken);
    }
    std::lock_guard lock(g_captureMutex);
    g_guideCallbackRegistered = g_deviceCallbackRegistered = g_initialized = false;
    g_devices.clear(); g_rawDevices.clear(); g_rawPressed.clear();
    g_events = EventQueue{};
    g_gameInput.Reset();
    g_hasLegacyDevice = false;
    InterlockedExchange(&g_pendingSystemButtons, 0);
}

bool ResolveRawDevice(HANDLE handle, DeviceKey& key) {
    ComPtr<IGameInput> input;
    {
        std::lock_guard lock(g_captureMutex);
        const auto found = g_rawDevices.find(handle);
        if (found != g_rawDevices.end()) { key = found->second; return true; }
        input = g_gameInput;
    }
    if (!input) return false;
    UINT chars = 0;
    if (GetRawInputDeviceInfoW(handle, RIDI_DEVICENAME, nullptr, &chars) == static_cast<UINT>(-1) || !chars) return false;
    std::vector<wchar_t> path(chars + 1);
    if (GetRawInputDeviceInfoW(handle, RIDI_DEVICENAME, path.data(), &chars) == static_cast<UINT>(-1)) return false;
    ComPtr<IGameInputDevice> device;
    if (FAILED(input->FindDeviceFromPlatformString(path.data(), &device)) || !device) return false;
    std::lock_guard lock(g_captureMutex);
    auto* capture = TrackDevice(device.Get(), (device->GetDeviceStatus() & GameInputDeviceConnected) != 0, input->GetCurrentTimestamp());
    if (!capture || !capture->connection.connected) return false;
    key = Key(capture->snapshot.deviceId);
    g_rawDevices[handle] = key;
    return true;
}

LRESULT CALLBACK ControllerInput_WindowProc(HWND window, UINT message, WPARAM wParam, LPARAM lParam) {
    if (message == WM_INPUT && GET_RAWINPUT_CODE_WPARAM(wParam) == RIM_INPUTSINK) {
        UINT size = 0;
        if (GetRawInputData(reinterpret_cast<HRAWINPUT>(lParam), RID_INPUT, nullptr, &size, sizeof(RAWINPUTHEADER)) == 0 && size >= sizeof(RAWINPUTHEADER)) {
            std::vector<BYTE> buffer(size);
            if (GetRawInputData(reinterpret_cast<HRAWINPUT>(lParam), RID_INPUT, buffer.data(), &size, sizeof(RAWINPUTHEADER)) == size) {
                const auto* raw = reinterpret_cast<const RAWINPUT*>(buffer.data());
                RID_DEVICE_INFO info{}; info.cbSize = sizeof(info);
                UINT infoSize = sizeof(info);
                if (raw->header.dwType == RIM_TYPEHID && GetRawInputDeviceInfoW(raw->header.hDevice, RIDI_DEVICEINFO, &info, &infoSize) != static_cast<UINT>(-1) &&
                    info.hid.dwVendorId == SonyVendorId && info.hid.dwProductId == DualSenseProductId) {
                    DeviceKey key{};
                    const bool attributed = ResolveRawDevice(raw->header.hDevice, key);
                    const UINT reportSize = raw->data.hid.dwSizeHid;
                    const size_t offset = reinterpret_cast<const BYTE*>(raw->data.hid.bRawData) - buffer.data();
                    if (reportSize && offset <= size && raw->data.hid.dwCount <= (size - offset) / reportSize) {
                        std::lock_guard lock(g_captureMutex);
                        for (UINT i = 0; i < raw->data.hid.dwCount; ++i) {
                            const bool pressed = IsDualSensePsPressed(raw->data.hid.bRawData + static_cast<size_t>(i) * reportSize, reportSize);
                            if (attributed) {
                                auto found = g_devices.find(key);
                                if (found != g_devices.end()) GuideSourceChanged(found->second, GuideSource::RawInput, pressed,
                                    g_gameInput ? g_gameInput->GetCurrentTimestamp() : 0);
                            } else if (pressed && !g_rawPressed[raw->header.hDevice]) {
                                // Preserve the legacy PS fallback, but never invent a v2 owner.
                                ++g_diagnostics.unattributedRawGuidePresses;
                                InterlockedOr(&g_pendingSystemButtons, ControllerSystemButtonGuide);
                                if (g_diagnosticsEnabled) OutputDebugStringA("ControllerInput v2: unattributed Raw Input Guide (legacy only)\n");
                            }
                            g_rawPressed[raw->header.hDevice] = pressed;
                        }
                    }
                }
            }
        }
    }
    return g_previousWindowProc ? CallWindowProc(g_previousWindowProc, window, message, wParam, lParam) : DefWindowProc(window, message, wParam, lParam);
}
} // namespace

bool ControllerInput_Initialize() {
    std::lock_guard api(g_apiMutex);
    if (g_initialized) return true;
    ComPtr<IGameInput> input;
    const HRESULT hr = GameInputCreate(&input);
    g_diagnostics = {sizeof(g_diagnostics), GAMEINPUT_API_VERSION, hr};
    if (FAILED(hr)) return false;
    {
        std::lock_guard lock(g_captureMutex);
        g_gameInput = input;
    }
    // Deliberately unchanged in Increment 1.
    input->SetFocusPolicy(static_cast<GameInputFocusPolicy>(GameInputExclusiveForegroundInput | GameInputEnableBackgroundGuideButton));
    g_diagnostics.deviceCallbackResult = input->RegisterDeviceCallback(nullptr, GameInputKindGamepad,
        GameInputDeviceConnected, GameInputBlockingEnumeration, nullptr, OnDevice, &g_deviceCallbackToken);
    g_deviceCallbackRegistered = SUCCEEDED(g_diagnostics.deviceCallbackResult);
    g_diagnostics.guideCallbackResult = input->RegisterSystemButtonCallback(nullptr,
        GameInputSystemButtonGuide, nullptr, OnSystemButton, &g_guideCallbackToken);
    g_guideCallbackRegistered = SUCCEEDED(g_diagnostics.guideCallbackResult);
    if (!g_deviceCallbackRegistered || !g_guideCallbackRegistered) {
        OutputDebugStringA("ControllerInput: callback registration FAILED\n");
        ShutdownCapture();
        return false;
    }
    g_initialized = true;
    return true;
}

bool ControllerInput_GetState(ControllerState* state) {
    if (!state) return false;
    *state = {};
    std::lock_guard api(g_apiMutex);
    std::lock_guard lock(g_captureMutex);
    if (!g_initialized) return false;
    LONG guide = InterlockedExchange(&g_pendingSystemButtons, 0);
    for (auto& [key, capture] : g_devices) {
        if (capture.connection.connected && capture.pendingLegacyGuide) guide |= ControllerSystemButtonGuide;
        capture.pendingLegacyGuide = false;
    }
    if (guide) { state->systemButtons = static_cast<uint32_t>(guide); return true; }
    auto selected = g_devices.find(g_legacyDevice);
    if (!g_hasLegacyDevice || selected == g_devices.end() || !selected->second.connection.connected) {
        selected = std::find_if(g_devices.begin(), g_devices.end(), [](const auto& entry) { return entry.second.connection.connected; });
        if (selected == g_devices.end()) { g_hasLegacyDevice = false; return false; }
        g_legacyDevice = selected->first; g_hasLegacyDevice = true;
    }
    ControllerState actual{};
    uint32_t pressed = 0, released = 0;
    bool reset = false;
    uint64_t timestamp = 0;
    const bool available = PollHistory(selected->second, selected->second.legacyCursor, actual, *state, pressed, released, reset, timestamp, true);
    // Guide edges detected in the reading just returned must not become a second
    // legacy pulse on the following poll. The attributed v2 edge remains latched.
    state->systemButtons |= static_cast<uint32_t>(InterlockedExchange(&g_pendingSystemButtons, 0));
    if (selected->second.pendingLegacyGuide) state->systemButtons |= ControllerSystemButtonGuide;
    selected->second.pendingLegacyGuide = false;
    return available || state->systemButtons != 0;
}

bool ControllerInput_GetDeviceInfo(ControllerDeviceInfo* info) {
    if (!info) return false;
    *info = {};
    std::lock_guard api(g_apiMutex);
    std::lock_guard lock(g_captureMutex);
    if (!g_initialized) return false;
    auto found = g_devices.find(g_legacyDevice);
    if (!g_hasLegacyDevice || found == g_devices.end() || !found->second.connection.connected) {
        found = std::find_if(g_devices.begin(), g_devices.end(), [](const auto& entry) { return entry.second.connection.connected; });
    }
    if (found == g_devices.end()) return false;
    info->vendorId = found->second.snapshot.vendorId;
    info->productId = found->second.snapshot.productId;
    return true;
}

void ControllerInput_SetWindowHandle(void* handle) {
    std::lock_guard api(g_apiMutex);
    const auto window = static_cast<HWND>(handle);
    if (window == g_windowHandle) return;
    if (g_previousWindowProc && g_windowHandle) SetWindowLongPtr(g_windowHandle, GWLP_WNDPROC, reinterpret_cast<LONG_PTR>(g_previousWindowProc));
    g_previousWindowProc = nullptr; g_windowHandle = window;
    RAWINPUTDEVICE devices[2]{};
    for (int i = 0; i < 2; ++i) {
        devices[i].usUsagePage = 1; devices[i].usUsage = static_cast<USHORT>(4 + i);
        devices[i].dwFlags = window ? RIDEV_INPUTSINK : RIDEV_REMOVE;
        devices[i].hwndTarget = window;
    }
    if (!RegisterRawInputDevices(devices, 2, sizeof(RAWINPUTDEVICE))) OutputDebugStringA("ControllerInput: Raw Input registration FAILED\n");
    if (window) g_previousWindowProc = reinterpret_cast<WNDPROC>(SetWindowLongPtr(window, GWLP_WNDPROC, reinterpret_cast<LONG_PTR>(ControllerInput_WindowProc)));
}

uint32_t ControllerInput_GetApiVersion() { return 2; }

int32_t ControllerInput_CopyNavigationProjectionV2(const ControllerDeviceIdV2* deviceId,
    uint64_t generation, ControllerState* state) {
    if (!deviceId || !state) return E_INVALIDARG;
    *state = {};
    std::lock_guard api(g_apiMutex);
    std::lock_guard lock(g_captureMutex);
    if (!g_initialized) return E_UNEXPECTED;
    const auto found = g_devices.find(Key(*deviceId));
    if (found == g_devices.end() || !found->second.connection.connected ||
        found->second.connection.generation != generation) return GAMEINPUT_E_DEVICE_DISCONNECTED;
    if (!found->second.snapshot.readingAvailable) return GAMEINPUT_E_READING_NOT_FOUND;
    *state = found->second.navigationProjection;
    return S_OK;
}

int32_t ControllerInput_PollDevicesV2(ControllerDeviceSnapshotV2* devices, uint32_t capacity, uint32_t stride, uint32_t* required) {
    if (!required || stride != sizeof(ControllerDeviceSnapshotV2) || (!devices && capacity)) return E_INVALIDARG;
    std::lock_guard api(g_apiMutex);
    std::lock_guard lock(g_captureMutex);
    *required = static_cast<uint32_t>(g_devices.size());
    if (!g_initialized) return E_UNEXPECTED;
    if (capacity < *required) return HRESULT_FROM_WIN32(ERROR_INSUFFICIENT_BUFFER);
    if (!devices) return S_OK;
    uint32_t index = 0;
    for (auto& [key, capture] : g_devices) { PollCapture(capture); devices[index++] = capture.snapshot; }
    return S_OK;
}

int32_t ControllerInput_DrainEventsV2(ControllerCaptureEventV2* events, uint32_t capacity, uint32_t stride, uint32_t* written) {
    if (!written || stride != sizeof(ControllerCaptureEventV2) || (!events && capacity)) return E_INVALIDARG;
    std::lock_guard api(g_apiMutex);
    std::lock_guard lock(g_captureMutex);
    *written = 0;
    if (!g_initialized) return E_UNEXPECTED;
    while (*written < capacity && g_events.Pop(events[*written])) ++*written;
    return S_OK;
}

int32_t ControllerInput_GetDiagnosticsV2(ControllerCaptureDiagnosticsV2* diagnostics, uint32_t size) {
    if (!diagnostics || size != sizeof(*diagnostics)) return E_INVALIDARG;
    std::lock_guard api(g_apiMutex);
    std::lock_guard lock(g_captureMutex);
    *diagnostics = g_diagnostics;
    diagnostics->connectedDevices = static_cast<uint32_t>(std::count_if(g_devices.begin(), g_devices.end(), [](const auto& entry) { return entry.second.connection.connected; }));
    diagnostics->droppedEvents = g_events.dropped;
    return S_OK;
}

void ControllerInput_SetDiagnosticsEnabledV2(bool enabled) {
    std::lock_guard lock(g_captureMutex);
    g_diagnosticsEnabled = enabled;
}

void ControllerInput_Shutdown() {
    ControllerInput_SetWindowHandle(nullptr);
    std::lock_guard api(g_apiMutex);
    ShutdownCapture();
}
