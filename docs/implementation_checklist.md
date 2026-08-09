# Checklist Triển Khai RTT UI - VR Workspace

> **Phiên bản**: 2.0
> **Ngày tạo**: 2025-12-25
> **Trạng thái**: Chưa bắt đầu

---

## Hướng Dẫn Sử Dụng

- [ ] = Chưa hoàn thành
- [x] = Đã hoàn thành
- [~] = Đang thực hiện
- [!] = Blocked/Có vấn đề

---

## Phase 0: Chuẩn Bị Môi Trường

### 0.1 Project Setup
- [ ] Backup toàn bộ project trước khi bắt đầu
- [ ] Tạo branch mới: `feature/rtt-ui-migration`
- [ ] Tạo Layer mới: `UI_RTT` trong Tags and Layers
- [x] Tạo thư mục: `Scripts/UI/RTT/`
- [x] Tạo thư mục: `Scripts/UI/RTT/Core/`
- [x] Tạo thư mục: `Scripts/UI/RTT/Components/`
- [x] Tạo thư mục: `Scripts/UI/RTT/Input/`
- [x] Tạo thư mục: `Scripts/UI/RTT/Effects/`
- [x] Tạo thư mục: `Scripts/UI/RTT/Debug/`

### 0.2 Dependencies Check
- [ ] Xác nhận Unity version: 2021.3 LTS hoặc 2022.3 LTS
- [ ] Kiểm tra XR Plugin đã cài đặt
- [ ] Kiểm tra TextMeshPro đã import
- [ ] Kiểm tra tất cả shaders compile không lỗi

---

## Phase 1: RTT Infrastructure

### 1.1 RTTConfig ScriptableObject
- [x] Tạo file `RTTConfig.cs`
- [x] Định nghĩa `RTTQualityPreset` struct
- [x] Thêm fields: defaultWidth, defaultHeight, antiAliasing
- [x] Thêm fields: format, filterMode
- [x] Thêm quality presets: low, medium, high
- [x] Thêm performance flags: useDirtyFlag, maxFrameSkip
- [x] Thêm debug flags: showDebugGizmos, logPerformanceMetrics
- [ ] Tạo asset: `Resources/RTTConfig.asset`
- [x] Cấu hình Low preset: 1280x720, AA=2
- [x] Cấu hình Medium preset: 1600x900, AA=4
- [x] Cấu hình High preset: 1920x1080, AA=4
- [ ] Test: ScriptableObject load được trong runtime

### 1.2 RTTCanvasBase Abstract Class
- [ ] Tạo file `RTTCanvasBase.cs`
- [ ] **Serialized Fields**
  - [ ] RTTConfig reference
  - [ ] worldWidth, worldHeight
  - [ ] uiLayerName, quadLayerName
- [ ] **Protected Fields**
  - [ ] RenderTexture _renderTexture
  - [ ] Camera _uiCamera
  - [ ] Canvas _canvas
  - [ ] CanvasScaler _canvasScaler
  - [ ] GraphicRaycaster _graphicRaycaster
  - [ ] MeshRenderer _displayQuad
  - [ ] BoxCollider _quadCollider
  - [ ] bool _isDirty, _isInitialized, _isVisible
- [ ] **Events**
  - [ ] OnRTTCreated
  - [ ] OnRTTDestroyed
  - [ ] OnVisibilityChanged
- [ ] **Lifecycle Methods**
  - [ ] Awake() - ValidateConfiguration
  - [ ] Start() - Initialize
  - [ ] OnDestroy() - Cleanup
  - [ ] OnEnable/OnDisable - SetVisible
  - [ ] LateUpdate() - HandleDirtyRendering
- [ ] **RenderTexture Methods**
  - [ ] CreateRenderTexture()
  - [ ] GetResolution() virtual
  - [ ] ResizeRenderTexture(width, height)
- [ ] **Camera Methods**
  - [ ] SetupUICamera()
  - [ ] GetCameraDepth() virtual
- [ ] **Canvas Methods**
  - [ ] SetupCanvas()
