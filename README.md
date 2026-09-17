# ControllerPlayground

ControllerPlayground is an experimental **controller-first Windows gaming shell** built with **C#, WinUI 3, C++, and Microsoft GameInput**.

The project explores what a PC gaming interface could feel like if it were designed primarily for a couch, TV, and game controller rather than a mouse and keyboard. Its long-term goal is to bring console-style navigation, game management, social features, overlays, and Windows gaming controls into one cohesive experience.

> **Status:** Active prototype. Core navigation, controller input, Steam authentication, friends, and chat foundations are working, while the broader shell is still under development.

## Current Features

### Controller-first navigation

- Xbox and PlayStation controller-family detection
- D-pad and left-stick UI navigation
- Repeat handling for held directional input
- Controller-specific on-screen button glyphs
- Shoulder-button tab navigation
- Guide/Home button handling
- Menu/View button actions
- Focus restoration between screens and overlays

### Native GameInput integration

ControllerPlayground uses a small native **C++ DLL** for Microsoft GameInput rather than relying only on managed input abstractions.

The native layer currently handles:

- Gamepad state polling
- Connection status
- Vendor and product identification
- Xbox controller support
- DualSense detection
- DualSense PS-button handling through extra-button state
- Thumbsticks, triggers, face buttons, shoulders, and system-button mapping

### Controller-native UI

The WinUI 3 interface currently includes:

- Home/game-library prototype
- Game tiles
- Tile-specific context menus
- Global Guide menu
- Friends overlay
- Steam chat pane
- Controller prompt bar
- Dynamic Xbox / PlayStation glyphs
- Game detail pages
- Activity, Your Stuff, Community, and Game Info tabs

### Steam integration

The project currently combines several Steam integration approaches.

**SteamKit2**
- Steam connection and session management
- QR-code authentication
- Persistent authentication
- Secure refresh-token storage through Windows Password Vault
- Automatic reconnect handling
- Steam friend chat
- Incoming message handling
- Chat-history retrieval

**Local Steam data**
- Steam installation discovery through the Windows registry
- Active Steam user detection
- `localconfig.vdf` parsing
- Local friend-list discovery
- SteamID64 conversion
- Friend avatar lookup

**Steam Web API**
- Game news/activity retrieval using Steam app IDs

## Game Page Prototype

Game pages are structured around a controller-friendly tab system:

- **Activity** — game activity and Steam news
- **Your Stuff** — DLC and owned-content concepts
- **Community** — Community Hub, Discussions, and Guides
- **Game Info** — metadata, cover art, store, and support actions

The current home library still uses prototype game data while the service layer is being built out.

## Architecture

```text
ControllerPlayground/
├── Controls/          Reusable WinUI controller-focused components
├── Input/             Managed controller input and glyph mapping
├── ControllerInput/   Native C++ Microsoft GameInput bridge
├── Models/            Game, friend, chat, and activity models
├── Navigation/        Application screen definitions
├── Overlays/          Guide, friends, chat, QR login, and context menus
├── Services/          SteamKit, local Steam, VDF, and web services
└── Views/             Home, game, friends, settings, and activity views
```

## Tech Stack

- **C# / .NET 8**
- **WinUI 3**
- **Windows App SDK 2.3**
- **C++**
- **Microsoft GameInput**
- **SteamKit2**
- **Steam Web API**
- **Windows Password Vault**
- **QRCoder**
- **XAML**

## Project Direction

ControllerPlayground is being developed toward a more complete Windows couch-gaming experience. Planned areas include:

- Unified game-library discovery
- Deeper Steam and other launcher integration
- Global controller-first overlays
- Display and audio switching
- Controller-native text entry
- Windows and GPU update surfaces
- Better suspend/resume behavior
- Discord integration
- TV-style system controls and settings

These are roadmap goals and are not all implemented yet.

## Why I Built It

PC gaming is powerful, but much of Windows still assumes a desktop workflow. ControllerPlayground is an exploration of how Windows gaming could be made more approachable from a couch while preserving the flexibility of a PC.

The project also serves as a hands-on systems-development project covering native interop, controller input, Windows APIs, UI architecture, authentication, local application data, and network services.

## Development

The application targets Windows and currently builds around an x64 development workflow.

Main project:

```text
ControllerPlayground.csproj
```

Native controller bridge:

```text
ControllerInput/ControllerInput.vcxproj
```

The native `ControllerInput.dll` is copied into the WinUI application's output during the build.

## Usage and Rights

ControllerPlayground is a personal portfolio project. Its source code is publicly available for viewing and evaluation, but no license is granted for copying, modifying, redistributing, or using the code in another project. All rights are reserved by the author.

## Author

**Matthew Tedesco**
