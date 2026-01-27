# VRWorkSpace Media App - Tài liệu đặc tả kỹ thuật

**Phiên bản**: 1.0
**Ngày tạo**: 2026-01-27
**Trạng thái**: Draft

---

## Mục lục

1. [Tổng quan](#1-tổng-quan)
2. [Kiến trúc hệ thống](#2-kiến-trúc-hệ-thống)
3. [Video Projection System](#3-video-projection-system)
4. [Media Library UI](#4-media-library-ui)
5. [Playback Controls](#5-playback-controls)
6. [Data Models](#6-data-models)
7. [Shaders](#7-shaders)
8. [File Structure](#8-file-structure)
9. [Implementation Phases](#9-implementation-phases)
10. [Testing & Verification](#10-testing--verification)

---

## 1. Tổng quan

### 1.1 Mục tiêu

Xây dựng chức năng **Media App** cho VRWorkSpace - một ứng dụng quản lý và phát video VR toàn diện, hỗ trợ đầy đủ các định dạng video VR phổ biến.

### 1.2 Phạm vi chức năng

| Chức năng | Mô tả |
|-----------|-------|
| **Media Library** | Duyệt, tìm kiếm, quản lý video từ bộ nhớ thiết bị |
| **VR Video Player** | Phát video với các chế độ projection khác nhau |
| **Playback Controls** | Điều khiển phát video tối ưu cho VR |
| **Playlist Management** | Quản lý danh sách phát |
| **Subtitles** | Hỗ trợ phụ đề SRT |

### 1.3 Giới hạn

- **Video source**: Chỉ local files (không streaming)
- **Subtitles**: Chỉ định dạng SRT
- **Environment (Mono)**: Sử dụng hệ thống môi trường có sẵn
- **Environment (180°/360°)**: Không hiển thị môi trường (video là môi trường)

### 1.4 Tích hợp với hệ thống hiện có

| Component | Vai trò |
|-----------|---------|
| `RTTAppRegistry` | App type `Media` (id: "media", position: 2) - đã định nghĩa |
| `RTTManager` | Lifecycle: `OpenApp()`, `CloseApp()`, `SwitchToApp()` |
| `RTTMenuFrame` | UI rendering qua Render-to-Texture |
| `WorldPanelPlus` | Display surface cho video (mode Flat) |
| `HevcDecoderPlugin` | Hardware H.265 decoding trên Android |
| `VideoFrameExtractor` | Thumbnail extraction |
| `FileThumbnailService` | Caching thumbnails |
| `FileMetadataService` | Đọc metadata video |

---

## 2. Kiến trúc hệ thống

### 2.1 Component Hierarchy

```
RTTManager.OpenApp("media")
    │
    └── VRMediaApp (RTTAppInstance)
            │
            ├── VRMediaAppController
            │       │
            │       ├── [Mode: Library]
            │       │   └── RTTMediaLibraryController
            │       │           ├── RTTMediaSidePanel (Categories/Filters)
            │       │           ├── RTTMediaGrid/List (Video thumbnails)
            │       │           └── RTTMediaDetail (Video info)
            │       │
            │       └── [Mode: Player]
            │           └── VRVideoPlayerController
            │                   ├── VideoPlaybackEngine (Decoding)
            │                   ├── VRVideoProjectionSystem (Display)
            │                   ├── RTTMediaControlsPanel (UI Controls)
            │                   └── SubtitleManager (Subtitles)
```

### 2.2 Core Classes

#### 2.2.1 VRMediaAppController

```csharp
/// <summary>
/// Main controller - điều phối giữa Library mode và Player mode
/// </summary>
public class VRMediaAppController : MonoBehaviour
{
    public enum AppMode { Library, Player }

    // State
    public AppMode CurrentMode { get; private set; }
    public MediaVideoInfo CurrentVideo { get; private set; }

    // Events
    public event Action OnBackClicked;
    public event Action<AppMode> OnModeChanged;

    // Lifecycle
    public GameObject CreateMenu(RectTransform container, float w, float h,
        TMP_FontAsset font, Color primaryColor, Color accentColor);
    public void Cleanup();

    // Navigation
    public void SwitchToLibrary();
    public void SwitchToPlayer(MediaVideoInfo video);
}
```

#### 2.2.2 VideoPlaybackEngine

```csharp
/// <summary>
/// Xử lý video decoding - wrapper cho Unity VideoPlayer + HEVC
/// </summary>
public class VideoPlaybackEngine : MonoBehaviour
{
    public enum DecoderMode { UnityVideoPlayer, HevcHardware }

    // Output
    public RenderTexture OutputTexture { get; }
    public Texture2D YPlaneTexture { get; }      // For HEVC NV12
    public Texture2D UVPlaneTexture { get; }     // For HEVC NV12
    public bool UseNV12Output { get; }

    // Properties
    public double Duration { get; }
    public double CurrentTime { get; set; }
    public float FrameRate { get; }
    public Vector2Int Resolution { get; }
    public bool IsPlaying { get; }
    public bool IsPrepared { get; }

    // Events
    public event Action OnPrepareCompleted;
    public event Action OnPlaybackEnded;
    public event Action<string> OnError;

    // Methods
    public void PrepareLocal(string filePath);
    public void Play();
    public void Pause();
    public void Seek(double timeSeconds);
    public void Stop();
    public void SetPlaybackSpeed(float speed);
    public void SetVolume(float volume);
    public void Dispose();
}
```

#### 2.2.3 VRVideoProjectionSystem

```csharp
/// <summary>
/// Quản lý các loại projection cho VR video
/// </summary>
public class VRVideoProjectionSystem : MonoBehaviour
{
    // Projection Renderers
    private Dictionary<ProjectionType, IProjectionRenderer> _renderers;
    private IProjectionRenderer _activeRenderer;

    // Display Settings (chỉ cho Flat mode)
    public float ScreenDistance { get; set; } = 2.0f;
    public float ScreenScale { get; set; } = 1.0f;
    public float ScreenCurvature { get; set; } = 0.0f;
    public bool HeadTrackingLocked { get; set; } = false;

    // Methods
    public void Initialize(Transform cameraRig);
    public void SetProjection(ProjectionType type, StereoMode stereo = StereoMode.Mono);
    public void UpdateTexture(Texture videoTexture);
    public void UpdateTextureNV12(Texture2D yPlane, Texture2D uvPlane);
    public void RecenterView();
    public void Show();
    public void Hide();
    public void Dispose();
}
```

#### 2.2.4 MediaLibraryService

```csharp
/// <summary>
/// Service quản lý thư viện video - scanning, search, metadata
/// </summary>
public class MediaLibraryService : MonoBehaviour
{
    // Scanning
    public IEnumerator ScanMediaLibrary(Action<int> onProgress, Action<List<MediaVideoInfo>> onComplete);

    // Query
    public List<MediaVideoInfo> GetAllVideos();
    public List<MediaVideoInfo> GetRecentVideos(int count = 50);
    public List<MediaVideoInfo> GetFavorites();
    public List<MediaVideoInfo> FilterByProjection(VideoProjectionType type);
    public List<MediaVideoInfo> FilterByFormat(VideoFormat format);
    public List<MediaVideoInfo> FilterByDuration(DurationRange range);
    public List<MediaVideoInfo> Search(string query);

    // Favorites
    public void SetFavorite(string path, bool isFavorite);
    public bool IsFavorite(string path);

    // History
    public void RecordPlayback(string path);
    public List<MediaVideoInfo> GetPlaybackHistory(int count);
}
```

### 2.3 Data Flow

```
┌─────────────────────────────────────────────────────────────────┐
│                     VRMediaAppController                         │
│  ┌─────────────────┐              ┌────────────────────────┐    │
│  │  Library Mode   │◄────────────►│     Player Mode        │    │
│  │                 │   Switch     │                        │    │
│  │ RTTMediaLibrary │              │ VRVideoPlayerController│    │
│  │ Controller      │              │                        │    │
│  └────────┬────────┘              └───────────┬────────────┘    │
│           │                                   │                  │
│           ▼                                   ▼                  │
│  ┌─────────────────┐              ┌────────────────────────┐    │
│  │MediaLibrary     │              │VideoPlaybackEngine     │    │
│  │Service          │              │                        │    │
│  │ • Scan files    │              │ • Unity VideoPlayer    │    │
│  │ • Search        │              │ • HevcDecoderPlugin    │    │
│  │ • Filter        │              │                        │    │
│  └─────────────────┘              └───────────┬────────────┘    │
│                                               │                  │
│                                               ▼                  │
│                                   ┌────────────────────────┐    │
│                                   │VRVideoProjectionSystem │    │
│                                   │                        │    │
│                                   │ • FlatProjection       │    │
│                                   │ • Dome180              │    │
│                                   │ • Sphere360            │    │
│                                   │ • Stereoscopic         │    │
│                                   └────────────────────────┘    │
└─────────────────────────────────────────────────────────────────┘
```

---

## 3. Video Projection System

### 3.1 Projection Types

| Type | Enum Value | Description | Mesh |
|------|------------|-------------|------|
| Flat | `Flat` | Standard 2D screen | Quad hoặc Curved Quad |
| 180° | `Dome180` | 180° equirectangular | Hemisphere (inside-out) |
| 360° | `Sphere360` | 360° equirectangular | Sphere (inside-out) |
| SBS 3D | `SideBySide3D` | Side-by-side stereoscopic | Quad |
| OU 3D | `OverUnder3D` | Over-Under stereoscopic | Quad |
| VR180 | `VR180Stereo` | VR180 stereoscopic (SBS + 180°) | Hemisphere |

```csharp
public enum VideoProjectionType
{
    Flat,           // Standard 2D screen
    Dome180,        // 180° half-dome
    Sphere360,      // Full 360° sphere
    SideBySide3D,   // Left-Right 3D
    OverUnder3D,    // Top-Bottom 3D
    VR180Stereo     // VR180 stereoscopic
}

public enum StereoMode
{
    Mono,
    LeftEye,
    RightEye,
    Stereo
}
```

### 3.2 Projection Renderer Interface

```csharp
public interface IProjectionRenderer
{
    ProjectionType Type { get; }

    void Initialize(Transform parent);
    void SetTexture(Texture texture);
    void SetTextureNV12(Texture2D yPlane, Texture2D uvPlane);
    void SetStereoMode(StereoMode mode);
    void UpdateDisplay(DisplaySettings settings);
    void Show();
    void Hide();
    void Dispose();
}

public struct DisplaySettings
{
    public float Distance;      // 1.5m - 4.0m
    public float Scale;         // 0.5x - 2.0x
    public float Curvature;     // 0.0 - 1.0
    public bool HeadLocked;
    public Vector3 PositionOffset;
    public Quaternion RotationOffset;
}
```

### 3.3 Projection Renderers

#### 3.3.1 FlatProjectionRenderer

```csharp
/// <summary>
/// 2D flat screen projection - sử dụng cho video thông thường
/// </summary>
public class FlatProjectionRenderer : MonoBehaviour, IProjectionRenderer
{
    // Mesh: Quad hoặc Curved Quad
    // Screen có thể curved dựa trên Curvature setting
    // Tích hợp với môi trường có sẵn + lights toggle

    private MeshRenderer _renderer;
    private MeshFilter _meshFilter;
    private Material _material;

    public void UpdateDisplay(DisplaySettings settings)
    {
        // Position screen at distance
        transform.localPosition = Vector3.forward * settings.Distance;

        // Scale based on aspect ratio
        float aspect = (float)_resolution.x / _resolution.y;
        transform.localScale = new Vector3(settings.Scale * aspect, settings.Scale, 1f);

        // Apply curvature
        _material.SetFloat("_Curvature", settings.Curvature);
    }
}
```

#### 3.3.2 Dome180Renderer

```csharp
/// <summary>
/// 180° hemisphere projection - viewer ở tâm dome
/// </summary>
public class Dome180Renderer : MonoBehaviour, IProjectionRenderer
{
    // Mesh: Hemisphere inside-out (64 segments)
    // Covers: -90° to +90° horizontal, -90° to +90° vertical
    // UV mapping: Equirectangular
    // Environment: Hidden (video là môi trường)

    private const int SEGMENTS_HORIZONTAL = 64;
    private const int SEGMENTS_VERTICAL = 32;

    private void GenerateDomeMesh()
    {
        // Generate hemisphere with inside-facing triangles
        // UV: u = longitude / 180, v = latitude / 180
    }
}
```

#### 3.3.3 Sphere360Renderer

```csharp
/// <summary>
/// 360° sphere projection - viewer ở tâm sphere
/// </summary>
public class Sphere360Renderer : MonoBehaviour, IProjectionRenderer
{
    // Mesh: Full sphere inside-out (64 segments)
    // UV mapping: Equirectangular
    // Environment: Hidden

    private const int SEGMENTS = 64;

    private void GenerateSphereMesh()
    {
        // Full sphere with inverted triangles
        // UV: u = longitude / 360 + 0.5, v = latitude / 180 + 0.5
    }
}
```

#### 3.3.4 StereoscopicRenderer

```csharp
/// <summary>
/// Stereoscopic 3D - hỗ trợ SBS, OU, VR180
/// </summary>
public class StereoscopicRenderer : MonoBehaviour, IProjectionRenderer
{
    public StereoFormat Format { get; set; }      // SBS, OU
    public ProjectionType BaseProjection { get; set; }  // Flat, Dome180

    // Shader samples left/right halves based on XR eye index
    // SBS: left eye u=[0, 0.5], right eye u=[0.5, 1]
    // OU: left eye v=[0.5, 1], right eye v=[0, 0.5]
}
```

### 3.4 Projection Auto-Detection

```csharp
public static class ProjectionDetector
{
    /// <summary>
    /// Tự động detect projection type từ filename và resolution
    /// </summary>
    public static VideoProjectionType DetectProjection(string filename, int width, int height)
    {
        string lower = filename.ToLower();

        // Filename patterns
        if (lower.Contains("_360") || lower.Contains("360x180"))
            return VideoProjectionType.Sphere360;
        if (lower.Contains("_180") || lower.Contains("180x180"))
            return VideoProjectionType.Dome180;
        if (lower.Contains("_sbs") || lower.Contains("_3d_sbs"))
            return VideoProjectionType.SideBySide3D;
        if (lower.Contains("_tb") || lower.Contains("_ou") || lower.Contains("_3d_ou"))
            return VideoProjectionType.OverUnder3D;
        if (lower.Contains("_vr180") || lower.Contains("_180_3d"))
            return VideoProjectionType.VR180Stereo;

        // Resolution heuristics
        if (width == height * 2)  // 2:1 ratio
            return VideoProjectionType.Sphere360;
        if (width == height)      // 1:1 ratio
            return VideoProjectionType.Dome180;

        return VideoProjectionType.Flat;
    }
}
```

### 3.5 Environment Logic

```csharp
public void SetProjection(ProjectionType type)
{
    _activeRenderer?.Hide();
    _activeRenderer = _renderers[type];
    _activeRenderer.Show();

    // Environment visibility
    bool showEnvironment = (type == ProjectionType.Flat ||
                            type == ProjectionType.SideBySide3D ||
                            type == ProjectionType.OverUnder3D);

    EnvironmentManager.Instance.SetVisible(showEnvironment);

    // Screen settings chỉ available cho Flat mode
    OnProjectionChanged?.Invoke(type);
}
```

---

## 4. Media Library UI

### 4.1 Layout Overview (3-Panel)

```
┌──────────────────────────────────────────────────────────────────────────────┐
│                              HEADER (75px)                                    │
│ [X] [Sort▼] [_______________Search_______________] [+ Playlist] [Grid][List][E] │
├────────────────┬─────────────────────────────────────┬───────────────────────┤
│                │                                     │                       │
│   LEFT PANEL   │           CENTER PANEL              │     RIGHT PANEL       │
│    (400px)     │            (1120px)                 │       (400px)         │
│                │                                     │                       │
│   Categories   │         Video Grid/List             │    Video Detail       │
│   Filters      │         Pagination                  │    Actions            │
│                │                                     │                       │
└────────────────┴─────────────────────────────────────┴───────────────────────┘
         Total Width: 1920px (logical)
```

### 4.2 Left Panel - Navigation (400px)

```
┌────────────────────────────────────┐
│          MEDIA LIBRARY             │  40px font, bold
├────────────────────────────────────┤
│                                    │
│  [●] All Videos _______________    │  60px height each
│                                    │  Selection: 11x72px capsule
│  [ ] Recent ___________________    │  Icon: 42x42px
│                                    │  Text: 32px bold
│  [ ] Favorites _________________   │
│                                    │
│  [ ] Playlists ▶                   │  Expandable
│      ├── My Playlist 1             │
│      ├── VR Favorites              │
│      └── [+] New Playlist          │
│                                    │
├────────────────────────────────────┤
│            FILTERS                 │
├────────────────────────────────────┤
│                                    │
│  Format:   [All           ▼]       │  Dropdown filters
│                                    │  Options: All, MP4, MKV, AVI, WebM
│  Type:     [All           ▼]       │  Options: All, 2D, 3D SBS, 3D OU,
│                                    │           180°, 360°, VR180
│  Duration: [All           ▼]       │  Options: All, Short (<5min),
│                                    │           Medium (5-30min), Long (>30min)
└────────────────────────────────────┘
```

### 4.3 Center Panel - Video Grid (1120px)

#### Header Row 1 (75px)
```
[X] [Sort: Name ▼] [___________Search___________] [+ Playlist] [Grid][List][Edit]
 75    220              ~600                         220        75x2   75
```

#### Header Row 2 (50px)
```
All Videos > Current Folder                                    42 videos [Refresh]
Breadcrumbs                                                    Count     Icon 50x50
```

#### Grid View

```
┌───────────────────┐  ┌───────────────────┐  ┌───────────────────┐
│                   │  │                   │  │                   │
│    [Thumbnail]    │  │    [Thumbnail]    │  │    [Thumbnail]    │
│      16:9         │  │      16:9         │  │      16:9         │
│                   │  │                   │  │                   │
│ [360]      2:34:15│  │ [VR]       1:45:30│  │           0:45:00 │
└───────────────────┘  └───────────────────┘  └───────────────────┘
│ Video Title...    │  │ Another Video...  │  │ Standard Video... │
└───────────────────┘  └───────────────────┘  └───────────────────┘
     395 x 320px            395 x 320px            395 x 320px

     Grid: 3 columns, 2 rows visible (6 items per page)
```

### 4.4 Right Panel - Video Detail (400px)

```
┌────────────────────────────────────┐
│                                    │
│  << Video Title Scrolling...       │  Marquee, 36px bold
│                                    │
├────────────────────────────────────┤
│                                    │
│  ┌──────────────────────────────┐  │
│  │                              │  │
│  │      [Large Preview]         │  │  512px thumbnail
│  │         16:9                 │  │  No overlay
│  │                              │  │
│  └──────────────────────────────┘  │
│                                    │
│  ┌──────────────────────────────┐  │
│  │      ▶  PLAY NOW             │  │  ConnectButton style
│  └──────────────────────────────┘  │  80px height, pulsing glow
│                                    │
├────────────────────────────────────┤
│                                    │
│  Format:      MP4 (H.265)          │  Metadata rows
│  Resolution:  3840 x 2160          │  44px height each
│  Projection:  360° Equirectangular │
│  Duration:    2:34:15              │
│  Size:        8.5 GB               │
│  Date Added:  Jan 15, 2026         │
│                                    │
├────────────────────────────────────┤
│                                    │
│  [★] Add to Favorites              │  Action buttons
│  [+] Add to Playlist...            │  60px height each
│                                    │
├────────────────────────────────────┤
│                                    │
│  RELATED VIDEOS                    │  Section header
│  ┌────┐ ┌────┐ ┌────┐              │
│  │ th │ │ th │ │ th │              │  Small thumbnails
│  └────┘ └────┘ └────┘              │  100x75px each
│                                    │
└────────────────────────────────────┘
```

### 4.5 Projection Badges

| Badge | Text | Color | Condition |
|-------|------|-------|-----------|
| 360° | `[360]` | `#FF8000` (Orange) | Sphere360 |
| 180° | `[180]` | `#9933FF` (Purple) | Dome180 |
| 3D | `[3D]` | `#00E5FF` (Cyan) | SBS/OU |
| VR | `[VR]` | `#4DFF80` (Green) | VR180Stereo |
| (none) | - | - | Flat |

### 4.6 Video Grid Item Component

```csharp
public class RTTMediaGridItem : MonoBehaviour
{
    // Visual Elements
    private Image _thumbnailImage;          // 395x222 (16:9)
    private Image _projectionBadge;         // Bottom-left corner
    private TextMeshProUGUI _badgeText;     // "360", "180", etc.
    private TextMeshProUGUI _durationText;  // Bottom-right, "2:34:15"
    private TextMeshProUGUI _titleText;     // Below thumbnail, 2 lines max

    // Edit Mode
    private GameObject _checkbox;
    private Image _checkmark;

    // State
    private bool _isSelected;
    private bool _isHovered;

    // Data
    public MediaVideoInfo VideoInfo { get; private set; }

    // Events
    public event Action<MediaVideoInfo> OnClicked;
    public event Action<MediaVideoInfo> OnDoubleClicked;
    public event Action<MediaVideoInfo> OnLongPressed;
}
```

---

## 5. Playback Controls

### 5.1 Main Control Bar

```
┌────────────────────────────────────────────────────────────────────────────────┐
│                                                                                │
│  [<<]  [▶]  [>>]  │ [━━━━━━━━━━━●━━━━━━━━━━━━━━━] 12:34 / 45:00  [🔊] [1x] [⚙] [⛶] │
│                                                                                │
└────────────────────────────────────────────────────────────────────────────────┘
   Prev  Play  Next       Seek Timeline             Time      Vol Spd Set Full
```

### 5.2 Control Sizes (VR-optimized)

| Control | Visual Size | Touch Target | Notes |
|---------|-------------|--------------|-------|
| Play/Pause | 80x80px | 100x100px | Center, largest |
| Prev/Next | 60x60px | 80x80px | Flanking play |
| Seek Slider | 800x48px | 800x80px | Extended touch height |
| Time Display | 180x44px | - | Read-only |
| Volume | 50x50px | 70x70px | BareIconButton |
| Speed | 50x50px | 70x70px | Shows current (1x) |
| Settings | 50x50px | 70x70px | BareIconButton |
| Fullscreen | 50x50px | 70x70px | BareIconButton |

### 5.3 Timeline Features

```
                    Thumbnail Preview (240x135)
                         ┌─────────────┐
                         │    12:34    │
                         │   [frame]   │
                         └──────┬──────┘
                                │
[Buffer ████████████████████░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░]
[Progress ████████████████●━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━]
                         ▲
                      Thumb

      ▼ Chapter      ▼ Chapter                    ▼ Chapter
      │              │                            │

Legend:
████ = Progress (played)
░░░░ = Buffer (loaded)
━━━━ = Track (remaining)
● = Thumb (draggable)
▼ = Chapter markers
```

### 5.4 Settings Panel

```
┌───────────────────────────────────────┐
│          VIDEO SETTINGS               │  48px title
├───────────────────────────────────────┤
│                                       │
│  Quality      [Auto           ▼]      │  Dropdown
│                                       │  Options: Auto, 4K, 1080p, 720p
│  Audio        [Track 1 (English) ▼]   │  Dynamic from video
│                                       │
│  Subtitles    [Off            ▼]      │  Dynamic + Off option
│                                       │
├───────────────────────────────────────┤
│                                       │
│  Projection   [Auto Detected  ▼]      │  2D, 180°, 360°, etc.
│                                       │
├───────────────────────────────────────┤  << Chỉ hiện khi Flat mode >>
│                                       │
│  Screen Size  [━━━━●━━━━━━━]  1.2x    │  Slider + value
│                                       │
│  Distance     [━━━━━━●━━━━━]  2.0m    │  Slider + value
│                                       │
│  Curvature    [━●━━━━━━━━━━]  0.2     │  Slider + value
│                                       │
├───────────────────────────────────────┤  << Chỉ hiện khi Flat mode >>
│                                       │
│  [💡] Environment Lights  [ON/OFF]    │  Toggle existing lights
│                                       │
├───────────────────────────────────────┤
│                                       │
│         [Apply]     [Reset]           │  Action buttons
│                                       │
└───────────────────────────────────────┘
     600 x ~720 pixels
```

### 5.5 Auto-hide Behavior

```csharp
public class ControlsVisibilityManager
{
    public enum VisibilityState { Hidden, FadingIn, Visible, FadingOut }

    private const float HIDE_DELAY = 3.0f;      // Seconds of inactivity
    private const float FADE_IN_DURATION = 0.3f;
    private const float FADE_OUT_DURATION = 0.5f;

    public VisibilityState CurrentState { get; private set; }
    public bool IsLocked { get; set; }          // Always visible option

    // Triggers
    public void OnUserActivity();               // Show controls
    public void OnVideoPlaying();               // Start hide timer
    public void OnVideoPaused();                // Keep visible
}
```

### 5.6 VRSliderFactory (New Component)

```csharp
/// <summary>
/// Factory class cho VR-optimized sliders - timeline, volume, settings
/// </summary>
public static class VRSliderFactory
{
    [Serializable]
    public class SliderConfig
    {
        public float width = 600f;
        public float height = 48f;
        public float trackHeight = 12f;
        public float thumbSize = 40f;

        public Color trackColor = new Color(0.2f, 0.2f, 0.25f, 0.8f);
        public Color fillColor = new Color(0f, 0.9f, 1f);      // Primary
        public Color bufferColor = new Color(0.4f, 0.4f, 0.45f, 0.6f);
        public Color thumbColor = Color.white;

        public float cornerRadius = 0.5f;   // Full rounded
        public float glowWidth = 0.08f;
        public float glowIntensity = 3.5f;

        public bool showThumb = true;
        public bool showBuffer = true;      // For video buffering

        public string layerName = "VirtualObjects";
        public TMP_FontAsset font;
    }

    public static GameObject CreateSlider(Transform parent, SliderConfig config,
        Action<float> onValueChanged = null);

    public static GameObject CreateSeekSlider(Transform parent, float width,
        Color fillColor, Action<float> onSeek = null);

    public static GameObject CreateVolumeSlider(Transform parent, float width,
        Color fillColor, Action<float> onVolumeChanged = null);
}
```

---

## 6. Data Models

### 6.1 MediaVideoInfo

```csharp
[Serializable]
public struct MediaVideoInfo
{
    // Identity
    public string Path;
    public string Title;

    // Video Properties
    public TimeSpan Duration;
    public VideoProjectionType Projection;
    public VideoFormat Format;
    public int Width;
    public int Height;
    public float FrameRate;
    public long FileSizeBytes;

    // Timestamps
    public DateTime DateAdded;
    public DateTime DateModified;
    public DateTime LastPlayed;

    // User Data
    public bool IsFavorite;
    public List<string> PlaylistIds;
    public TimeSpan LastPosition;       // Resume playback

    // Cached
    public Texture2D Thumbnail;

    // Computed
    public string FormattedDuration => FormatDuration(Duration);
    public string FormattedSize => FormatFileSize(FileSizeBytes);
    public string FormattedResolution => $"{Width} x {Height}";
}

public enum VideoFormat
{
    Unknown,
    MP4,
    MKV,
    AVI,
    WebM,
    MOV,
    WMV
}
```

### 6.2 MediaPlaylist

```csharp
[Serializable]
public class MediaPlaylist
{
    public string Id;
    public string Name;
    public Sprite CustomIcon;           // Optional
    public List<string> VideoPaths;
    public DateTime Created;
    public DateTime Modified;
    public PlaylistSettings Settings;

    public int VideoCount => VideoPaths?.Count ?? 0;
}

[Serializable]
public class PlaylistSettings
{
    public bool AutoPlay = true;
    public bool ShuffleEnabled = false;
    public RepeatMode Repeat = RepeatMode.None;
}

public enum RepeatMode
{
    None,
    RepeatOne,
    RepeatAll
}
```

### 6.3 SubtitleData

```csharp
[Serializable]
public struct SubtitleTrack
{
    public int Index;
    public string Language;
    public string Label;
    public string FilePath;         // .srt file
}

[Serializable]
public struct SubtitleCue
{
    public int Index;
    public TimeSpan StartTime;
    public TimeSpan EndTime;
    public string Text;
}

public class SrtParser
{
    public static List<SubtitleCue> Parse(string srtContent);
    public static async Task<List<SubtitleCue>> ParseFileAsync(string filePath);
}
```

---

## 7. Shaders

### 7.1 VideoFlatProjection.shader

```hlsl
Shader "VRWorkspace/Media/VideoFlatProjection"
{
    Properties
    {
        _MainTex ("Video Texture", 2D) = "black" {}
        _Curvature ("Curvature", Range(0, 1)) = 0
        _Brightness ("Brightness", Range(0, 2)) = 1
        _Contrast ("Contrast", Range(0, 2)) = 1
        _Saturation ("Saturation", Range(0, 2)) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            // Vertex: Apply cylindrical curvature to flat quad
            // Fragment: Sample video, apply color correction

            ENDCG
        }
    }
}
```

### 7.2 Video180Dome.shader

```hlsl
Shader "VRWorkspace/Media/Video180Dome"
{
    Properties
    {
        _MainTex ("Video Texture", 2D) = "black" {}
        _StereoMode ("Stereo Mode", Float) = 0      // 0=Mono, 1=SBS, 2=OU
        _EyeIndex ("Eye Index", Float) = 0          // 0=Left, 1=Right
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Background" }
        Cull Front  // Render inside

        Pass
        {
            CGPROGRAM
            // View direction to spherical coordinates
            // Map to UV (only half sphere: -90° to +90° horizontal)
            // Handle stereoscopic UV offset based on eye
            ENDCG
        }
    }
}
```

### 7.3 Video360Sphere.shader

```hlsl
Shader "VRWorkspace/Media/Video360Sphere"
{
    Properties
    {
        _MainTex ("Video Texture", 2D) = "black" {}
        _StereoMode ("Stereo Mode", Float) = 0
        _EyeIndex ("Eye Index", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Background" }
        Cull Front

        Pass
        {
            CGPROGRAM
            // Full spherical mapping
            // u = atan2(dir.z, dir.x) / (2*PI) + 0.5
            // v = asin(dir.y) / PI + 0.5
            ENDCG
        }
    }
}
```

### 7.4 VideoStereoscopic.shader

```hlsl
Shader "VRWorkspace/Media/VideoStereoscopic"
{
    Properties
    {
        _MainTex ("Video Texture", 2D) = "black" {}
        _StereoLayout ("Layout", Float) = 0         // 0=SBS, 1=OU
        _EyeIndex ("Eye Index", Float) = 0
        _SwapEyes ("Swap Eyes", Float) = 0
    }

    SubShader
    {
        Pass
        {
            CGPROGRAM
            // UV manipulation based on layout:
            // SBS: left eye u=[0, 0.5], right eye u=[0.5, 1]
            // OU:  left eye v=[0.5, 1], right eye v=[0, 0.5]
            ENDCG
        }
    }
}
```

### 7.5 VideoNV12ToRGB.shader

```hlsl
Shader "VRWorkspace/Media/VideoNV12ToRGB"
{
    Properties
    {
        _YTex ("Y Plane", 2D) = "black" {}
        _UVTex ("UV Plane", 2D) = "gray" {}
    }

    SubShader
    {
        Pass
        {
            CGPROGRAM
            // Sample Y from _YTex
            // Sample UV from _UVTex (interleaved)
            // Convert YUV to RGB using BT.709 matrix:
            // R = Y + 1.5748 * V
            // G = Y - 0.1873 * U - 0.4681 * V
            // B = Y + 1.8556 * U
            ENDCG
        }
    }
}
```

---

## 8. File Structure

```
Assets/VR-Workspace/
├── Scripts/
│   └── Media/
│       ├── Core/
│       │   ├── VRMediaAppController.cs         # Main app controller
│       │   ├── VideoPlaybackEngine.cs          # Video decoding
│       │   ├── VRVideoProjectionSystem.cs      # Projection management
│       │   ├── MediaLibraryService.cs          # File scanning/search
│       │   └── MediaPlaylistService.cs         # Playlist management
│       │
│       ├── Projections/
│       │   ├── IProjectionRenderer.cs          # Interface
│       │   ├── FlatProjectionRenderer.cs       # 2D flat/curved
│       │   ├── Dome180Renderer.cs              # 180° hemisphere
│       │   ├── Sphere360Renderer.cs            # 360° sphere
│       │   └── StereoscopicRenderer.cs         # SBS/OU/VR180
│       │
│       ├── UI/
│       │   ├── RTTMediaLibrary.cs              # Library main view
│       │   ├── RTTMediaLibraryController.cs    # Library logic
│       │   ├── RTTMediaSidePanel.cs            # Left panel
│       │   ├── RTTMediaGrid.cs                 # Video grid
│       │   ├── RTTMediaGridItem.cs             # Grid item
│       │   ├── RTTMediaDetail.cs               # Right panel
│       │   ├── RTTMediaControlsPanel.cs        # Playback controls
│       │   ├── RTTMediaSettingsPopup.cs        # Settings popup
│       │   └── RTTTimelineOverlay.cs           # Timeline features
│       │
│       ├── Subtitles/
│       │   ├── SubtitleManager.cs              # Load & sync
│       │   ├── SrtParser.cs                    # Parse .srt
│       │   └── SubtitleRenderer.cs             # VR display
│       │
│       ├── Data/
│       │   ├── MediaVideoInfo.cs               # Video data model
│       │   ├── VideoProjectionType.cs          # Projection enum
│       │   ├── MediaPlaylist.cs                # Playlist model
│       │   └── SubtitleData.cs                 # Subtitle structures
│       │
│       └── Utils/
│           ├── ProjectionDetector.cs           # Auto-detect projection
│           └── VRSliderFactory.cs              # Slider component
│
├── Shaders/
│   └── Media/
│       ├── VideoFlatProjection.shader
│       ├── Video180Dome.shader
│       ├── Video360Sphere.shader
│       ├── VideoStereoscopic.shader
│       └── VideoNV12ToRGB.shader
│
└── Resources/
    └── Media/
        └── ProjectionMeshes/
            ├── Dome180.mesh                    # Pre-generated
            └── Sphere360.mesh                  # Pre-generated
```

---

## 9. Implementation Phases

### Phase 1: Core Infrastructure (8 files)

**Mục tiêu**: Có thể mở Media app và phát video 2D cơ bản

| # | File | Description |
|---|------|-------------|
| 1 | `VRMediaAppController.cs` | Main app lifecycle |
| 2 | `VideoPlaybackEngine.cs` | Unity VideoPlayer wrapper |
| 3 | `FlatProjectionRenderer.cs` | Basic 2D display |
| 4 | `VideoFlatProjection.shader` | Flat video shader |
| 5 | `IProjectionRenderer.cs` | Interface |
| 6 | `MediaVideoInfo.cs` | Data model |
| 7 | `VideoProjectionType.cs` | Enum |
| 8 | Update `RTTManager.cs` | Add Media case in CreateAppContent() |

**Verification**:
- [ ] Open Media app từ main menu
- [ ] Load và play video local
- [ ] Basic playback controls (play/pause)

### Phase 2: Media Library UI (6 files)

**Mục tiêu**: Có thể duyệt và chọn video

| # | File | Description |
|---|------|-------------|
| 1 | `RTTMediaLibrary.cs` | Main library view |
| 2 | `RTTMediaLibraryController.cs` | Logic controller |
| 3 | `RTTMediaGrid.cs` | Video grid |
| 4 | `RTTMediaGridItem.cs` | Grid item component |
| 5 | `RTTMediaDetail.cs` | Detail panel |
| 6 | `MediaLibraryService.cs` | File scanning |

**Verification**:
- [ ] Scan và hiển thị danh sách video
- [ ] Grid view với thumbnails
- [ ] Click video để xem detail
- [ ] Double-click để play

### Phase 3: VR Projections (5 files)

**Mục tiêu**: Hỗ trợ video 180° và 360°

| # | File | Description |
|---|------|-------------|
| 1 | `VRVideoProjectionSystem.cs` | Projection manager |
| 2 | `Dome180Renderer.cs` | 180° projection |
| 3 | `Sphere360Renderer.cs` | 360° projection |
| 4 | `Video180Dome.shader` | 180° shader |
| 5 | `Video360Sphere.shader` | 360° shader |

**Verification**:
- [ ] Phát video 180° với dome projection
- [ ] Phát video 360° với sphere projection
- [ ] Auto-detect projection từ filename
- [ ] Environment hidden khi VR mode

### Phase 4: Playback Controls (5 files)

**Mục tiêu**: Điều khiển đầy đủ và settings

| # | File | Description |
|---|------|-------------|
| 1 | `VRSliderFactory.cs` | Slider component |
| 2 | `RTTMediaControlsPanel.cs` | Controls UI |
| 3 | `RTTMediaSettingsPopup.cs` | Settings popup |
| 4 | `RTTTimelineOverlay.cs` | Timeline features |
| 5 | `VRVideoPlayerController.cs` | Playback controller |

**Verification**:
- [ ] Play/Pause/Seek hoạt động
- [ ] Volume control
- [ ] Speed control (0.5x - 2x)
- [ ] Settings panel
- [ ] Auto-hide controls

### Phase 5: Stereoscopic (3 files)

**Mục tiêu**: Hỗ trợ video 3D

| # | File | Description |
|---|------|-------------|
| 1 | `StereoscopicRenderer.cs` | SBS/OU/VR180 |
| 2 | `VideoStereoscopic.shader` | 3D shader |
| 3 | `VideoNV12ToRGB.shader` | HEVC support |

**Verification**:
- [ ] Phát video SBS 3D
- [ ] Phát video OU/TB 3D
- [ ] Phát video VR180
- [ ] Eye swap option

### Phase 6: Subtitles & Playlists (6 files)

**Mục tiêu**: Features bổ sung

| # | File | Description |
|---|------|-------------|
| 1 | `SubtitleManager.cs` | Load & sync |
| 2 | `SrtParser.cs` | Parse .srt |
| 3 | `SubtitleRenderer.cs` | VR display |
| 4 | `MediaPlaylistService.cs` | Playlist management |
| 5 | `MediaPlaylist.cs` | Playlist model |
| 6 | `RTTMediaSidePanel.cs` | Add playlist UI |

**Verification**:
- [ ] Load và hiển thị phụ đề SRT
- [ ] Tạo/sửa/xóa playlist
- [ ] Play playlist với auto-next

---

## 10. Testing & Verification

### 10.1 Unit Tests

```csharp
// ProjectionDetectorTests.cs
[Test] public void DetectProjection_360InFilename_ReturnsSphere360()
[Test] public void DetectProjection_SBSInFilename_ReturnsSideBySide3D()
[Test] public void DetectProjection_2To1Ratio_ReturnsSphere360()

// SrtParserTests.cs
[Test] public void Parse_ValidSrt_ReturnsCues()
[Test] public void Parse_MultilineCue_PreservesNewlines()
[Test] public void Parse_InvalidTimestamp_SkipsCue()

// MediaPlaylistServiceTests.cs
[Test] public void CreatePlaylist_ValidName_ReturnsPlaylist()
[Test] public void AddToPlaylist_ValidPath_AddsVideo()
[Test] public void GetNextVideo_LastInList_ReturnsNull()
```

### 10.2 Integration Tests

```csharp
// VideoPlaybackTests.cs
[UnityTest] public IEnumerator LoadVideo_ValidPath_PreparesSuccessfully()
[UnityTest] public IEnumerator Seek_MidVideo_UpdatesPosition()
[UnityTest] public IEnumerator SwitchProjection_180To360_ChangesRenderer()
```

### 10.3 VR Testing Checklist

| Test Case | Expected Result |
|-----------|-----------------|
| Open Media app | App mở, hiển thị library |
| Browse videos | Grid hiển thị thumbnails |
| Search videos | Filter hoạt động |
| Play 2D video | Video phát, controls hiển thị |
| Play 180° video | Dome projection, no environment |
| Play 360° video | Sphere projection, immersive |
| Play 3D SBS | Stereo effect đúng |
| Seek video | Seek smooth, thumbnail preview |
| Volume control | Âm lượng thay đổi |
| Subtitle | Phụ đề hiển thị đúng timing |
| Settings | Tất cả settings hoạt động |
| Screen distance (Flat) | Screen di chuyển |
| Lights toggle (Flat) | Đèn bật/tắt |
| Favorites | Add/remove hoạt động |
| Playlist | CRUD hoạt động |
| Auto-hide controls | Ẩn sau 3s, hiện khi tương tác |
| Performance | Stable 72fps |
| Memory | < 100MB video buffer |

### 10.4 Performance Targets

| Metric | Target |
|--------|--------|
| Frame rate | ≥ 72fps stable |
| Video texture memory | < 100MB (4K) |
| Thumbnail cache | < 50MB |
| UI render | < 2ms/frame |
| Video load time | < 2s (local file) |
| Seek latency | < 500ms |

---

## Appendix A: Integration Points

### A.1 RTTManager.CreateAppContent()

```csharp
// Thêm vào RTTManager.cs
private void CreateAppContent(RTTAppInstance instance)
{
    var appType = appRegistry?.GetAppType(instance.AppId);

    switch (appType)
    {
        case RTTAppRegistry.AppType.Remote:
            CreateRemoteMenuContent(instance);
            break;
        case RTTAppRegistry.AppType.Files:
            CreateFilesMenuContent(instance);
            break;
        case RTTAppRegistry.AppType.Media:            // <-- Thêm
            CreateMediaContent(instance);              // <-- Thêm
            break;
        // ...
    }
}

private void CreateMediaContent(RTTAppInstance instance)
{
    GameObject controllerObj = new GameObject($"MediaController_{instance.AppId}");
    controllerObj.transform.SetParent(this.transform);
    var controller = controllerObj.AddComponent<VRMediaAppController>();

    var containerSize = instance.Frame.GetContentSize();
    instance.MenuContent = controller.CreateMenu(
        instance.Frame.ContentContainer,
        containerSize.x,
        containerSize.y,
        Font,
        PrimaryColor,
        AccentColor
    );

    instance.Controller = controller;
    controller.OnBackClicked += () => CloseApp(instance.AppId);
    instance.Frame.MarkDirty();
}
```

### A.2 Reuse Existing Components

| Component | Sử dụng trong Media App |
|-----------|-------------------------|
| `VRButtonFactory` | Tất cả buttons |
| `VRDropdownFactory` | Filter dropdowns, settings |
| `RTTPopupMenu` | Settings panel |
| `FileThumbnailService` | Video thumbnails |
| `VideoFrameExtractor` | Timeline previews |
| `FileMetadataService` | Video metadata |
| `HevcDecoderPlugin` | Hardware HEVC decode |
| `WorldPanelPlus` | Reference for flat projection |

---

## Appendix B: Color Scheme

```csharp
// Following RTTThemeConfig
primaryColor = new Color(0f, 0.9f, 1f);        // Cyan
accentColor = new Color(0.76f, 0.36f, 1f);     // Purple

// Badges
badge360 = new Color(1f, 0.5f, 0f);            // Orange
badge180 = new Color(0.6f, 0.2f, 1f);          // Purple
badge3D = new Color(0f, 0.9f, 1f);             // Cyan
badgeVR = new Color(0.3f, 1f, 0.5f);           // Green

// Glass
glassColorA = new Color(0f, 0.1f, 0.15f, 0.85f);
glassColorB = new Color(0.05f, 0.15f, 0.2f, 0.75f);

// Text
textPrimary = Color.white;
textSecondary = new Color(1f, 1f, 1f, 0.7f);
textDisabled = new Color(1f, 1f, 1f, 0.4f);
```

---

*End of specification document*