- [ ] **Quad Methods**
  - [ ] SetupDisplayQuad()
  - [ ] CreateQuadMesh()
  - [ ] CreateQuadMaterial()
- [ ] **Dirty Flag Methods**
  - [ ] MarkDirty()
  - [ ] HandleDirtyRendering()
- [ ] **Visibility Methods**
  - [ ] SetVisible(bool)
- [ ] **Cleanup Methods**
  - [ ] Cleanup()
  - [ ] HandleInitializationFailure()
  - [ ] SetupFallbackWorldSpaceCanvas() virtual
- [ ] **Public API**
  - [ ] GetRenderTexture()
  - [ ] GetCanvas()
  - [ ] GetGraphicRaycaster()
  - [ ] GetDisplayQuad()
  - [ ] GetQuadCollider()
- [ ] **Abstract Method**
  - [ ] BuildUI()
- [ ] Test: Có thể kế thừa và override methods

### 1.3 RTTManager Singleton
- [ ] Tạo file `RTTManager.cs`
- [ ] Implement Singleton pattern
- [ ] DontDestroyOnLoad setup
- [ ] **Fields**
  - [ ] RTTConfig defaultConfig
  - [ ] int maxConcurrentRenders
  - [ ] bool enablePerformanceLogging
  - [ ] List<RTTCanvasBase> _registeredPanels
  - [ ] int _currentCameraDepth
- [ ] **Registration Methods**
  - [ ] RegisterPanel(RTTCanvasBase)
  - [ ] UnregisterPanel(RTTCanvasBase)
  - [ ] AssignCameraDepth()
- [ ] **Quality Methods**
  - [ ] SetQualityLevel(RTTQualityLevel)
  - [ ] GetQualityPreset(RTTQualityLevel)
- [ ] **Performance Stats**
  - [ ] GetPerformanceStats()
  - [ ] CalculateTotalTextureMemory()
- [ ] Tạo enum `RTTQualityLevel`
- [ ] Tạo struct `RTTPerformanceStats`
- [ ] Test: Singleton accessible từ mọi nơi
- [ ] Test: Panel registration/unregistration hoạt động

### 1.4 RTTMemoryManager
- [ ] Tạo file `RTTMemoryManager.cs`
- [ ] **Fields**
  - [ ] float maxTextureMemoryMB
  - [ ] float warningThresholdMB
  - [ ] bool enableAutoScaling
  - [ ] float scaleDownThresholdMB
  - [ ] float scaleUpThresholdMB
  - [ ] RTTQualityLevel _currentQuality
- [ ] **Methods**
  - [ ] Update() - check memory and auto-scale
  - [ ] ScaleDown()
  - [ ] ScaleUp()
- [ ] Test: Auto-scaling khi memory cao
- [ ] Test: Không scale khi disabled

---

## Phase 2: RTT Raycast System

### 2.1 RTTRaycastManager
- [ ] Tạo file `RTTRaycastManager.cs`
- [ ] Implement Singleton pattern
- [ ] **Configuration**
  - [ ] float maxRaycastDistance
  - [ ] LayerMask raycastLayerMask
  - [ ] bool showDebugRays
  - [ ] bool logHitInfo
- [ ] **State**
  - [ ] Dictionary<RTTCanvasBase, RTTPanelRaycastData> cache
  - [ ] RTTHitResult _currentHit, _previousHit
  - [ ] GameObject _currentHoveredObject, _previousHoveredObject
  - [ ] PointerEventData _pointerEventData
  - [ ] List<RaycastResult> _raycastResults
- [ ] **Core Methods**
  - [ ] Raycast(Ray ray) → RTTHitResult
  - [ ] SendClick() → bool
  - [ ] SendScroll(Vector2 scrollDelta)
  - [ ] GetCurrentHit()
  - [ ] IsHoveringUI()
- [ ] **Private Methods**
  - [ ] FindRTTPanel(Collider)
  - [ ] GetOrCreatePanelData(RTTCanvasBase)
  - [ ] UVToScreenPosition(Vector2 uv, RTTPanelRaycastData)
  - [ ] HandleHoverStateChanges()
  - [ ] HandleNoHit()
