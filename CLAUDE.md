# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

VR-Workspace is a Unity-based VR application for remote desktop streaming to mobile VR headsets (Google Cardboard-compatible). It streams desktop content from a Windows server to an Android VR client over WebRTC.

## Build & Development

This is a Unity project - open in Unity Editor (not built via CLI). The project targets Android with Google Cardboard XR.

**Key Unity settings:**
- Target: Android
- XR: Google Cardboard (XR Plugin)
- Rendering: URP (Universal Render Pipeline)

## Architecture

### Core Systems

**1. Service Locator Pattern** (`Scripts/Core/ServiceLocator.cs`)
- Central dependency injection for ViewModels and Services
- Register: `ServiceLocator.Register<T>(service)`
- Retrieve: `ServiceLocator.Get<T>()`

**2. Observable Properties** (`Scripts/Core/ObservableProperty.cs`)
- Thread-safe property binding for MVVM pattern
- Auto-marshals to main thread via `MainThreadDispatcher`
- Usage: `property.OnChanged += handler`

**3. RTT (Render-to-Texture) UI System** (`Scripts/UI/RTT/`)
- All UI renders to RenderTexture, displayed on 3D quads
- `RTTManager` - singleton managing panels, themes, apps
- `RTTMenuFrame` - main UI container with glassmorphism effects
- `RTTCanvasBase` - base class for all RTT panels with dirty-flag optimization
- `RTTBootstrapper` - initializes RTT system on startup

**4. 3-Phase Streaming Protocol** (`Scripts/Streaming/`)
- `PhaseProtocolClient` - WebSocket + WebRTC client handling:
  - Phase 1: Hardware info exchange, speed test, config suggestion
  - Phase 2: Display config, ICE negotiation
  - Phase 3: Active streaming
- `ConnectionStateMachine` - state management with phase transitions
- `ConnectionViewModel` - observable wrapper for UI binding

### Key Component Relationships

```
APBootstrap (startup)
    └── AndroidStreamingHelper (WiFi/Wake locks)

RTTBootstrapper
    └── RTTMenu
        ├── RTTMenuFrame (main content)
        └── RTTMiniFrame + RTTTaskbar

RTTManager (singleton)
    ├── Panel registration/lifecycle
    ├── App lifecycle (open/close/switch)
    ├── Theme management
    └── Menu navigation

ConnectionViewModel
    └── PhaseProtocolClient
        ├── WebSocket signaling
        └── WebRTC peer connections (per monitor)

WorldPanelClusterRig
    └── WorldPanelPlus[] (curved panel array for multi-monitor)
```

MainScene uses one display/XR camera (`VRCamera`) for the virtual environment. RTT panels keep their own offscreen cameras with RenderTexture targets. Global camera passthrough and the RealWorld/VirtualSpace mode split are not part of the application architecture.

### Directory Structure

- `Scripts/Core/` - ServiceLocator, ObservableProperty, MainThreadDispatcher
- `Scripts/Streaming/` - PhaseProtocolClient, ConnectionStateMachine, SpeedTestClient
- `Scripts/ViewModels/` - ConnectionViewModel (MVVM binding layer)
- `Scripts/UI/RTT/Core/` - RTTManager, RTTCanvasBase, RTTConfig, RTTThemeConfig
- `Scripts/UI/RTT/Components/` - RTTMenuFrame, RTTTaskbar, RTTRemoteMenu, etc.
- `Scripts/UI/RTT/Controllers/` - RTTMainMenuController, RTTRemoteMenuController
- `Scripts/Panel/` - WorldPanelPlus, WorldPanelClusterRig, ClusterPanelVisual
- `Scripts/Input/` - VRGazeReticle (gaze-based interaction)
- `Scripts/Native/` - HevcDecoderPlugin, RtpDepacketizer (native interop)

### UI Factory Pattern

Use factory classes for consistent UI creation:
- `VRButtonFactory` - buttons with animations and ripple effects
- `VRDropdownFactory` - styled dropdowns
- `VRInputFieldFactory` - labeled input fields

## Connection Flow

1. User enters server IP in RTTRemoteMenu
2. `ConnectionViewModel.ConnectAsync(host, port)` initiates connection
3. Phase 1: Speed test runs, server sends hardware info + suggested config
4. Phase 2: User accepts config, ICE negotiation establishes WebRTC
5. Phase 3: Video streams to WorldPanelPlus textures via WebRTC

## Configuration Assets (Resources/)

- `RTTConfig` - resolution presets, memory limits, dirty-flag settings
- `RTTThemeConfig` - colors (primary/accent), glassmorphism colors
- `RTTAppRegistry` - app definitions with icons and types

## Namespaces

- `VRWorkspace.Core` - ServiceLocator, ObservableProperty, MainThreadDispatcher
- `VRWorkspace.Streaming` - PhaseProtocolClient, ConnectionStateMachine, StreamingMetrics
- `VRWorkspace.ViewModels` - ConnectionViewModel
- `VRWorkspace.Native` - HevcDecoderPlugin, RtpDepacketizer

## Transport Modes

- **WiFi**: Default mode, uses standard WebRTC ICE
- **USB Tethering**: `ConnectionViewModel.ConnectUSBAsync()` - filters ICE candidates to USB subnet only
