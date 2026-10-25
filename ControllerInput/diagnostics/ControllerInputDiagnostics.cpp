#include <windows.h>
#include "../ControllerInput.h"
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <vector>
#include <chrono>

static bool closing = false;
static LRESULT CALLBACK DiagnosticWindowProc(HWND window, UINT message, WPARAM wParam, LPARAM lParam) {
    if (message == WM_CLOSE) { closing = true; return 0; }
    return DefWindowProcW(window, message, wParam, lParam);
}

static bool Check(bool condition, const char* name) {
    if (!condition) std::fprintf(stderr, "FAIL: %s\n", name);
    return condition;
}

static void PrintId(const ControllerDeviceIdV2& id) {
    for (const auto byte : id.bytes) std::printf("%02X", byte);
}

static void PrintEvent(const ControllerCaptureEventV2& event) {
    const auto& d = event.device;
    std::printf("seq=%llu kind=%u id=", event.sequence, event.kind);
    PrintId(d.deviceId);
    std::printf(" gen=%llu connected=%u available=%u vid=%04X pid=%04X time=%llu buttons=%08X pressed=%08X released=%08X guide=%u stick=(%.2f,%.2f) triggers=(%.2f,%.2f)\n",
        d.connectionGeneration, d.connected, d.readingAvailable, d.vendorId, d.productId, d.timestamp,
        d.state.buttons, d.pressedButtons, d.releasedButtons, d.guidePressed,
        d.state.leftStickX, d.state.leftStickY, d.state.leftTrigger, d.state.rightTrigger);
}

static bool Poll(std::vector<ControllerDeviceSnapshotV2>& devices) {
    uint32_t required = 0;
    auto hr = ControllerInput_PollDevicesV2(nullptr, 0, sizeof(ControllerDeviceSnapshotV2), &required);
    if (hr < 0 && hr != static_cast<int32_t>(HRESULT_FROM_WIN32(ERROR_INSUFFICIENT_BUFFER))) return false;
    // A hotplug between the count query and copy may require another attempt.
    for (unsigned attempt = 0; attempt < 4; ++attempt) {
        devices.resize(required ? required : 1);
        hr = ControllerInput_PollDevicesV2(devices.data(), static_cast<uint32_t>(devices.size()), sizeof(devices[0]), &required);
        if (hr == static_cast<int32_t>(HRESULT_FROM_WIN32(ERROR_INSUFFICIENT_BUFFER))) continue;
        if (hr < 0) return false;
        devices.resize(required);
        return true;
    }
    return false;
}

static int SmokeTests() {
    bool ok = true;
    ControllerInput_Shutdown();
    ControllerCaptureDiagnosticsV2 diagnostics{};
    uint32_t count = 99;
    ok &= Check(ControllerInput_GetApiVersion() == 2, "ABI version");
    ok &= Check(ControllerInput_PollDevicesV2(nullptr, 0, sizeof(ControllerDeviceSnapshotV2), &count) == E_UNEXPECTED, "poll before initialization");
    ok &= Check(!ControllerInput_GetState(nullptr) && !ControllerInput_GetDeviceInfo(nullptr), "legacy null pointers");
    ok &= Check(ControllerInput_GetDiagnosticsV2(nullptr, sizeof(diagnostics)) == E_INVALIDARG, "diagnostics null pointer");
    ok &= Check(ControllerInput_GetDiagnosticsV2(&diagnostics, sizeof(diagnostics) - 1) == E_INVALIDARG, "diagnostics ABI mismatch");
    ok &= Check(ControllerInput_PollDevicesV2(nullptr, 1, sizeof(ControllerDeviceSnapshotV2), &count) == E_INVALIDARG, "invalid snapshot buffer");
    ok &= Check(ControllerInput_DrainEventsV2(nullptr, 0, 1, &count) == E_INVALIDARG, "invalid event stride");
    for (int pass = 0; pass < 3; ++pass) {
        if (!Check(ControllerInput_Initialize(), "runtime initialization")) { ControllerInput_Shutdown(); return 1; }
        ok &= Check(ControllerInput_Initialize(), "idempotent initialization");
        ok &= Check(ControllerInput_GetDiagnosticsV2(&diagnostics, sizeof(diagnostics)) == S_OK, "diagnostics read");
        ok &= Check(diagnostics.gameInputApiVersion == 3 && diagnostics.initializationResult == S_OK &&
            diagnostics.deviceCallbackResult == S_OK && diagnostics.guideCallbackResult == S_OK, "v3 runtime and callback registration");
        std::vector<ControllerDeviceSnapshotV2> devices;
        ok &= Check(Poll(devices), "device snapshot with hotplug-safe sizing");
        for (const auto& device : devices) {
            ok &= Check(device.structSize == sizeof(device) && device.connectionGeneration != 0, "snapshot ABI/generation");
        }
        ControllerCaptureEventV2 events[16]{};
        ok &= Check(ControllerInput_DrainEventsV2(events, 16, sizeof(events[0]), &count) == S_OK && count <= 16, "bounded event drain");
        ControllerState state{};
        ControllerInput_GetState(&state); // Existing scalar entry point remains callable.
        ControllerDeviceInfo info{};
        ControllerInput_GetDeviceInfo(&info);
        std::printf("smoke pass %d: connected=%u known=%zu API=%u callbacks=(0x%08X,0x%08X)\n",
            pass + 1, diagnostics.connectedDevices, devices.size(), diagnostics.gameInputApiVersion,
            static_cast<unsigned>(diagnostics.deviceCallbackResult), static_cast<unsigned>(diagnostics.guideCallbackResult));
        ControllerInput_Shutdown();
        ControllerInput_Shutdown();
    }
    if (ok) std::puts("PASS: native ABI validation, runtime/callbacks, legacy exports, repeated initialization/shutdown");
    return ok ? 0 : 1;
}