- [ ] **Cleanup**
  - [ ] ClearCache()
- [ ] Tạo struct `RTTHitResult`
- [ ] Tạo class `RTTPanelRaycastData`
- [ ] Test: UV (0,0) → Screen bottom-left
- [ ] Test: UV (1,1) → Screen top-right
- [ ] Test: UV (0.5, 0.5) → Screen center
- [ ] Test: Raycast hit đúng panel
- [ ] Test: Raycast hit đúng UI element
- [ ] Test: Hover state change triggers events

### 2.2 VRGazeReticle Integration
- [ ] Backup file `VRGazeReticle.cs`
- [ ] Thêm field: bool useRTTRaycast
- [ ] Thêm field: RTTRaycastManager _rttRaycastManager
- [ ] Thêm method: InitializeRTTIntegration()
- [ ] Thêm method: CheckGaze_RTT()
- [ ] Thêm method: ProcessDwellClick_RTT(RTTHitResult)
- [ ] Modify CheckGaze() để switch giữa RTT và Legacy
- [ ] Test: Gaze hoạt động với RTT panels
- [ ] Test: Dwell click hoạt động với RTT buttons
- [ ] Test: Fallback về legacy khi RTT disabled

---

## Phase 3: Component Migration

### 3.1 RTTMenuFrame (từ VRMenuFrame)
- [ ] Tạo file `RTTMenuFrame.cs`
- [ ] Kế thừa từ RTTCanvasBase
- [ ] **Configuration (copy từ VRMenuFrame)**
  - [ ] panelWidth = 1.6f
  - [ ] panelHeight = 0.9f
  - [ ] logicalWidth = 1920f
  - [ ] marginLeft = 75f, marginRight = 75f
  - [ ] marginTop = 50f, marginBottom = 50f
  - [ ] glassColor
  - [ ] cornerRadius = 0.12f
  - [ ] glowColorA, glowColorB
  - [ ] glowExpansion = 0.02f
  - [ ] horizontalSeparators, verticalSeparators
  - [ ] enableFloatingData
  - [ ] shimmerSpeed = 0.1f
- [ ] **Primary Instance Pattern**
  - [ ] static RTTMenuFrame _primaryInstance
  - [ ] bool isPrimary
- [ ] **Override Methods**
  - [ ] Awake() - set worldWidth/worldHeight
  - [ ] Start() - set primary instance
  - [ ] GetResolution() - calculate from logicalWidth
  - [ ] GetCameraDepth() - return -50
  - [ ] BuildUI()
  - [ ] SetupFallbackWorldSpaceCanvas()
- [ ] **UI Building Methods**
  - [ ] CreateGlassBackground()
  - [ ] CreateGlowingBorder()
  - [ ] CreateContentContainer()
  - [ ] CreateFloatingDataEffect()
  - [ ] SetupMaterialProperties()
  - [ ] SetupSeparators()
- [ ] **Public API**
  - [ ] GetContentContainer()
  - [ ] SetContent(GameObject)
  - [ ] UpdateGlowColors(Color, Color)
- [ ] **Cleanup**
  - [ ] Cleanup() - destroy materials
- [ ] Test: Panel renders với đúng kích thước 1.6m x 0.9m
- [ ] Test: Glass gradient hiển thị cyan-purple
- [ ] Test: Border shimmer animation chạy
- [ ] Test: Content margins đúng 75/75/50/50
- [ ] Test: Floating data particles hiển thị
- [ ] Test: Primary instance accessible globally

### 3.2 RTTTaskbar (từ VRTaskbar)

Current architecture: the Light button directly controls `MediaEnvironmentController`; there is no Eye/Passthrough expansion. `MainScene` uses one display/XR camera while RTT canvases retain offscreen RenderTexture cameras.

- [ ] Tạo file `RTTTaskbar.cs`
- [ ] Kế thừa từ RTTCanvasBase
- [ ] **Configuration**
  - [ ] logicalHeight = 80f
  - [ ] buttonSize = 44f
  - [ ] iconSize = 28f
  - [ ] spacing = 16f
  - [ ] spacingFromMenu = 20f
  - [ ] RTTMenuFrame targetMenuFrame
