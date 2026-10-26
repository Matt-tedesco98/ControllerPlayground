# Increment 4 Phase 4A — Xbox and PS5 DualSense regression verification

Scope approved October 26, 2026. Baseline:
`f388c8f3893597239b4bdd5dc291ceb02dc81648` (accepted Increment 4 Phase 3).

## Scope and boundaries

Verify Xbox and PS5 DualSense only, separately and together with both ownership
directions. Steam Controller-specific implementation and qualification are deferred
to a future milestone and are not completion blockers for this milestone.

Exercise Home, Library, Game Details, Guide, Friends, Chat and KeyboardHost.
Preserve production architecture, native/managed mappings, legacy compatibility,
GameInput ExclusiveForegroundInput + EnableBackgroundGuideButton policy, keyboard
layouts/editing and Steam settings. Steam Input Desktop Layout keyboard/mouse
injection remains a known compatibility issue. Record the existing configuration;
do not develop a workaround or reconfigure it as part of verification.

Windows x64 packaging verification requires separate approval after Phase 4A
review. The user subsequently approved completing and committing multi-controller
documentation; see [the consolidated architecture](../../ControllerPlayground.Input/MULTI_CONTROLLER.md).
Development-build test execution is not packaging
qualification. Do not fix ReadyToRun, refactor production code, remove legacy paths,
or start later work automatically. This document separates automated evidence
from user-performed hardware results and records coverage limits explicitly.

## Regression matrix

Run hardware rows once with Xbox initiating/owning and once with DualSense.
Release all controls after transitions. A fresh recovery Accept claims without
typing/activating; subsequent input operates the recovered interaction.

| ID | Surface / scenario | Expected result | Phase 4A status |
|---|---|---|---|
| A1 | Full Debug and Release console suites | Input, routing, protocol, lifecycle and mediated groups pass | Passed: 109 groups each; phase4a-tests-debug.log / phase4a-tests-release.log |
| A2 | Protocol/session tests with JSON reflection disabled | All 28 groups pass; restore normal test runtime configuration afterward | Passed: 28; normal Release runtime setting rebuilt afterward |
| H1 | Home and Library: short D-pad taps, stick flicks/diagonals, held directions, neutral, alternating controllers | Single steps, controlled existing repeats, no skipped tiles or duplicate activation | Passed: user reported all requested navigation checks pass with both controllers |
| H2 | Home/Library context menus, including simultaneous owner/non-owner input and held controls across close | Exclusive modal ownership; Back never becomes Accept or launches a game; no fallthrough | Passed: user confirmed owner isolation, held transitions and no accidental launch |
| H3 | Game Details tabs, Support/Known Issues, return to originating tile | Existing mappings, nested focus and scroll behavior preserved | Passed: user reported requested available surfaces work as intended |
| H4 | Guide over Home/Library and Friends/Chat; repeated Guide/PS; non-owner Guide/View | One toggle per press, exclusive owner, no underlying command; correct focus on unwind | Passed: user confirmed requested Guide isolation and nested focus checks work as intended |
| H5 | Friends selection and Chat; controller- and mouse-opened interactions | Correct owner/claim behavior; shared root navigation resumes after close | Passed: user reported requested Friends/Chat checks work |
| H6 | Keyboard: owner typing, repeats, layouts, Shift/Caps, X/backspace, Y/space, movement, Menu/commit and B/cancel | Exact existing behavior, correct owner glyphs; commit only updates original draft; never Send during test | Passed: user reported requested keyboard behavior works with each owner |
| H7 | RT/R2 + Accept and RT/R2 + direction; simultaneous non-owner buttons including Guide/View | Modifier retained; no lost/doubled typing; non-owner cannot modify or terminate session or escape to shell | Passed: user reported simultaneous-input and non-owner rejection checks work |
| H8 | Disconnect owner and non-owner; held-input reconnect; reverse takeover directions | Owner loss suspends; non-owner loss does not interrupt; neutral + fresh claim required; no old generation/replay | Passed for requested owner/held-reconnect/takeover checks; non-owner-only unplug not separately attested |
| H9 | Commit/cancel, nested modal close, Alt-Tab during held input, change/close original chat | Correct valid-context focus and draft; no focus theft, stale callbacks or text applied to another chat | Passed: user reported requested focus/context recovery checks work |
| H10 | Launch selected installed game, resume through shell/Guide, return after game exit | Existing gameplay and foreground behavior; correct shell focus; no new game-wide suppression or duplicate launch | Passed: user reported Phasmophobia launch, Guide/resume, gameplay and exit-to-shell checks work |
| H11 | Repeated keyboard opening/closing; cancel during delayed work; helper close/termination mid-draft and reopen | Bounded cleanup, no implicit commit, stale callbacks rejected, successful fresh session | Passed: user reported requested lifecycle checks work; forced callback delays covered by automated tests |
| H12 | Shell close/restart during helper session, where safely testable | No orphan controller authorization; no stale text/commands carried into restarted shell | Passed as user-reported requested safe restart/recovery checks |

