using UnityEngine;

/// <summary>
/// Interface cho các loại projection renderer
/// Mỗi projection type (Flat, 180, 360, etc.) sẽ implement interface này
/// </summary>
public interface IProjectionRenderer
{
    /// <summary>
    /// Loại projection mà renderer này xử lý
    /// </summary>
    VideoProjectionType Type { get; }

    /// <summary>
    /// Renderer đang active hay không
    /// </summary>
    bool IsActive { get; }

    /// <summary>
    /// Initialize renderer với parent transform
    /// </summary>
    /// <param name="parent">Parent transform để attach projection mesh</param>
    void Initialize(Transform parent);

    /// <summary>
    /// Set video texture để render
    /// </summary>
    /// <param name="texture">Video texture (RGB)</param>
    void SetTexture(Texture texture);

    /// <summary>
    /// Set video texture từ NV12 format (HEVC hardware decode)
    /// </summary>
    /// <param name="yPlane">Y plane texture</param>
    /// <param name="uvPlane">UV plane texture (interleaved)</param>
    void SetTextureNV12(Texture2D yPlane, Texture2D uvPlane);

    /// <summary>
    /// Set stereo mode cho 3D video
    /// </summary>
    /// <param name="mode">Stereo mode</param>
    void SetStereoMode(StereoMode mode);

    /// <summary>
    /// Update display settings (distance, scale, curvature)
    /// </summary>
    /// <param name="settings">Display settings</param>
    void UpdateDisplay(DisplaySettings settings);

    /// <summary>
    /// Show projection
    /// </summary>
    void Show();

    /// <summary>
    /// Hide projection
    /// </summary>
    void Hide();

    /// <summary>
    /// Recenter view (reset rotation offset)
    /// </summary>
    void RecenterView();

    /// <summary>
    /// Force monoscopic rendering: both eyes see the left eye's image.
    /// Used to temporarily disable 3D when UI overlays are visible.
    /// </summary>
    void SetForceMonoscopic(bool force);

    /// <summary>
    /// Cleanup và release resources
    /// </summary>
    void Dispose();
}

/// <summary>
/// Display settings cho projection renderer
/// </summary>
[System.Serializable]
public struct DisplaySettings
{
    /// <summary>Screen distance from viewer (meters)</summary>
    public float Distance;

    /// <summary>Screen scale multiplier</summary>
    public float Scale;

    /// <summary>Screen curvature (0 = flat, 1 = fully curved)</summary>
    public float Curvature;

    /// <summary>Lock screen position to head</summary>
    public bool HeadLocked;

    /// <summary>Position offset from default</summary>
    public Vector3 PositionOffset;

    /// <summary>Rotation offset from default</summary>
    public Quaternion RotationOffset;

    /// <summary>
    /// Default display settings
    /// </summary>
    public static DisplaySettings Default => new DisplaySettings
    {
        Distance = 2.0f,
        Scale = 1.0f,
        Curvature = 0.0f,
        HeadLocked = false,
        PositionOffset = Vector3.zero,
        RotationOffset = Quaternion.identity
    };

    /// <summary>
    /// Settings cho immersive VR (180/360)
    /// </summary>
    public static DisplaySettings Immersive => new DisplaySettings
    {
        Distance = 0f,
        Scale = 1.0f,
        Curvature = 1.0f,
        HeadLocked = false,
        PositionOffset = Vector3.zero,
        RotationOffset = Quaternion.identity
    };
}