- [ ] **Override Methods**
  - [ ] GetResolution() - match menu width
  - [ ] GetCameraDepth() - return -49
  - [ ] BuildUI()
- [ ] **UI Building (copy logic từ VRTaskbar)**
  - [ ] CreateThreeSections()
  - [ ] CreateControlButtons() - Quit, Settings, Light, Recenter
  - [ ] CreateAppButtons() - Home + 3 slots
  - [ ] CreateStatusDisplay() - Clock, Battery, WiFi
- [ ] **Position Methods**
  - [ ] LateUpdate() - call UpdatePositionRelativeToMenu
  - [ ] UpdatePositionRelativeToMenu()
- [ ] **Status Update Methods**
  - [ ] UpdateClock()
  - [ ] UpdateBattery()
  - [ ] UpdateWiFi()
- [ ] Test: Taskbar positioned below menu
- [ ] Test: Buttons clickable via gaze
- [ ] Test: Clock updates real-time
- [ ] Test: Battery indicator working
- [ ] Test: Light button toggles the virtual environment directly

### 3.3 RTTMobileKeyboard (từ VRMobileKeyboard)
- [ ] Tạo file `RTTMobileKeyboard.cs`
- [ ] Kế thừa từ RTTCanvasBase
- [ ] **Configuration**
  - [ ] keyWidth = 36f
  - [ ] keyHeight = 40f
  - [ ] keySpacing = 5f
  - [ ] rowSpacing = 8f
  - [ ] Row configs for QWERTY layout
- [ ] **Keyboard Layouts**
  - [ ] Letters layout
  - [ ] Symbols layout
  - [ ] MoreSymbols layout
- [ ] **Override Methods**
  - [ ] GetResolution()
  - [ ] GetCameraDepth() - return -48
  - [ ] BuildUI()
- [ ] **UI Building**
  - [ ] CreateGlassBackground()
  - [ ] CreatePreviewRow()
  - [ ] CreateKeyRows()
  - [ ] CreateBottomRow()
- [ ] **Key Methods**
  - [ ] CreateKey(char, row, col)
  - [ ] OnKeyPress(char)
  - [ ] SwitchLayout(layout)
- [ ] **Position Methods**
  - [ ] UpdatePositionRelativeToTaskbar()
- [ ] **Show/Hide**
  - [ ] Show()
  - [ ] Hide()
  - [ ] static Instance accessor
- [ ] Test: Keyboard shows when InputField focused
- [ ] Test: Key presses input correct characters
- [ ] Test: Layout switching works
- [ ] Test: Keyboard hides on submit

### 3.4 RTTKeyboard (từ VRKeyboard - Full size)
- [ ] Tạo file `RTTKeyboard.cs`
- [ ] Kế thừa từ RTTCanvasBase
- [ ] Copy structure từ RTTMobileKeyboard
- [ ] Adjust layout for full QWERTY + numpad
- [ ] Test: Full keyboard renders correctly
- [ ] Test: All keys functional

---

## Phase 4: Effects & Shaders

### 4.1 Shader Compatibility Check
- [ ] Test `GlassGradientBackground.shader` trong RTT
- [ ] Test `GlowingGlassBorder.shader` trong RTT
- [ ] Test `GlowingElementBorder.shader` trong RTT
- [ ] Test `GlowingConnectButton.shader` trong RTT
- [ ] Test `GlowingHorizontalLine.shader` trong RTT
- [ ] Verify shimmer animation hoạt động
- [ ] Verify gradient colors đúng

### 4.2 GrabPass Alternative (Glassmorphism)
- [ ] Tạo file `RTTBackgroundCapture.cs`
- [ ] **Fields**
  - [ ] Camera mainCamera
  - [ ] RenderTexture backgroundCapture
  - [ ] Material blurMaterial
  - [ ] int blurIterations
  - [ ] RenderTexture _blurredBackground
- [ ] **Methods**
  - [ ] CaptureAndBlur()
  - [ ] ApplyGaussianBlur(source, iterations)
