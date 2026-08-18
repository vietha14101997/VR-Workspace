# RTT UI System - Hướng Dẫn Sử Dụng

> **Phiên bản**: 1.0
> **Ngày tạo**: 2025-12-25
> **Tác giả**: VR Workspace Team

---

## Mục Lục

1. [Giới Thiệu](#1-giới-thiệu)
2. [Cấu Trúc Thư Mục](#2-cấu-trúc-thư-mục)
3. [Các Component Chính](#3-các-component-chính)
4. [Hướng Dẫn Cài Đặt](#4-hướng-dẫn-cài-đặt)
5. [Cách Sử Dụng](#5-cách-sử-dụng)
6. [Tùy Chỉnh](#6-tùy-chỉnh)
7. [Debug & Troubleshooting](#7-debug--troubleshooting)
8. [API Reference](#8-api-reference)

---

## 1. Giới Thiệu

### RTT là gì?

**RTT (Render-to-Texture)** là kỹ thuật render UI lên RenderTexture thay vì render trực tiếp vào camera chính. Texture này sau đó được hiển thị trên một Quad trong không gian 3D.

### Lợi ích

| Tính năng | World Space UI (Cũ) | RTT UI (Mới) |
|-----------|---------------------|--------------|
| **Performance** | Render mỗi frame | Chỉ render khi thay đổi (Dirty Flag) |
| **Raycast** | Physics raycast phức tạp | UV-based raycast đơn giản |
| **Resolution** | Phụ thuộc camera | Độc lập, có thể scale |
| **Memory** | Nhiều draw calls | Ít draw calls hơn |
| **Glassmorphism** | GrabPass phức tạp | GrabPass tối ưu |

---

## 2. Cấu Trúc Thư Mục

```
Scripts/UI/RTT/
├── Core/
│   ├── RTTCanvasBase.cs      # Base class cho tất cả RTT panels
│   ├── RTTConfig.cs          # ScriptableObject cấu hình
│   ├── RTTManager.cs         # Singleton quản lý panels
│   └── RTTFeatureToggle.cs   # Bật/tắt RTT system
│
├── Components/
│   ├── RTTMenuFrame.cs       # Main menu panel
│   ├── RTTTaskbar.cs         # Taskbar panel
│   └── RTTMobileKeyboard.cs  # Virtual keyboard
│
├── Input/
│   └── RTTRaycastManager.cs  # Xử lý raycast & input
│
├── Effects/
│   └── (Future: RTT-specific effects)
│
└── Debug/
    ├── RTTDebugOverlay.cs    # Performance overlay (F12)
    └── RTTGizmoDrawer.cs     # Scene view gizmos
```

---

## 3. Các Component Chính

### 3.1 RTTCanvasBase (Abstract)

Base class cho tất cả RTT panels. Quản lý:
- RenderTexture lifecycle
- UI Camera setup
- Canvas configuration
- Display Quad với Material
- BoxCollider cho raycast
- Dirty Flag optimization

### 3.2 RTTManager (Singleton)

Quản lý toàn bộ RTT system:
- Đăng ký/hủy đăng ký panels
- Phân bổ camera depth
- Theo dõi memory usage
- Performance statistics

### 3.3 RTTRaycastManager (Singleton)

Xử lý raycast từ VR controllers/gaze:
- Chuyển đổi world-space ray → canvas-space position
- UV to screen coordinate conversion
- Integration với VRGazeReticle

### 3.4 RTTFeatureToggle (Static)

Bật/tắt RTT system:
- Lưu trữ trong PlayerPrefs
- Cho phép A/B testing
- Fallback về legacy UI

---

## 4. Hướng Dẫn Cài Đặt

### Bước 1: Tạo RTTConfig Asset

```
1. Right-click trong Project window
2. Create > ScriptableObject > RTTConfig
3. Đặt tên: RTTConfig
4. Di chuyển vào thư mục Resources/
```

### Bước 2: Cấu Hình RTTConfig

```csharp
// Trong Inspector của RTTConfig.asset:
Default Width: 1920
Default Height: 1080
Anti Aliasing: 4
Format: ARGB32
Filter Mode: Bilinear

// Quality Presets:
- Low: 1280x720, AA=2
- Medium: 1600x900, AA=4
- High: 1920x1080, AA=4

// Performance:
Use Dirty Flag: true
Max Frame Skip: 7 // Khoảng 10 lần render/giây ở 72 Hz; 0 = chỉ render khi dirty

// Giá trị 1-6 được runtime clamp lên 7 để tránh RTT render quá dày trên mobile.
// Nội dung cần cập nhật liên tục phải dùng SetContinuousRender(true) hoặc MarkDirty().

// Debug:
Show Debug Gizmos: true (dev) / false (prod)
Log Performance: false
```

### Bước 3: Thêm RTTManager vào Scene

```
1. Tạo empty GameObject: "RTTManager"
2. Add Component: RTTManager
3. Assign RTTConfig reference
4. Set Starting Camera Depth: -100
```

### Bước 4: Thêm RTTRaycastManager

```
1. Tạo empty GameObject: "RTTRaycastManager"
2. Add Component: RTTRaycastManager
3. Set Layer Mask: VirtualObjects (hoặc layer của RTT quads)
```

### Bước 5: Tạo RTT Panels

```
1. Tạo empty GameObject: "RTTMenuFrame"
2. Add Component: RTTMenuFrame
3. Cấu hình trong Inspector:
   - World Width: 1.6 (meters)
   - World Height: 0.9 (meters)
   - Is Primary: true
4. Position panel trong scene
```

---

## 5. Cách Sử Dụng

### 5.1 Bật/Tắt RTT System

```csharp
// Bật RTT
RTTFeatureToggle.UseRTT = true;

// Tắt RTT (fallback về legacy)
RTTFeatureToggle.UseRTT = false;

// Toggle
RTTFeatureToggle.Toggle();

// Lắng nghe thay đổi
RTTFeatureToggle.OnRTTToggled += (enabled) => {
    Debug.Log($"RTT is now: {enabled}");
};
```

### 5.2 Truy Cập RTT Panels

```csharp
// Lấy primary menu frame
RTTMenuFrame menu = RTTMenuFrame.PrimaryInstance;

// Lấy taskbar
RTTTaskbar taskbar = RTTTaskbar.Instance;

// Lấy keyboard
RTTMobileKeyboard keyboard = RTTMobileKeyboard.Instance;

// Lấy tất cả registered panels
var panels = RTTManager.Instance.GetRegisteredPanels();
```

### 5.3 Show/Hide Panels

```csharp
// Show panel
panel.Show();

// Hide panel
panel.Hide();

// Toggle visibility
panel.SetVisible(!panel.IsVisible);

// Check visibility
if (panel.IsVisible) { ... }
```

### 5.4 Force Re-render

```csharp
// Mark panel as dirty (sẽ render frame tiếp theo)
panel.MarkDirty();

// Force immediate render
panel.ForceRender();
```

### 5.5 Sử Dụng Keyboard

```csharp
// Show keyboard cho input field
RTTMobileKeyboard.Instance.Show(inputField);

// Hide keyboard
RTTMobileKeyboard.Instance.Hide();

// Lắng nghe events
keyboard.OnKeyPressed += (key) => Debug.Log($"Key: {key}");
keyboard.OnEnterPressed += () => Debug.Log("Enter pressed");
keyboard.OnClosePressed += () => Debug.Log("Closed");
```

### 5.6 Raycast Integration

```csharp
// Manual raycast
Ray ray = new Ray(origin, direction);
RTTHitResult? hit = RTTRaycastManager.Instance.Raycast(ray);

if (hit.HasValue)
{
    RTTCanvasBase panel = hit.Value.panel;
    Vector2 screenPos = hit.Value.screenPosition;
    Vector2 uv = hit.Value.uv;

    // Simulate pointer event
    // ...
}
```

---

## 6. Tùy Chỉnh

### 6.1 Tạo Custom RTT Panel

```csharp
public class MyCustomPanel : RTTCanvasBase
{
    // Override resolution
    protected override Vector2Int GetResolution()
    {
        return new Vector2Int(1280, 720);
    }

    // Override camera depth
    protected override int GetCameraDepth()
    {
        return -50; // Higher = render later
    }

    // Build UI content
    protected override void BuildUI()
    {
        // Create UI elements here
        CreateButton();
        CreateText();
        // ...
    }

    private void CreateButton()
    {
        // Use VRButtonFactory to create buttons
        var btn = VRButtonFactory.CreateTextButton(
            _canvas.transform,
            width: 200,
            height: 60,
            label: "Click Me",
            color: Color.cyan,
            onClick: () => Debug.Log("Clicked!")
        );
    }
}
```

### 6.2 Tùy Chỉnh Theme

```csharp
// Trong RTTMenuFrame hoặc custom panel:
[Header("Glass Effect")]
[SerializeField] private Color glassColorA = new Color(0f, 0.1f, 0.15f, 0.85f);
[SerializeField] private Color glassColorB = new Color(0.05f, 0.15f, 0.2f, 0.75f);

[Header("Border")]
[SerializeField] private Color glowColorA = new Color(0f, 1.5f, 2f);
[SerializeField] private Color glowColorB = new Color(1.2f, 0.3f, 2f);
```

### 6.3 Sử Dụng Factory Methods

```csharp
// Button với icon
var btn = VRButtonFactory.CreateIconButton(
    parent: container,
    size: 80,
    icon: mySprite,
    color: Color.cyan,
    onClick: () => { }
);

// Dropdown
var dropdown = VRDropdownFactory.CreateIconDropdown(
    parent: container,
    width: 300,
    label: "Quality",
    icon: settingsIcon,
    color: Color.cyan,
    options: new List<string> { "Low", "Medium", "High" },
    defaultIndex: 1,
    onValueChanged: (index, value) => { }
);

// Input Field
var input = VRInputFieldFactory.CreateLabeledInputField(
    parent: container,
    width: 400,
    label: "Server IP",
    defaultValue: "192.168.1.1",
    color: Color.cyan,
    onEndEdit: (value) => { }
);
```

---

## 7. Debug & Troubleshooting

### 7.1 Debug Overlay

Nhấn **F12** để toggle RTTDebugOverlay hiển thị:
- Total/Visible panels count
- Texture memory usage (MB)
- Current quality level
- FPS counter

### 7.2 Scene Gizmos

Thêm `RTTGizmoDrawer` vào scene để hiển thị:
- Quad bounds (cyan)
- Raycast rays (green = hit, gray = miss)
- UV hit points (red spheres)

### 7.3 Common Issues

#### Panel không hiển thị
```
1. Kiểm tra RTTManager đã được khởi tạo
2. Kiểm tra panel.IsVisible = true
3. Kiểm tra Camera Main không null
4. Kiểm tra quad material có RenderTexture
```

#### Raycast không hoạt động
```
1. Kiểm tra BoxCollider trên Display Quad
2. Kiểm tra Layer Mask trong RTTRaycastManager
3. Kiểm tra VRGazeReticle.useRTTRaycast = true
```

#### UI không cập nhật
```
1. Gọi panel.MarkDirty() sau khi thay đổi UI
2. Kiểm tra Dirty Flag không bị skip quá nhiều frames
3. Kiểm tra UI Camera đang enabled
```

#### Memory cao
```
1. Giảm resolution trong RTTConfig
2. Giảm Anti-Aliasing (2 thay vì 4)
3. Ẩn panels không sử dụng
4. Kiểm tra RTTManager.GetTextureMemoryMB()
```

### 7.4 Performance Tips

```csharp
// 1. Chỉ MarkDirty khi thực sự cần
if (textChanged)
    panel.MarkDirty();

// 2. Batch UI updates
StartCoroutine(BatchedUpdate());
IEnumerator BatchedUpdate()
{
    UpdateElement1();
    UpdateElement2();
    yield return null; // Wait 1 frame
    panel.MarkDirty(); // Single render
}

// 3. Ẩn panels không nhìn thấy
panel.SetVisible(IsInViewFrustum(panel));

// 4. Sử dụng quality presets phù hợp
RTTManager.Instance.SetQualityLevel(RTTQualityLevel.Medium);
```

---

## 8. API Reference

### RTTCanvasBase

| Property | Type | Description |
|----------|------|-------------|
| `IsVisible` | bool | Panel visibility state |
| `IsDirty` | bool | Needs re-render flag |
| `RenderTexture` | RenderTexture | Current render texture |
| `DisplayQuad` | MeshRenderer | 3D display quad |

| Method | Description |
|--------|-------------|
| `Show()` | Show the panel |
| `Hide()` | Hide the panel |
| `SetVisible(bool)` | Set visibility |
| `MarkDirty()` | Request re-render |
| `ForceRender()` | Immediate render |
| `GetWorldSize()` | Get world dimensions |
| `GetDisplayQuad()` | Get quad renderer |

### RTTManager

| Property | Type | Description |
|----------|------|-------------|
| `Instance` | RTTManager | Singleton instance |
| `PanelCount` | int | Total registered panels |
| `VisiblePanelCount` | int | Currently visible |

| Method | Description |
|--------|-------------|
| `RegisterPanel(panel)` | Register a panel |
| `UnregisterPanel(panel)` | Unregister a panel |
| `GetRegisteredPanels()` | Get all panels |
| `GetTextureMemoryMB()` | Get memory usage |
| `SetQualityLevel(level)` | Set quality preset |

### RTTRaycastManager

| Property | Type | Description |
|----------|------|-------------|
| `Instance` | RTTRaycastManager | Singleton instance |
| `LastHit` | RTTHitResult? | Last raycast result |

| Method | Description |
|--------|-------------|
| `Raycast(ray)` | Perform raycast |
| `GetPanelAtScreenPoint(pos)` | Get panel at screen pos |

### RTTFeatureToggle

| Property | Type | Description |
|----------|------|-------------|
| `UseRTT` | bool | RTT enabled state |

| Method | Description |
|--------|-------------|
| `Toggle()` | Toggle RTT on/off |
| `ResetToDefault()` | Reset to default |
| `ClearCache()` | Clear cached value |

| Event | Description |
|-------|-------------|
| `OnRTTToggled` | Fired when toggled |

---

## Changelog

### v1.0.0 (2025-12-25)
- Initial RTT system implementation
- RTTCanvasBase, RTTManager, RTTRaycastManager
- RTTMenuFrame, RTTTaskbar, RTTMobileKeyboard
- RTTDebugOverlay, RTTGizmoDrawer
- RTTFeatureToggle for A/B testing
- Factory compatibility (VRButtonFactory, VRDropdownFactory, VRInputFieldFactory)
- VRGazeReticle integration

---

*Tài liệu này được tạo tự động. Vui lòng báo cáo lỗi hoặc đề xuất cải tiến.*
