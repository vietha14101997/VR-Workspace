using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Phân phối texture đã được chia từ ClusterStreamDecoder tới các WorldPanel tương ứng
/// Quản lý binding cho layout 3 panels với mảnh 0,1,2
/// </summary>
public class ClusterStreamDistributor : MonoBehaviour
{
    [Header("Cluster Setup")]
    public WorldPanelClusterRig clusterRig;
    public ClusterStreamDecoder decoder;
    
    [Header("Piece Assignment")]
    // Mapping từ panel đến piece index
    public int leftPanelPiece = 0;
    public int centerPanelPiece = 1;
    public int rightPanelPiece = 2;
    
    [Header("Debug")]
    public bool debugLog = true;
    
    // Cache để tránh apply lại texture không cần thiết
    private readonly Dictionary<WorldPanelPlus, int> _panelToPieceMap = new Dictionary<WorldPanelPlus, int>();
    private Texture2D[] _lastAppliedTextures = new Texture2D[3];
    
    void Awake()
    {
        if (decoder == null) decoder = GetComponent<ClusterStreamDecoder>();
        if (clusterRig == null) clusterRig = GetComponent<WorldPanelClusterRig>();
    }
    
    void Start()
    {
        SetupPieceAssignment();
        SubscribeToDecoder();
    }
    
    /// <summary>
    /// Thiết lập mapping từ panel đến piece index
    /// </summary>
    void SetupPieceAssignment()
    {
        if (clusterRig == null)
        {
            Debug.LogError("[ClusterStreamDistributor] No cluster rig assigned");
            return;
        }
        
        // Clear existing mapping
        _panelToPieceMap.Clear();
        
        // Setup mapping for 3 panels
        if (clusterRig.left != null)
            _panelToPieceMap[clusterRig.left] = leftPanelPiece;
            
        if (clusterRig.center != null)
            _panelToPieceMap[clusterRig.center] = centerPanelPiece;
            
        if (clusterRig.right != null)
            _panelToPieceMap[clusterRig.right] = rightPanelPiece;
        
        if (debugLog)
            Debug.Log($"[ClusterStreamDistributor] Setup mapping: Left={leftPanelPiece}, Center={centerPanelPiece}, Right={rightPanelPiece}");
    }
    
    /// <summary>
    /// Đăng ký nhận sự kiện từ decoder
    /// </summary>
    void SubscribeToDecoder()
    {
        if (decoder != null)
        {
            decoder.OnPieceTextureReady += OnPieceTexturesUpdated;
        }
    }
    
    /// <summary>
    /// Callback khi decoder có texture mới
    /// </summary>
    void OnPieceTexturesUpdated(Texture2D pieceTexture)
    {
        DistributeTexturesToPanels();
    }
    
    /// <summary>
    /// Phân phối texture từ decoder tới các panel
    /// </summary>
    public void DistributeTexturesToPanels()
    {
        if (decoder == null) return;
        
        var allPieces = decoder.GetAllPieceTextures();
        
        foreach (var kvp in _panelToPieceMap)
        {
            var panel = kvp.Key;
            int pieceIndex = kvp.Value;
            
            if (panel == null || pieceIndex < 0 || pieceIndex >= allPieces.Length)
                continue;
            
            var pieceTexture = allPieces[pieceIndex];
            
            // Chỉ apply nếu texture thay đổi
            if (ShouldApplyTexture(panel, pieceTexture))
            {
                ApplyTextureToPanel(panel, pieceTexture);
            }
        }
    }
    
    /// <summary>
    /// Kiểm tra có nên apply texture mới không
    /// </summary>
    bool ShouldApplyTexture(WorldPanelPlus panel, Texture2D newTexture)
    {
        // Lấy index của panel trong mapping để cache
        if (!_panelToPieceMap.TryGetValue(panel, out int pieceIndex))
            return true;
        
        int arrayIndex = GetPanelArrayIndex(panel);
        var lastTexture = arrayIndex >= 0 && arrayIndex < _lastAppliedTextures.Length ? _lastAppliedTextures[arrayIndex] : null;
        
        // Apply nếu texture mới khác với texture cũ
        return !ReferenceEquals(lastTexture, newTexture);
    }
    
    /// <summary>
    /// Lấy index trong cache array cho một panel
    /// </summary>
    int GetPanelArrayIndex(WorldPanelPlus panel)
    {
        if (ReferenceEquals(panel, clusterRig?.left)) return 0;
        if (ReferenceEquals(panel, clusterRig?.center)) return 1;
        if (ReferenceEquals(panel, clusterRig?.right)) return 2;
        return -1;
    }
    
    /// <summary>
    /// Apply texture vào một panel cụ thể
    /// </summary>
    void ApplyTextureToPanel(WorldPanelPlus panel, Texture2D pieceTexture)
    {
        if (panel == null || pieceTexture == null) return;
        
        try
        {
            // Set content texture
            panel.contentTexture = pieceTexture;
            
            // Apply để update material
            panel.Apply();
            
            // Cache texture đã apply
            int arrayIndex = GetPanelArrayIndex(panel);
            if (arrayIndex >= 0 && arrayIndex < _lastAppliedTextures.Length)
            {
                _lastAppliedTextures[arrayIndex] = pieceTexture;
            }
            
            if (debugLog && Time.frameCount % 60 == 0) // Log mỗi giây
            {
                string panelName = ReferenceEquals(panel, clusterRig?.left) ? "Left" :
                                 ReferenceEquals(panel, clusterRig?.center) ? "Center" :
                                 ReferenceEquals(panel, clusterRig?.right) ? "Right" : "Unknown";
                Debug.Log($"[ClusterStreamDistributor] Applied piece texture to {panelName} panel");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[ClusterStreamDistributor] Error applying texture to panel: {e.Message}");
        }
    }
    
    /// <summary>
    /// Manual trigger để phân phối texture (cho testing)
    /// </summary>
    [ContextMenu("Force Distribute Textures")]
    public void ForceDistributeTextures()
    {
        DistributeTexturesToPanels();
    }
    
    /// <summary>
    /// Cập nhật mapping của pieces
    /// </summary>
    public void UpdatePieceAssignment(int left, int center, int right)
    {
        leftPanelPiece = left;
        centerPanelPiece = center;
        rightPanelPiece = right;
        
        SetupPieceAssignment();
        ForceDistributeTextures();
        
        if (debugLog)
            Debug.Log($"[ClusterStreamDistributor] Updated assignment: Left={left}, Center={center}, Right={right}");
    }
    
    void Update()
    {
        // Cập nhật liên tục để đảm bảo texture được phân phối
        if (Time.frameCount % 5 == 0) // Mỗi 5 frame
        {
            DistributeTexturesToPanels();
        }
    }
    
    void OnDestroy()
    {
        // Unsubscribe events
        if (decoder != null)
        {
            decoder.OnPieceTextureReady -= OnPieceTexturesUpdated;
        }
        
        // Cleanup cache
        for (int i = 0; i < _lastAppliedTextures.Length; i++)
        {
            _lastAppliedTextures[i] = null;
        }
    }
}