- [ ] Tạo blur shader nếu cần
- [ ] Set global texture: `_BlurredBackground`
- [ ] Modify glass shader để sử dụng `_BlurredBackground`
- [ ] Test: Background blur renders correctly
- [ ] Test: Performance acceptable (< 1ms)

### 4.3 FloatingDataAnim Integration
- [ ] Verify FloatingDataAnim works trong RTT canvas
- [ ] Add callback: OnAnimationUpdate → MarkDirty()
- [ ] Test: Particles animate smoothly
- [ ] Test: Particles trigger re-render

### 4.4 VRButtonAnimation Integration
- [ ] Verify hover glow works với RTT raycast
- [ ] Verify click animation works
- [ ] Test: Border width increases 4x on hover
- [ ] Test: Flash animation on click

---

## Phase 5: Factory Updates

### 5.1 VRButtonFactory Compatibility
- [x] Verify buttons create correctly in RTT canvas
- [x] Verify layer assignment works
- [x] Verify colliders set up for RTT raycast
- [ ] Test: Button hover detected
- [ ] Test: Button click triggers callback

### 5.2 VRDropdownFactory Compatibility
- [x] Verify dropdown creates in RTT canvas
- [x] Verify dropdown panel renders in same RTT
- [x] Fix z-order if needed
- [ ] Test: Dropdown opens correctly
- [ ] Test: Option selection works
- [ ] Test: Dropdown closes on click outside

### 5.3 VRInputFieldFactory Compatibility
- [x] Verify input field creates in RTT canvas
- [x] Verify keyboard trigger works (Updated VRKeyboardManager + VRInputFieldTrigger)
- [ ] Test: Focus shows keyboard
- [ ] Test: Text input works
- [ ] Test: Submit closes keyboard

---

## Phase 6: Testing

### 6.1 Unit Tests
- [ ] Tạo file `RTTTests.cs`
- [ ] Test: RenderTexture_CreatesCorrectDimensions
- [ ] Test: UVToScreen_ConvertsCorrectly
- [ ] Test: DirtyFlag_PreventsUnnecessaryRenders
- [ ] Test: MemoryCalculation_Accurate
- [ ] Test: QualityPreset_AppliesCorrectly
- [ ] Run all unit tests - PASS

### 6.2 Integration Tests
- [ ] Test: Create RTTMenuFrame → RenderTexture created
- [ ] Test: Create RTTTaskbar → Positioned below menu
- [ ] Test: Gaze at button → Correct button highlighted
- [ ] Test: Dwell click → Button callback triggered
- [ ] Test: Multiple RTT panels → Each has unique RT
- [ ] Test: Panel resize → RenderTexture recreated
- [ ] Test: Panel hide/show → Visibility toggles

### 6.3 Visual Tests
- [ ] Glass gradient visible and correct colors
- [ ] Border shimmer animating smoothly
- [ ] Hover glow increases on buttons
- [ ] Floating data particles moving
- [ ] Text sharp and readable
- [ ] No visual artifacts or tearing

### 6.4 Performance Tests
- [ ] Frame time impact < 2ms per panel
- [ ] Memory per panel < 16MB
- [ ] Raycast time < 0.1ms
- [ ] UI update time < 0.5ms
- [ ] No frame drops in VR (maintain 72+ FPS)
- [ ] Memory stable after 5 minutes
- [ ] No memory leaks after show/hide cycles

### 6.5 Edge Case Tests
- [ ] RTT initialization fails → Fallback works
- [ ] High memory usage → Auto-scale down
- [ ] Multiple keyboards open → Handled correctly
- [ ] Rapid button clicks → No double-fire
- [ ] Panel destroyed while hovering → No crash

---

## Phase 7: Debug Tools

### 7.1 RTTDebugOverlay
- [x] Tạo file `RTTDebugOverlay.cs`
- [x] Display: Total panels count
- [x] Display: Visible panels count
- [x] Display: Total texture memory MB
- [x] Display: Current quality level
- [x] Display: FPS counter
- [x] Toggle với keyboard shortcut (F12?)
- [ ] Test: Overlay displays correctly

