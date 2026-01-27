/// <summary>
/// Enum các loại projection cho VR video
/// </summary>
public enum VideoProjectionType
{
    /// <summary>Standard 2D video trên flat screen</summary>
    Flat,

    /// <summary>180 degree equirectangular (half-dome)</summary>
    Dome180,

    /// <summary>360 degree equirectangular (full sphere)</summary>
    Sphere360,

    /// <summary>Side-by-Side stereoscopic 3D</summary>
    SideBySide3D,

    /// <summary>Over-Under (Top-Bottom) stereoscopic 3D</summary>
    OverUnder3D,

    /// <summary>VR180 stereoscopic (SBS + 180 dome)</summary>
    VR180Stereo
}

/// <summary>
/// Stereo mode for 3D video playback
/// </summary>
public enum StereoMode
{
    /// <summary>Mono - same image for both eyes</summary>
    Mono,

    /// <summary>Left eye only</summary>
    LeftEye,

    /// <summary>Right eye only</summary>
    RightEye,

    /// <summary>Full stereo - different images for each eye</summary>
    Stereo
}

/// <summary>
/// Video file format
/// </summary>
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

/// <summary>
/// Duration range filter
/// </summary>
public enum DurationRange
{
    All,
    Short,      // < 5 minutes
    Medium,     // 5-30 minutes
    Long        // > 30 minutes
}
