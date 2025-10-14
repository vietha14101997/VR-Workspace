using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Nhận stream duy nhất từ server và chia thành 6 mảnh riêng biệt
/// Chịu trách nhiệm decoding và tạo texture cho từng mảnh
/// </summary>
public class ClusterStreamDecoder : MonoBehaviour
{
    [Header("Texture Configuration")]
    // Tổng kích thước texture từ server: 4082x1532
    public const float TOTAL_WIDTH = 4082f;
    public const float TOTAL_HEIGHT = 1532f;

    // Kích thước mỗi ô: 1360x765, rãnh 1px
    public const float CELL_WIDTH = 1360f;
    public const float CELL_HEIGHT = 765f;
    public const float SEPARATOR = 1f;
    public const float ROW_SEPARATOR = 1f;

    [Header("Debug")]
    public bool debugLog = true;

    // Texture gốc nhận từ server
    private Texture _sourceTexture;

    // 6 texture con cho từng mảnh (0-5)
    private readonly Texture2D[] _pieceTextures = new Texture2D[6];

    // Events
    public System.Action<Texture2D> OnPieceTextureReady;

    void Awake()
    {
        InitializePieceTextures();
    }

    /// <summary>
    /// Khởi tạo 6 texture rỗng cho các mảnh
    /// </summary>
    void InitializePieceTextures()
    {
        for (int i = 0; i < 6; i++)
        {
            _pieceTextures[i] = new Texture2D((int)CELL_WIDTH, (int)CELL_HEIGHT, TextureFormat.RGBA32, false);
            _pieceTextures[i].name = $"ClusterPiece_{i}";
        }

        if (debugLog) Debug.Log("[ClusterStreamDecoder] Initialized 6 piece textures");
    }

    /// <summary>
    /// Nhận texture gốc từ PCStreamClient
    /// </summary>
    public void SetSourceTexture(Texture sourceTexture)
    {
        if (_sourceTexture == sourceTexture) return;

        _sourceTexture = sourceTexture;
        if (debugLog && sourceTexture != null)
            Debug.Log($"[ClusterStreamDecoder] Source texture set: {sourceTexture.width}x{sourceTexture.height}");
    }

    /// <summary>
    /// Cắt texture gốc thành 6 mảnh và cập nhật các piece textures
    /// </summary>
    public void UpdatePieceTextures()
    {
        if (_sourceTexture == null)
        {
            if (debugLog) Debug.LogWarning("[ClusterStreamDecoder] No source texture available");
            return;
        }

        // Chỉ cắt khi có thay đổi
        if (Time.frameCount % 2 == 0) // Giới hạn tần suất cắt để tối ưu
        {
            CropAllPieces();
        }
    }

    /// <summary>
    /// Cắt tất cả 6 mảnh từ texture gốc
    /// </summary>
    void CropAllPieces()
    {
        try
        {
            // Lấy pixel data từ source texture
            var sourceTex2D = _sourceTexture as Texture2D;
            if (sourceTex2D == null)
            {
                if (debugLog) Debug.LogWarning("[ClusterStreamDecoder] Source texture is not Texture2D");
                return;
            }

            // Cắt từng mảnh theo layout 2x3
            for (int row = 0; row < 2; row++)
            {
                for (int col = 0; col < 3; col++)
                {
                    int pieceIndex = row * 3 + col;
                    CropSinglePiece(sourceTex2D, pieceIndex, row, col);
                }
            }

            // Thông báo có texture mới
            OnPieceTextureReady?.Invoke(_pieceTextures[0]);

            if (debugLog && Time.frameCount % 60 == 0) // Log mỗi giây
                Debug.Log("[ClusterStreamDecoder] Updated 6 piece textures");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[ClusterStreamDecoder] Error cropping pieces: {e.Message}");
        }
    }

    /// <summary>
    /// Cắt một mảnh cụ thể từ texture gốc
    /// </summary>
    void CropSinglePiece(Texture2D sourceTex, int pieceIndex, int row, int col)
    {
        // Tính toán tọa độ cắt
        int startX = col * ((int)CELL_WIDTH + (int)SEPARATOR);
        int startY = row * ((int)CELL_HEIGHT + (int)ROW_SEPARATOR);

        // Adjust cho hàng trên (nội dung ở dưới cùng của texture)
        startY = (int)TOTAL_HEIGHT - (startY + (int)CELL_HEIGHT);

        // Đảm bảo không vượt quá biên
        startX = Mathf.Min(startX, sourceTex.width - (int)CELL_WIDTH);
        startY = Mathf.Min(startY, sourceTex.height - (int)CELL_HEIGHT);
        startX = Mathf.Max(0, startX);
        startY = Mathf.Max(0, startY);

        // Cắt pixels
        var pixels = sourceTex.GetPixels(startX, startY, (int)CELL_WIDTH, (int)CELL_HEIGHT);
        _pieceTextures[pieceIndex].SetPixels(pixels);
        _pieceTextures[pieceIndex].Apply();
    }

    /// <summary>
    /// Lấy texture cho một mảnh cụ thể
    /// </summary>
    public Texture2D GetPieceTexture(int pieceIndex)
    {
        if (pieceIndex < 0 || pieceIndex >= 6)
        {
            Debug.LogError($"[ClusterStreamDecoder] Invalid piece index: {pieceIndex}");
            return null;
        }
        return _pieceTextures[pieceIndex];
    }

    /// <summary>
    /// Lấy tất cả piece textures
    /// </summary>
    public Texture2D[] GetAllPieceTextures()
    {
        return (Texture2D[])_pieceTextures.Clone();
    }

    void Update()
    {
        // Cập nhật cutting mỗi frame
        UpdatePieceTextures();
    }

    void OnDestroy()
    {
        // Cleanup textures
        for (int i = 0; i < 6; i++)
        {
            if (_pieceTextures[i] != null)
            {
                DestroyImmediate(_pieceTextures[i]);
            }
        }
    }
}
