#pragma once
#include "ControllerInput.h"
#include <array>
#include <cstring>
#include <deque>

namespace ControllerCapture {
using DeviceKey = std::array<uint8_t, 32>;

inline DeviceKey Key(const ControllerDeviceIdV2& id) {
    DeviceKey key{};
    std::memcpy(key.data(), id.bytes, key.size());
    return key;
}

// State shared by the three Guide sources, but never between devices.
enum GuideSource : uint32_t { Callback = 1, ExtraButton = 2, RawInput = 4 };
struct ConnectionState {
    uint64_t generation = 0;
    bool connected = false;
    uint32_t guideSources = 0;
    uint64_t guideTimestamp = 0;
    uint64_t statusTimestamp = 0;
    uint64_t connectedAt = 0;

    bool SetConnected(bool value, uint64_t timestamp = 0) {
        if (timestamp < statusTimestamp) return false;
        statusTimestamp = timestamp;
        if (connected == value) return false;
        connected = value;
        guideSources = 0;
        guideTimestamp = 0;
        if (value) { ++generation; connectedAt = timestamp; }
        return true;
    }
    bool SetGuide(uint32_t source, bool pressed, uint64_t timestamp) {
        if (!connected || timestamp < connectedAt || timestamp < guideTimestamp) return false;
        guideTimestamp = timestamp;
        const auto previous = guideSources;
        if (pressed) guideSources |= source;
        // These are observations of ONE physical Guide button. A newer release
        // clears older observations, including an extra-button reading frozen
        // when the application lost foreground access.
        else guideSources = 0;
        return previous == 0 && guideSources != 0;
    }
};

enum class LatestSample { Stale, Unchanged, New };
struct ReadingBaseline {
    ControllerState state{};
    uint64_t timestamp = 0;
    bool hasSample = false;

    LatestSample Classify(uint64_t sampleTimestamp, uint64_t connectionFloor) const {
        if (sampleTimestamp < connectionFloor || (hasSample && sampleTimestamp < timestamp)) return LatestSample::Stale;
        if (hasSample && sampleTimestamp == timestamp) return LatestSample::Unchanged;
        return LatestSample::New;
    }
    void Commit(ControllerState sample, uint64_t sampleTimestamp) {
        state = sample;
        timestamp = sampleTimestamp;
        hasSample = true;
    }
};

// Independent accumulators retain actual state separately from the legacy
// short-press/stick-flick projection. Never combine samples from different IDs.
class ReadingAccumulator {
public:
    explicit ReadingAccumulator(ControllerState initial) : actual(initial), strongest(initial) {}
    void Add(ControllerState next) {
        pressed |= next.buttons & ~actual.buttons;
        released |= actual.buttons & ~next.buttons;
        systemPressed |= next.systemButtons & ~actual.systemButtons;
        if (Magnitude(next) > Magnitude(strongest)) strongest = next;
        actual = next;
    }
    ControllerState ProjectLegacy() const {
        auto projected = actual;
        projected.buttons |= pressed;
        projected.systemButtons |= systemPressed;
        if (Magnitude(actual) <= 0.30f * 0.30f && Magnitude(strongest) >= 0.65f * 0.65f) {
            projected.leftStickX = strongest.leftStickX;
            projected.leftStickY = strongest.leftStickY;
        }
        return projected;
    }
    ControllerState actual{};
    uint32_t pressed = 0;
    uint32_t released = 0;
private:
    static float Magnitude(const ControllerState& s) { return s.leftStickX * s.leftStickX + s.leftStickY * s.leftStickY; }
    ControllerState strongest{};
    uint32_t systemPressed = 0;
};

// A diagnostic consumer cannot cause unbounded growth or block capture.
class EventQueue {
public:
    static constexpr size_t Capacity = 1024;
    void Push(ControllerCaptureEventV2 event) {
        event.structSize = sizeof(event);
        event.sequence = ++sequence;
        if (events.size() == Capacity) {
            events.pop_front();
            ++dropped;
        }
        events.push_back(event);
    }
    bool Pop(ControllerCaptureEventV2& event) {
        if (events.empty()) return false;
        event = events.front();
        events.pop_front();
        return true;
    }
    uint64_t dropped = 0;
private:
    uint64_t sequence = 0;
    std::deque<ControllerCaptureEventV2> events;
};
}