### 7.2 RTTGizmoDrawer
- [x] Tạo file `RTTGizmoDrawer.cs`
- [x] Draw quad bounds in editor
- [x] Draw raycast debug rays
- [x] Draw UV hit points
- [ ] Test: Gizmos visible in Scene view

---

## Phase 8: Migration & Rollout

### 8.1 Feature Toggle
- [x] Tạo file `RTTFeatureToggle.cs`
- [x] Implement PlayerPrefs storage
- [x] Property: UseRTT (get/set)
- [ ] Add toggle to Settings menu
- [ ] Test: Toggle persists between sessions

### 8.2 Fallback System
- [ ] Verify auto-fallback on init failure
- [ ] Verify fallback on memory threshold
- [ ] Add user notification on fallback
- [ ] Test: Graceful degradation works

### 8.3 Alpha Release
- [ ] Enable RTTMenuFrame only
- [ ] Internal testing: 3 days
- [ ] Fix critical bugs found
- [ ] Document known issues

### 8.4 Beta Release
- [ ] Enable RTTTaskbar
- [ ] Internal testing: 3 days
- [ ] Fix bugs found
- [ ] Update documentation

### 8.5 RC Release
- [ ] Enable RTTKeyboard(s)
- [ ] Full integration testing: 5 days
- [ ] Performance optimization
- [ ] Fix all bugs

### 8.6 Final Release
- [ ] Enable all RTT components by default
- [ ] Remove legacy World Space code (optional)
- [ ] Update user documentation
- [ ] Create release notes

---

## Phase 9: Documentation

### 9.1 Code Documentation
- [x] XML comments cho RTTCanvasBase
- [x] XML comments cho RTTManager
- [x] XML comments cho RTTRaycastManager
- [x] XML comments cho all public APIs

### 9.2 User Documentation
- [x] Update README với RTT info (RTT_USAGE_GUIDE.md)
- [x] Document Settings toggle
- [x] Document troubleshooting steps
- [x] Document performance tuning

### 9.3 Developer Documentation
- [x] Architecture overview diagram (text-based in guide)
- [x] Class relationship diagram (in usage guide)
- [x] Data flow diagram (in usage guide)
- [x] How to extend RTT system

---

## Tracking

### Progress Summary

| Phase | Tasks | Completed | Progress |
|-------|-------|-----------|----------|
| Phase 0: Setup | 13 | 6 | 46% |
| Phase 1: Infrastructure | 52 | 45 | 87% |
| Phase 2: Raycast | 28 | 20 | 71% |
| Phase 3: Components | 67 | 35 | 52% |
| Phase 4: Effects | 19 | 8 | 42% |
| Phase 5: Factories | 14 | 8 | 57% |
| Phase 6: Testing | 32 | 0 | 0% |
| Phase 7: Debug | 11 | 10 | 91% |
| Phase 8: Migration | 18 | 5 | 28% |
| Phase 9: Documentation | 12 | 12 | 100% |
| **TOTAL** | **266** | **149** | **56%** |

### Issue Log

| ID | Phase | Description | Status | Resolution |
|----|-------|-------------|--------|------------|
| - | - | - | - | - |

### Notes

```
[Ghi chú trong quá trình triển khai]


```

---

## Sign-off

- [ ] **Phase 0 Complete** - Date: ______ - Signed: ______
- [ ] **Phase 1 Complete** - Date: ______ - Signed: ______
- [ ] **Phase 2 Complete** - Date: ______ - Signed: ______
- [ ] **Phase 3 Complete** - Date: ______ - Signed: ______
- [ ] **Phase 4 Complete** - Date: ______ - Signed: ______
- [ ] **Phase 5 Complete** - Date: ______ - Signed: ______
- [ ] **Phase 6 Complete** - Date: ______ - Signed: ______
- [ ] **Phase 7 Complete** - Date: ______ - Signed: ______
- [ ] **Phase 8 Complete** - Date: ______ - Signed: ______
- [ ] **Phase 9 Complete** - Date: ______ - Signed: ______
- [ ] **PROJECT COMPLETE** - Date: ______ - Signed: ______