Record transport (USB/Bluetooth as actually tested), controller identities and
generations, owner/glyph changes, resulting UI command, focus target and failures.
Do not claim untested transport variants. Normal runs should not drop input events;
intentional overflow tests must instead demonstrate safe resynchronization.
Do not log chat contents or send disposable drafts.

## Evidence and completion

The user selected Phasmophobia for H10 and reported Steam Input disabled. The
initial managed/native ABI smoke passed with zero drops and one PS5 DualSense
observed; Xbox was not observed in that short probe. Subsequent user-performed
checks exercised both controllers; the smoke alone is not two-controller evidence.
Log: `phase4a-native-smoke.log` (ignored artifact).
No deployment or packaging changes were performed; hardware checks use the
previously deployed, accepted Phase 3 shell/helper pair.

On October 26 the user reported both-controller Home/Library navigation, context
menus, held transitions, repeated Back and no accidental launch all passed. The
requested Game Details/available nested surfaces and Guide tests over Home/Library
and Friends/Chat, including non-owner Guide/View and nested focus return, were
reported to work as intended. These are user-performed hardware results, not
automated WinUI assertions.

The user subsequently reported all three remaining requested test batches work:
Friends/Chat and keyboard behavior with each owner, simultaneous modifiers and
non-owner rejection, held reconnect and takeover; focus/context restoration and
helper/safe-shell-restart recovery; and Phasmophobia launch, Guide/resume, gameplay
input and exit-to-shell navigation with each controller in turn. No failures or
untested requested items were reported. These results are user attestations, not
independent automated observations of every UI step.

Coverage limits: physical connection transports were not recorded, a non-owner-only
unplug was not separately attested, and forced delayed-callback timing was exercised
by automated tests rather than a new hardware fault-injection harness. No claim is
made about other games, transport variants, signed/release packages or controller
families. Steam Controller qualification is deferred and is not a blocker.

Prior Phase 3 hardware acceptance remains valid historical evidence but does not
automatically mark every Phase 4A surface or game scenario passed. Automated
tests use simulated UI callbacks and do not prove real WinUI focus/layout behavior.

## Phase 4A review status — October 26, 2026

Verification is complete for the agreed Xbox + PS5 DualSense checks, with the
coverage limits above disclosed for review. Both 109-group suites, all 28
reflection-free protocol/session groups and the native ABI smoke passed. The
reported hardware checks found no regressions. Production source, architecture,
GameInput policies, legacy compatibility and Steam settings were not changed.

During verification, only the roadmap scope/status note and this record were
edited; production source and deployments were unchanged. The user subsequently
authorized final multi-controller documentation and its Git commit. That
documentation consolidates the architecture, historical boundaries, acceptance
and remaining limits without expanding hardware claims.

No packaging verification, ReadyToRun fix or Steam Controller implementation is
included. Windows x64 packaging verification remains a separately approved step.
