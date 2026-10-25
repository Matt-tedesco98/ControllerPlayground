#include "../ControllerCaptureState.h"
#include <cstdio>
#include <cstdlib>

static void Check(bool condition, const char* name) {
    if (!condition) { std::fprintf(stderr, "FAIL: %s\n", name); std::exit(1); }
}

int main() {
    using namespace ControllerCapture;
    ControllerDeviceIdV2 first{}, second{};
    for (unsigned i = 0; i < 32; ++i) first.bytes[i] = second.bytes[i] = static_cast<uint8_t>(i);
    second.bytes[31] ^= 0x80;
    Check(Key(first) != Key(second), "IDs differing only in byte 32 remain distinct");
    Check(Key(first)[31] == 31 && Key(first)[0] == 0, "identity copies all bytes in order");

    ConnectionState a, b;
    Check(a.SetConnected(true) && a.generation == 1, "first connection");
    Check(!a.SetConnected(true) && a.generation == 1, "duplicate connection notification");
    b.SetConnected(true);
    Check(a.SetGuide(ExtraButton, true, 100), "first Guide edge");
    Check(!a.SetGuide(Callback, true, 101), "same physical Guide from second source is deduplicated");
    Check(b.SetGuide(Callback, true, 101), "other device Guide is independent");
    Check(!a.SetGuide(RawInput, false, 110), "background release clears stale foreground source");
    Check(!a.SetGuide(ExtraButton, true, 100), "old reading cannot replay a released Guide");
    Check(a.SetGuide(RawInput, true, 120), "next physical Guide press is accepted");
    Check(a.SetConnected(false), "disconnect tracked");
    Check(a.guideSources == 0 && b.connected && b.guideSources != 0, "disconnect resets only its own state");
    Check(!a.SetGuide(Callback, true, 130), "disconnected device cannot generate Guide");
    Check(a.SetConnected(true) && a.generation == 2, "reconnect gets fresh generation");
    Check(b.generation == 1, "other device generation unchanged");
    Check(!a.SetConnected(true), "repeated reconnect does not increment generation");
    ConnectionState timeline;
    timeline.SetConnected(true, 100);
    timeline.SetConnected(false, 200);
    timeline.SetConnected(true, 300);
    Check(!timeline.SetConnected(false, 250) && timeline.connected && timeline.generation == 2,
        "delayed disconnect from old generation cannot disconnect a reconnected endpoint");
    Check(!timeline.SetGuide(ExtraButton, true, 250), "pre-reconnect Guide reading cannot enter new generation");

    ReadingBaseline history;
    history.Commit({}, 100);
    for (int poll = 0; poll < 3000; ++poll)
        Check(history.Classify(100, 0) == LatestSample::Unchanged, "idle sample does not require history traversal or reset");
    ControllerState firstPress{}; firstPress.buttons = 4;
    ReadingAccumulator recovery(history.state);
    recovery.Add(firstPress);
    history.Commit(recovery.actual, 200);
    Check(recovery.pressed == 4, "expired history recovery compares latest sample against retained neutral baseline");
    Check(history.Classify(199, 0) == LatestSample::Stale, "older sample cannot roll accepted history backward");
    Check(history.Classify(200, 0) == LatestSample::Unchanged, "same sample does not replay press edge");
    Check(history.Classify(250, 300) == LatestSample::Stale, "pre-reconnect sample is rejected before baseline comparison");
    history = {}; // The same operation performed when the native cursor resets on reconnect.
    Check(history.Classify(250, 300) == LatestSample::Stale, "cleared cursor still rejects old generation sample");
    Check(history.Classify(350, 300) == LatestSample::New, "fresh post-reconnect sample accepted");
    ReadingAccumulator reconnected(history.state);
    reconnected.Add(firstPress);
    Check(reconnected.pressed == 4, "first fresh post-reconnect press is retained");
    history.Commit(reconnected.actual, 350);
    ReadingAccumulator release(history.state);
    release.Add({});
    Check(release.released == 4 && release.pressed == 0, "release after recovery uses accepted baseline");

    ReadingAccumulator inputA({}), inputB({});
    ControllerState accept{}; accept.buttons = 1;
    ControllerState back{}; back.buttons = 2;
    inputA.Add(accept); inputB.Add(back);
    inputA.Add({}); inputB.Add({});
    Check(inputA.pressed == 1 && inputB.pressed == 2, "simultaneous devices retain separate button transitions");
    Check(inputA.actual.buttons == 0 && inputA.released == 1, "actual held and released states are preserved");
    Check(inputA.ProjectLegacy().buttons == 1 && inputB.ProjectLegacy().buttons == 2, "short taps survive independently in legacy projection");
    ControllerState flick{}; flick.leftStickX = 0.8f;
    inputA.Add(flick); inputA.Add({});
    Check(inputA.actual.leftStickX == 0 && inputA.ProjectLegacy().leftStickX == 0.8f, "short stick flick survives projection without changing held state");
    Check(inputB.ProjectLegacy().leftStickX == 0, "stick flick cannot affect another device");

    EventQueue queue;
    for (size_t i = 0; i < EventQueue::Capacity + 3; ++i) {
        ControllerCaptureEventV2 event{};
        event.kind = ControllerCaptureInputV2;
        event.device.deviceId = first;
        event.device.connectionGeneration = 2;
        queue.Push(event);
    }
    Check(queue.dropped == 3, "bounded overflow is counted");
    ControllerCaptureEventV2 event{};
    Check(queue.Pop(event) && event.sequence == 4, "overflow drops oldest and preserves sequence gap");
    Check(event.structSize == sizeof(event) && Key(event.device.deviceId) == Key(first), "events preserve full identity and ABI size");
    Check(event.device.connectionGeneration == 2, "events retain connection generation");
    size_t remaining = 0;
    while (queue.Pop(event)) ++remaining;
    Check(remaining == EventQueue::Capacity - 1, "queue capacity and drain");
    std::puts("PASS: identity, lifecycle/reconnect, idle/history recovery, stale-generation rejection, Guide dedup, input transitions/legacy projection, bounded events");
}