int main(int argc, char** argv) {
    if (argc > 1 && std::strcmp(argv[1], "--smoke") == 0) return SmokeTests();
    const int seconds = argc > 1 ? std::atoi(argv[1]) : 30;
    if (seconds < 1 || seconds > 3600) { std::fprintf(stderr, "Usage: ControllerInputDiagnostics [seconds 1..3600 | --smoke]\n"); return 1; }
    const wchar_t* name = L"ControllerInputV2DiagnosticWindow";
    WNDCLASSW cls{}; cls.lpfnWndProc = DiagnosticWindowProc; cls.hInstance = GetModuleHandleW(nullptr); cls.lpszClassName = name;
    RegisterClassW(&cls);
    HWND window = CreateWindowW(name, L"ControllerInput v2: focus here; use two controllers; close to stop", WS_OVERLAPPEDWINDOW,
        CW_USEDEFAULT, CW_USEDEFAULT, 850, 180, nullptr, nullptr, cls.hInstance, nullptr);
    if (!window) return 1;
    ControllerInput_SetWindowHandle(window);
    ControllerInput_SetDiagnosticsEnabledV2(true);
    if (!ControllerInput_Initialize()) {
        std::fprintf(stderr, "Initialization failed. Run --smoke for runtime checks.\n");
        ControllerInput_Shutdown(); DestroyWindow(window); return 1;
    }
    ShowWindow(window, SW_SHOW);
    SetForegroundWindow(window);
    wchar_t runtime[MAX_PATH]{};
    if (GetModuleFileNameW(GetModuleHandleW(L"GameInputRedist.dll"), runtime, MAX_PATH)) std::printf("Runtime: %ls\n", runtime);
    std::printf("API v%u; Ctrl+C terminates; closing diagnostic window stops cleanly.\n", ControllerInput_GetApiVersion());
    const auto end = std::chrono::steady_clock::now() + std::chrono::seconds(seconds);
    bool ok = true;
    while (!closing && IsWindow(window) && std::chrono::steady_clock::now() < end) {
        MSG message{};
        while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE)) { TranslateMessage(&message); DispatchMessageW(&message); }
        std::vector<ControllerDeviceSnapshotV2> devices;
        if (!Poll(devices)) { ok = false; break; }
        ControllerCaptureEventV2 events[64]{};
        uint32_t count = 0;
        if (ControllerInput_DrainEventsV2(events, 64, sizeof(events[0]), &count) != S_OK) { ok = false; break; }
        for (uint32_t i = 0; i < count; ++i) PrintEvent(events[i]);
        Sleep(16);
    }
    ControllerCaptureDiagnosticsV2 diagnostics{};
    ControllerInput_GetDiagnosticsV2(&diagnostics, sizeof(diagnostics));
    std::printf("connected=%u dropped=%llu unattributedRawGuide=%llu historyResets=%llu\n",
        diagnostics.connectedDevices, diagnostics.droppedEvents, diagnostics.unattributedRawGuidePresses, diagnostics.historyResets);
    ControllerInput_Shutdown();
    if (IsWindow(window)) DestroyWindow(window);
    return ok ? 0 : 1;
}
