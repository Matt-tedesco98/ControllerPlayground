# ControllerPlayground

**A controller-first Windows gaming shell built for the couch.**

ControllerPlayground is an experimental Windows application designed to make PC gaming feel more like a console experience, without sacrificing the flexibility of a PC.

Built with **C#, WinUI 3, C++, and Microsoft GameInput**, the project explores a unified interface for game libraries, controller navigation, system settings, gaming services, and overlays.

> **Development status: Active prototype**
>
> ControllerPlayground is under active development and is not ready for general use. Features are implemented incrementally and validated on real hardware. The long-term goal is a unified, controller-native gaming experience across Windows and multiple PC gaming platforms.

## Vision

The goal is to create a Windows gaming experience where a keyboard and mouse are optional rather than required.

ControllerPlayground aims to provide:

- A unified, controller-friendly game library
- Seamless navigation using Xbox and PlayStation controllers
- Console-style overlays and system controls
- Integrated gaming services and launcher management
- Controller-native text entry
- Reliable game launching, session monitoring, and return-to-shell behavior
- A customizable, TV-friendly interface

The project prioritizes reusable components, native Windows integration, and maintainable architecture over rushing toward a release.

## Current Features

### Controller Input and Navigation

- Native Microsoft GameInput integration through a C++ bridge
- Xbox and DualSense controller support
- Full per-device controller identities
- D-pad and analog-stick navigation
- Held-input repeat handling
- Controller-family-specific button glyphs
- Shoulder-button tab navigation
- Guide/Home button handling
- Controller focus restoration
- Independent multi-controller device state
- Exclusive ownership of modal interactions
- Neutral-gated controller ownership takeover
- Disconnect and reconnect recovery

Xbox and DualSense multi-controller behavior passed the October 2026 Phase 4A validation milestone.

### Steam Integration

ControllerPlayground integrates with Steam using SteamKit2, local Steam data, and selected web services.

Implemented components include:

- Steam QR-code authentication
- Persistent authentication and reconnect handling
- Secure token storage using Windows Password Vault
- Steam-owned game discovery
- Library and installed-game filtering
- Game metadata and artwork handling
- Local SQLite data persistence
- Steam artwork caching
- Game launching and session monitoring
- Recently played game functionality
- Steam friends and chat foundations
- Steam news and game activity retrieval

Steam remains an active integration area. Not all Steam client capabilities are exposed through ControllerPlayground.

### Game Library and Interface

The WinUI 3 interface includes:

- Home screen with recent games and activity
- Game library with controller navigation
- Individual game detail pages
- Controller-accessible context menus
- Global Guide/Quick Actions overlay
- Friends and chat interfaces
- Controller prompt bar
- Dynamic controller glyphs

Game detail pages are organized into Activity, Your Stuff, Community, and Game Info sections. Some content and actions within these sections remain under development.

### Controller-Native Keyboard

ControllerPlayground includes a separate KeyboardHost process for controller-driven text entry.

The architecture supports:

- Controller-attributed keyboard sessions
- Shell-authorized input execution
- Controller-specific button glyphs
- Ownership validation and stale-input rejection
- Session lifecycle and protocol version negotiation
- Disconnect recovery and neutral-gated takeover
- Guarded focus restoration
- Retained legacy communication compatibility

The KeyboardHost v2 execution milestone passed 109 test groups in Debug and Release, along with Xbox and DualSense hardware validation.

See [KeyboardHost Phase 3 validation](tools/phase3/README.md) and [Phase 4A end-to-end verification](tools/phase4a/README.md).

### Windows System Controls

ControllerPlayground also provides controller-accessible Windows controls, including:

- Current audio output information
- Volume adjustment and mute controls
- Visual volume indication
- Audio settings navigation
- Power options for sleep, restart, and shutdown
- A controller-navigable power options panel

These features are part of the ongoing effort to reduce desktop interactions during couch gaming.

## Architecture

ControllerPlayground uses a combination of managed C# application components and native Windows integration.

