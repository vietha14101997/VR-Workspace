# Kế Hoạch Chi Tiết: Chuyển Đổi VR UI sang Cấu Trúc Render-to-Texture

## 📋 Tổng Quan

Dự án VR-Workspace hiện tại sử dụng **World Space Canvas** cho tất cả UI elements. Chuyển đổi sang **Render-to-Texture (RTT)** UI sẽ mang lại:
- Hiệu năng tốt hơn khi render UI phức tạp
- Khả năng áp dụng post-processing effects lên UI
- Dễ dàng quản lý độ phân giải UI độc lập với scene rendering
- Hỗ trợ tốt hơn cho gaze interaction trong VR

---

## 📊 Phân Tích Cấu Trúc Hiện Tại

### 1. Các UI Components Chính

| Component | File | Dòng Code | Chức Năng | Độ Phức Tạp |
|-----------|------|-----------|-----------|-------------|
| **VRMenuFrame** | [VRMenuFrame.cs](file:///C:/Users/super/OneDrive/Documents/VRWorkspaceProjects/VRWorkSpace/Assets/VR-Workspace/Scripts/UI/Menu/VRMenuFrame.cs) | 1052 | Frame chính với glass effect, border glow | ⭐⭐⭐⭐⭐ |
| **VRMainMenu** | [VRMainMenu.cs](file:///C:/Users/super/OneDrive/Documents/VRWorkspaceProjects/VRWorkSpace/Assets/VR-Workspace/Scripts/UI/Menu/VRMainMenu.cs) | 657 | Menu 6 nút với grid layout | ⭐⭐⭐⭐ |
| **VRRemoteMenu** | [VRRemoteMenu.cs](file:///C:/Users/super/OneDrive/Documents/VRWorkspaceProjects/VRWorkSpace/Assets/VR-Workspace/Scripts/UI/Menu/VRRemoteMenu.cs) | 571 | Form kết nối Remote Desktop | ⭐⭐⭐⭐ |
| **VRTaskbar** | [VRTaskbar.cs](file:///C:/Users/super/OneDrive/Documents/VRWorkspaceProjects/VRWorkSpace/Assets/VR-Workspace/Scripts/Taskbar/VRTaskbar.cs) | 2268 | Taskbar với clock, battery, buttons | ⭐⭐⭐⭐⭐ |
| **VRMobileKeyboard** | [VRMobileKeyboard.cs](file:///C:/Users/super/OneDrive/Documents/VRWorkspaceProjects/VRWorkSpace/Assets/VR-Workspace/Scripts/Keyboard/VRMobileKeyboard.cs) | 1794 | Bàn phím mobile-style | ⭐⭐⭐⭐⭐ |
| **VRKeyboard** | [VRKeyboard.cs](file:///C:/Users/super/OneDrive/Documents/VRWorkspaceProjects/VRWorkSpace/Assets/VR-Workspace/Scripts/Keyboard/VRKeyboard.cs) | 1195 | Bàn phím full-size | ⭐⭐⭐⭐ |
| **QRScannerManager** | [QRScannerManager.cs](file:///C:/Users/super/OneDrive/Documents/VRWorkspaceProjects/VRWorkSpace/Assets/VR-Workspace/Scripts/QRScanner/QRScannerManager.cs) | 1073 | Full-screen QR scanning UI | ⭐⭐⭐⭐ |

### 2. UI Factory Components

| Component | File | Chức Năng |
|-----------|------|-----------|
| **VRButtonFactory** | [VRButtonFactory.cs](file:///C:/Users/super/OneDrive/Documents/VRWorkspaceProjects/VRWorkSpace/Assets/VR-Workspace/Scripts/UI/Components/VRButtonFactory.cs) | Tạo buttons với glass, border, icon glow |
| **VRDropdownFactory** | [VRDropdownFactory.cs](file:///C:/Users/super/OneDrive/Documents/VRWorkspaceProjects/VRWorkSpace/Assets/VR-Workspace/Scripts/UI/Components/VRDropdownFactory.cs) | Tạo dropdowns với panel popup |
| **VRInputFieldFactory** | [VRInputFieldFactory.cs](file:///C:/Users/super/OneDrive/Documents/VRWorkspaceProjects/VRWorkSpace/Assets/VR-Workspace/Scripts/UI/Components/VRInputFieldFactory.cs) | Tạo input fields với VRKeyboard integration |
| **VRButtonAnimation** | [VRButtonAnimation.cs](file:///C:/Users/super/OneDrive/Documents/VRWorkspaceProjects/VRWorkSpace/Assets/VR-Workspace/Scripts/UI/Components/VRButtonAnimation.cs) | Hover/press animations cho buttons |

### 3. Input & Interaction

| Component | File | Chức Năng |
|-----------|------|-----------|
| **VRGazeReticle** | [VRGazeReticle.cs](file:///C:/Users/super/OneDrive/Documents/VRWorkspaceProjects/VRWorkSpace/Assets/VR-Workspace/Scripts/Input/VRGazeReticle.cs) | Gaze-based interaction với dwell timer |
| **VRKeyboardManager** | [VRKeyboardManager.cs](file:///C:/Users/super/OneDrive/Documents/VRWorkspaceProjects/VRWorkSpace/Assets/VR-Workspace/Scripts/Keyboard/VRKeyboardManager.cs) | Quản lý keyboard visibility |
| **GazeDwellToggle** | [GazeDwellToggle.cs](file:///C:/Users/super/OneDrive/Documents/VRWorkspaceProjects/VRWorkSpace/Assets/VR-Workspace/Scripts/Input/GazeDwellToggle.cs) | Toggle mode bằng gaze dwell |

### 4. Shaders Đang Sử Dụng

```
📁 VR-Workspace/Shaders/
├── GlassGradientBackground.shader      ← Background với gradient glass effect
├── GlassGradientBackgroundOverlay.shader
├── GlassNoiseBackground.shader
├── GlowingGlassBorder.shader           ← Viền glow với shimmer animation
├── GlowingElementBorder.shader         ← Viền cho buttons/elements
├── GlowingConnectButton.shader         ← Button đặc biệt với gradient+shimmer
├── GlowingHorizontalLine.shader        ← Separator lines
├── ClusterBorderGlow.shader
├── UIBlurBackground.shader             ← Blur effect (glassmorphism)
├── RoundedRawImage.shader
├── WorldPanelBoard.shader              ← Streaming panel
├── WorldPanelCursor.shader
├── WorldPanelDash.shader
├── WorldPanelDock.shader
└── WorldPanelRounded.shader
```

### 5. Các Thông Số Cần Bảo Toàn

#### A. Kích Thước & Tỉ Lệ

| Component | Kích Thước (meters) | Logical Size (pixels) | Scale Factor |
|-----------|---------------------|----------------------|--------------|
| VRMenuFrame | 1.6m × 0.9m | 1920 × 1080 (calc) | 0.000833 |
| VRTaskbar | `dynamicWidth` × 80px (logical) | width phụ thuộc VRMenuFrame | Giữ nguyên |
| VRMobileKeyboard | Tự tính theo content | 364px (dynamicHeight) | Theo VRMenuFrame |
| VRKeyboard | Tự tính theo content | Full QWERTY layout | Theo VRMenuFrame |

#### B. Vị Trí Tương Quan

| Thành Phần | Vị Trí | Mối Quan Hệ |
|------------|--------|-------------|
| VRTaskbar | Dưới VRMenuFrame | `posY = menuFrame.posY - (menuFrame.height/2) - spacing` |
| VRKeyboard | Dưới VRTaskbar | `posY = taskbar.posY - offset` |
| QRScannerManager | Overlay toàn màn hình | Thay thế tạm thời VRMenuFrame+VRTaskbar |

#### C. Thuộc Tính Visual

```csharp
// VRMenuFrame
panelWidth = 1.6f;               // meters
panelHeight = 0.9f;              // meters
logicalWidth = 1920f;            // pixels
glassColor = new Color(1f, 1f, 1f, 0.098f);
glowColorA = new Color(0f, 1.5f, 2f, 1f);    // Cyan HDR
glowColorB = new Color(1.2f, 0.3f, 2f, 1f);  // Purple HDR
contentMargin = (Left:75, Right:75, Top:50, Bottom:50);
cornerRadius = 0.12f;

// VRTaskbar
logicalHeight = 80f;             // pixels
buttonSize = 44f;                // pixels
iconSize = 28f;                  // pixels
spacing = 16f;                   // pixels

// VRButtonFactory
glassAlpha = 0.08f;
borderWidth = 0.02f;
glowLayers = [0.008f, 0.018f, 0.04f, 0.08f]; // 4 layers with different widths
```

#### D. Hiệu Ứng Animation

| Hiệu Ứng | Component | Thông Số |
|----------|-----------|----------|
| Shimmer Border | VRMenuFrame, VRTaskbar | `shimmerSpeed = 0.1f`, chạy liên tục |
| Hover Glow | VRButtonAnimation | `borderWidth *= 4` khi hover |
| Floating Data | FloatingDataAnim | `speed = 10-40`, random direction |
| Dwell Ring | VRGazeReticle | `dwellTime = 1.0s`, ring fill animation |
| Key Press | VRMobileKeyboard | Scale pulse animation |

---

## 🏗️ Kế Hoạch Chuyển Đổi Chi Tiết

### Phase 1: Tạo RTT Infrastructure

#### 1.1 Tạo RenderTextureUIManager

```csharp
// Đề xuất cấu trúc mới
public class RenderTextureUIManager : MonoBehaviour
{
    [Header("Render Texture Settings")]
    public int textureWidth = 1920;
    public int textureHeight = 1080;
    public int antiAliasing = 4;
    
    [Header("References")]
    public Camera uiCamera;        // Camera render UI vào RenderTexture
    public MeshRenderer uiQuad;    // Quad hiển thị RenderTexture trong 3D space
    
    private RenderTexture _renderTexture;
    private Canvas _uiCanvas;
    
    // Tạo RenderTexture với anti-aliasing
    void CreateRenderTexture();
    // Setup Camera để render vào RenderTexture
    void SetupUICamera();
    // Setup Quad để hiển thị trong VR world
    void SetupWorldQuad();
}
```

#### 1.2 Tạo RTTCanvasBase

```csharp
// Base class cho tất cả RTT UI panels
public abstract class RTTCanvasBase : MonoBehaviour
{
    [Header("RTT Settings")]
    public Vector2Int resolution = new Vector2Int(1920, 1080);
    public float worldWidth = 1.6f;   // meters
    public float worldHeight = 0.9f;  // meters
    
    protected RenderTexture _rt;
    protected Camera _uiCamera;
    protected Canvas _canvas;
    protected MeshRenderer _displayQuad;
    
    // Shared functionality
    protected virtual void CreateRenderTexture();
    protected virtual void SetupCamera();
    protected virtual void SetupDisplayQuad();
    
    // Override để build specific UI
    protected abstract void BuildUI();
}
```

---

### Phase 2: Chuyển Đổi Từng Component

#### 2.1 Chuyển Đổi VRMenuFrame → RTTMenuFrame

**Cấu trúc mới:**

```
RTTMenuFrame (GameObject)
├── UICamera (Camera - renders to RenderTexture)
│   └── Canvas (Screen Space Camera, resolution 1920x1080)
│       ├── GlassBackground (Image + GlassGradientBackground shader)
│       │   ├── GlowingBorder (Image + GlowingGlassBorder shader)
│       │   └── FX_DataStream (FloatingDataAnim particles)
│       └── ContentContainer (RectTransform)
│           └── [VRMainMenu / VRRemoteMenu content]
└── DisplayQuad (MeshRenderer - shows RenderTexture in world)
    └── Material với RenderTexture
```

**Các thay đổi cần thực hiện:**

| File | Thay Đổi | Ghi Chú |
|------|----------|---------|
| [VRMenuFrame.cs](file:///C:/Users/super/OneDrive/Documents/VRWorkspaceProjects/VRWorkSpace/Assets/VR-Workspace/Scripts/UI/Menu/VRMenuFrame.cs) | Thay `RenderMode.WorldSpace` → `RenderMode.ScreenSpaceCamera` | Giữ nguyên tất cả child creation logic |
| Lines 250-279 | Thêm `CreateRenderTexture()`, `SetupUICamera()`, `SetupDisplayQuad()` | Thêm vào `Start()` |
| Lines 602-677 | `CreateGlassPanel()` - Giữ nguyên | Không cần thay đổi |
| Lines 679-741 | `CreateGlowingBorder()` - Giữ nguyên | Không cần thay đổi |

**Thông số bảo toàn:**
```csharp
// Giữ nguyên hoàn toàn
panelWidth = 1.6f;
panelHeight = 0.9f;
logicalWidth = 1920f;
contentMargin = (75, 75, 50, 50);
glowColorA, glowColorB = unchanged;
cornerRadius = 0.12f;
glowExpansion = 0.02f;

// Thêm mới cho RTT
rtResolution = new Vector2Int(1920, 1080);
rtAntiAliasing = 4; // MSAA cho text rõ nét
uiCameraDepth = -10; // Render trước main camera
```

---

#### 2.2 Chuyển Đổi VRTaskbar → RTTTaskbar

**Cấu trúc mới:**

```
RTTTaskbar (GameObject)
├── UICamera (Camera)
│   └── Canvas (Screen Space Camera)
│       └── [Existing VRTaskbar content - 3 sections]
└── DisplayQuad (WorldPanelPlus-style)
```

**Các thay đổi cần thực hiện:**

| File | Thay Đổi |
|------|----------|
| [VRTaskbar.cs](file:///C:/Users/super/OneDrive/Documents/VRWorkspaceProjects/VRWorkSpace/Assets/VR-Workspace/Scripts/Taskbar/VRTaskbar.cs#L250-L280) | Wrap với RTT camera |
| Lines 331-361 `UpdatePositionRelativeToPrimary()` | Cập nhật để định vị DisplayQuad thay vì Canvas |

**Thông số bảo toàn:**
```csharp
logicalHeight = 80f;           // pixels - giữ nguyên
buttonSize = 44f;              // pixels - giữ nguyên
iconSize = 28f;                // pixels - giữ nguyên
spacing = 16f;                 // pixels - giữ nguyên
spacingFromMenu = 20f;         // meters offset - giữ nguyên

// Position formula - giữ nguyên logic
posY = menu.posY - (menu.height/2) - spacing;
```

---

#### 2.3 Chuyển Đổi VRMobileKeyboard → RTTMobileKeyboard

**Cấu trúc mới:**

```
RTTMobileKeyboard (GameObject)
├── UICamera (Camera)
│   └── Canvas (Screen Space Camera)
│       ├── GlassBackground
│       ├── PreviewRow
│       ├── KeyRows (4 rows)
│       └── BottomRow (special keys)
└── DisplayQuad
```

**Các thay đổi:**

| File | Thay Đổi |
|------|----------|
| [VRMobileKeyboard.cs](file:///C:/Users/super/OneDrive/Documents/VRWorkspaceProjects/VRWorkSpace/Assets/VR-Workspace/Scripts/Keyboard/VRMobileKeyboard.cs#L593-L660) | `BuildKeyboard()` wrap với RTT |
| Lines 448-508 `UpdatePositionRelativeToPrimary()` | Cập nhật vị trí DisplayQuad |

**Thông số bảo toàn:**
```csharp
// Key layout - giữ nguyên hoàn toàn
keyWidth = 36f;
keyHeight = 40f;
keySpacing = 5f;
rowSpacing = 8f;
rowConfigs = unchanged;  // QWERTY layout

// Position - giữ nguyên logic
overlapsTaskbar = true;
closerToCamera = 0.1f;  // Z offset closer to viewer
```

---

#### 2.4 Cập Nhật VRGazeReticle

**Thay đổi chính:**

Hiện tại VRGazeReticle sử dụng `Physics.Raycast` với `BoxCollider` trên các UI elements. Với RTT, cần chuyển sang raycast vào **DisplayQuad** và tính toán UV hit point để xác định element được tương tác.

| File | Thay Đổi |
|------|----------|
| [VRGazeReticle.cs](file:///C:/Users/super/OneDrive/Documents/VRWorkspaceProjects/VRWorkSpace/Assets/VR-Workspace/Scripts/Input/VRGazeReticle.cs#L282-L339) | `CheckGaze()` - Thêm UV coordinate raycast |
| Lines 341-486 `ProcessDwellClick()` | Chuyển sang dùng `RTTRaycastManager` |

**Thuật toán mới:**
```csharp
// 1. Raycast vào DisplayQuad
if (Physics.Raycast(gazeRay, out hit, maxDistance, quadLayer))
{
    // 2. Lấy UV từ hit point
    Vector2 uv = hit.textureCoord;
    
    // 3. Chuyển UV → screen coordinates trong RenderTexture
    Vector2 screenPos = new Vector2(uv.x * rtWidth, (1 - uv.y) * rtHeight);
    
    // 4. Raycast trong UI space qua GraphicRaycaster
    var pointerData = new PointerEventData(eventSystem);
    pointerData.position = screenPos;
    
    var results = new List<RaycastResult>();
    graphicRaycaster.Raycast(pointerData, results);
    
    // 5. Process hover/click như cũ
}
```

---

### Phase 3: Xử Lý Hiệu Ứng & Shaders

#### 3.1 Hiệu Ứng Tích Hợp Được Hoàn Toàn ✅

| Hiệu Ứng | Shader/Component | Lý Do |
|----------|------------------|-------|
| Glass Gradient Background | `GlassGradientBackground.shader` | Shader hoạt động độc lập với render mode |
| Glowing Border | `GlowingGlassBorder.shader` | Shader-based, không phụ thuộc world space |
| Shimmer Animation | `_Time.y` trong shader | Hoạt động tự động |
| Button Hover Glow | `GlowingElementBorder.shader` | Shader-based |
| Horizontal Separators | `GlowingGlassBorder.shader` | Shader properties |

#### 3.2 Hiệu Ứng Cần Điều Chỉnh ⚠️

| Hiệu Ứng | Vấn Đề | Giải Pháp |
|----------|--------|-----------|
| **FloatingDataAnim** | Hiện di chuyển theo RectTransform world position | ✅ Không cần thay đổi - hoạt động trong Canvas space |
| **VRButtonAnimation** | Sử dụng `Mathf.SmoothDamp` trong Update | ✅ Không cần thay đổi - animation logic độc lập |
| **Dwell Ring** | Hiển thị tại world position | ⚠️ Cần render vào riêng RTT hoặc overlay |

#### 3.3 Hiệu Ứng Không Tích Hợp Được ❌

| Hiệu Ứng | Vấn Đề | Hướng Thay Thế |
|----------|--------|----------------|
| **UIBlurBackground** (Glassmorphism) | Shader grab scene behind UI → Với RTT, không có scene phía sau | 1. **Pre-baked blur**: Capture scene, blur, dùng làm background<br>2. **Fake blur**: Dùng noise texture thay thế<br>3. **Bỏ qua**: Giữ glass color đơn thuần |
| **Dynamic scene reflection** | Không thể grab pass trong RTT | Không cần - hiện không được sử dụng |

---

### Phase 4: Cập Nhật Các Factory Classes

#### 4.1 VRButtonFactory
- **Không cần thay đổi logic tạo UI**
- Chỉ cần đảm bảo parent canvas là RTT canvas

#### 4.2 VRDropdownFactory
- **Dropdown panel popup** cần đặc biệt xử lý
- Panel cần render trong cùng RTT với parent dropdown

```csharp
// Hiện tại (World Space)
dropdownPanel.SetParent(canvas.transform.root, false);

// Sau chuyển đổi (RTT)
dropdownPanel.SetParent(rttCanvas.transform, false);
dropdownPanel.transform.SetAsLastSibling(); // Ensure on top
```

#### 4.3 VRInputFieldFactory
- Keyboard trigger vẫn hoạt động bình thường
- Keyboard là RTT riêng biệt, không cần thay đổi logic

---

### Phase 5: Tạo Các Scripts Mới

```
📁 Scripts/UI/RTT/ (NEW)
├── RTTCanvasBase.cs           ← Abstract base class
├── RTTMenuPanel.cs            ← Wrap VRMenuFrame
├── RTTTaskbar.cs              ← Wrap VRTaskbar
├── RTTKeyboard.cs             ← Wrap VRMobileKeyboard/VRKeyboard
├── RTTRaycastManager.cs       ← Handle gaze interaction với RTT
└── RTTGrabBackground.cs       ← (Optional) Fake glassmorphism
```

---

## 📐 Chi Tiết Kỹ Thuật

### RenderTexture Configuration

```csharp
public static RenderTexture CreateUIRenderTexture(int width, int height)
{
    var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
    rt.antiAliasing = 4;           // MSAA cho text rõ
    rt.filterMode = FilterMode.Bilinear;
    rt.useMipMap = false;          // UI không cần mipmaps
    rt.autoGenerateMips = false;
    rt.anisoLevel = 0;
    rt.Create();
    return rt;
}
```

### UI Camera Setup

```csharp
public static Camera CreateUICamera(RenderTexture target, Canvas canvas)
{
    var cameraGO = new GameObject("UICamera");
    var cam = cameraGO.AddComponent<Camera>();
    
    cam.clearFlags = CameraClearFlags.SolidColor;
    cam.backgroundColor = new Color(0, 0, 0, 0); // Transparent
    cam.cullingMask = 1 << LayerMask.NameToLayer("UI");
    cam.orthographic = true;
    cam.orthographicSize = target.height / 2f;
    cam.nearClipPlane = 0.1f;
    cam.farClipPlane = 100f;
    cam.targetTexture = target;
    cam.depth = -10; // Render trước main camera
    
    // Canvas setup
    canvas.renderMode = RenderMode.ScreenSpaceCamera;
    canvas.worldCamera = cam;
    canvas.planeDistance = 10f;
    
    return cam;
}
```

### Display Quad Setup

```csharp
public static MeshRenderer CreateDisplayQuad(
    Transform parent, 
    RenderTexture texture,
    float worldWidth, 
    float worldHeight)
{
    var quadGO = new GameObject("DisplayQuad");
    quadGO.transform.SetParent(parent, false);
    
    var mf = quadGO.AddComponent<MeshFilter>();
    mf.mesh = CreateQuadMesh();
    
    var mr = quadGO.AddComponent<MeshRenderer>();
    var mat = new Material(Shader.Find("Unlit/Transparent"));
    mat.mainTexture = texture;
    mat.renderQueue = 3000; // Render after opaque
    mr.material = mat;
    
    quadGO.transform.localScale = new Vector3(worldWidth, worldHeight, 1f);
    
    // Add collider for raycast
    var col = quadGO.AddComponent<BoxCollider>();
    col.size = new Vector3(1, 1, 0.01f);
    
    return mr;
}
```

---

## ✅ Checklist Xác Minh

### Kích Thước & Tỉ Lệ

| Item | Kiểm Tra |
|------|----------|
| VRMenuFrame width | = 1.6 meters |
| VRMenuFrame height | = 0.9 meters |
| VRMenuFrame logical width | = 1920 pixels |
| VRTaskbar height | = 80 logical pixels |
| VRKeyboard key size | = 36×40 pixels |
| Content margins | = 75, 75, 50, 50 |

### Vị Trí Tương Quan

| Item | Kiểm Tra |
|------|----------|
| VRTaskbar dưới VRMenuFrame | Khoảng cách = `spacingFromMenu` |
| VRKeyboard dưới VRTaskbar | Overlap một phần |
| Tất cả UI cùng Z-plane | Parallel với camera forward |

### Hiệu Ứng Visual

| Item | Kiểm Tra |
|------|----------|
| Glass gradient background | Hiển thị đúng màu cyan-purple |
| Glowing border | Shimmer animation chạy |
| Button hover | Border glow tăng 4x |
| Floating data particles | Di chuyển ngẫu nhiên |
| Dwell ring | Fill animation khi gaze |

### Chức Năng

| Item | Kiểm Tra |
|------|----------|
| Gaze selection | Hoạt động với RTT UI |
| Button click | Trigger callback |
| Dropdown open/close | Panel hiển thị đúng |
| Keyboard input | Text được nhập vào InputField |
| QR Scanner | Camera passthrough hoạt động |

---

## ⚠️ Rủi Ro & Giải Pháp

| Rủi Ro | Mức Độ | Giải Pháp |
|--------|--------|-----------|
| Text bị mờ trong RTT | Cao | Tăng resolution, MSAA 4x |
| Glassmorphism blur không hoạt động | Trung bình | Sử dụng fake blur hoặc bỏ qua |
| Performance drop | Thấp | UI camera chỉ render khi dirty |
| Gaze raycast không chính xác | Trung bình | Impl `RTTRaycastManager` cẩn thận |
| Dropdown z-order issues | Trung bình | Render dropdown trong cùng RTT |

---

## 📅 Thứ Tự Triển Khai Đề Xuất

1. **Phase 1** (1-2 ngày): Tạo RTT infrastructure classes
2. **Phase 2A** (2-3 ngày): Chuyển đổi VRMenuFrame + test
3. **Phase 2B** (1-2 ngày): Chuyển đổi VRTaskbar + test
4. **Phase 2C** (2-3 ngày): Chuyển đổi VRKeyboard(s) + test
5. **Phase 3** (1-2 ngày): Xử lý hiệu ứng đặc biệt
6. **Phase 4** (1 ngày): Cập nhật factories
7. **Phase 5** (2-3 ngày): Integration testing toàn bộ

**Tổng thời gian ước tính: 10-16 ngày**

---

## 🔗 Tài Liệu Tham Khảo

- [Unity RenderTexture Documentation](https://docs.unity3d.com/ScriptReference/RenderTexture.html)
- [Unity Canvas Render Modes](https://docs.unity3d.com/Packages/com.unity.ugui@1.0/manual/UICanvas.html)
- [VR UI Best Practices](https://developer.oculus.com/documentation/unity/unity-best-practices-intro/)