```text
ControllerPlayground/
├── ControllerInput/
│   Native C++ GameInput bridge
├── ControllerPlayground.Input/
│   Managed input, ownership and protocols
├── ControllerPlayground.KeyboardHost/
│   Independent controller-native keyboard
├── ControllerPlayground.Input.Tests/
│   Input and protocol validation tests
├── Controls/
│   Reusable WinUI components
├── Models/
│   Application data models
├── Navigation/
│   Screen navigation
├── Overlays/
│   Guide, friends, chat and menus
├── Services/
│   Steam, game sessions, audio, power
│   SQLite and artwork caching
├── Views/
│   Home, library, game and settings UI
└── tools/
    Engineering validation resources
```

The architecture is designed to keep native input handling, application UI, system services, and supporting processes separated where practical.

## Technology Stack

| Technology | Purpose |
|---|---|
| C# / .NET 8 | Application and service logic |
| WinUI 3 / XAML | Windows user interface |
| Windows App SDK | Windows application framework |
| C++ | Native input integration |
| Microsoft GameInput | Controller input and device state |
| SteamKit2 | Steam client protocol integration |
| Steam Web API | Selected game data and news |
| SQLite | Local persistence |
| Windows Password Vault | Authentication token storage |
| Windows APIs | Audio, power and system integration |

## Experimental Research

Some development activities investigate lower-level platform behavior without yet delivering complete application features.

### Steam Controller HID Research

Ongoing research focuses on understanding controller device reports, input mapping, connection transports, and potential integration strategies.

This research is separate from the completed Xbox and DualSense multi-controller qualification. Steam Controller support is not yet considered production-ready.

### Steam Client and Frontend Investigation

ControllerPlayground is exploring Steam client and frontend interfaces to better understand how external launcher functionality may be integrated into a unified shell.

These investigations are experimental and do not imply that undocumented Steam functionality has been integrated into the application.

## Development Roadmap

Planned areas of development include:

- Deeper Steam client and launcher integration
- Epic Games and Xbox gaming platform discovery
- Further controller-native keyboard integration
- Improved display and audio device management
- Broader Windows system settings
- Expanded game-session and resume behavior
- More complete friends, chat and social functionality
- Discord integration
- GPU and Windows update visibility
- Further UI refinement for television displays
- Packaging, distribution and installation workflows

These are development goals, not commitments or released features.

## Validation and Engineering Documentation

The project follows an incremental development process, including automated testing and physical controller validation.

Completed milestones include:

- Native Xbox and DualSense input identification
- Multi-controller ownership and modal input isolation
- KeyboardHost v2 protocol and lifecycle transport
- Controller-authorized keyboard execution
- Phase 4A end-to-end integration regression testing

Detailed records:

- [Multi-controller architecture](ControllerPlayground.Input/MULTI_CONTROLLER.md)
- [KeyboardHost Phase 3 validation](tools/phase3/README.md)
- [Phase 4A integration validation](tools/phase4a/README.md)

Hardware validation and automated test results are scoped to the tested configurations. Broader device compatibility and distribution testing remain ongoing work.

## Development Environment

ControllerPlayground currently targets Windows x64.

The repository includes:

- `ControllerPlayground.slnx` — Visual Studio solution
- `ControllerPlayground.csproj` — Main WinUI application
- `ControllerInput/ControllerInput.vcxproj` — Native C++ controller bridge

Development uses Visual Studio with the relevant .NET, Windows App SDK, and C++ build tools.

The native `ControllerInput.dll` is copied into the application's build output as part of the configured build workflow.

The project does not currently provide a supported general-purpose installer or stable release.

## Why I Built It

PC gaming offers flexibility, performance, and customization, but navigating Windows from a couch can still require switching between a controller, keyboard, and mouse.

ControllerPlayground is an attempt to bridge that gap.

The project also serves as a practical software-engineering learning experience, exploring native interoperability, controller APIs, UI architecture, authentication, concurrency, Windows system services, and application integration.

The goal is not simply to create a gaming interface, but to build a better understanding of the systems that make one possible.

## Usage and Rights

ControllerPlayground is a personal portfolio and experimental development project.

The source code is publicly available for viewing and evaluation. No license is currently granted for copying, modifying, redistributing, or using the code in another project.

All rights are reserved by the author.

## Author

**Matthew Tedesco**

[GitHub](https://github.com/Matt-tedesco98)
