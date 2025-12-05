using UnityEngine;

/// <summary>
/// Nhận texture từ PCStreamClient master và crop UV cho panel này
/// </summary>
public class UVCropReceiver : MonoBehaviour
{
    [Header("Source")]
    public PCStreamClient sourceClient;
    public WorldPanelPlus worldPanel;

    [Header("Grid Position")]
    public int gridCol = 0;
    public int gridRow = 0;
    public int gridCols = 3;
    public int gridRows = 2;

    [Header("Frame & Cell dimensions")]
    public int frameWidth = 4082;
    public int frameHeight = 1532;
    public int cellWidth = 1360;
    public int cellHeight = 765;
    public int gapPixels = 1;

    private RenderTexture _croppedRT;
    private Texture _lastSourceTex;

    void Update()
    {
        if (sourceClient == null || worldPanel == null) return;

        var sourceTex = sourceClient.GetRawTexture();
        if (sourceTex == null) return;

        // Luôn crop mỗi frame (texture có thể thay đổi nội dung)
        var cropped = GetCroppedTexture(sourceTex);
        if (cropped != null)
        {
            if (!ReferenceEquals(_lastSourceTex, sourceTex))
            {
                worldPanel.contentTexture = cropped;
                worldPanel.Apply();
                _lastSourceTex = sourceTex;
            }
        }
    }

    Texture GetCroppedTexture(Texture source)
    {
        if (source == null) return null;

        // Tính vị trí pixel của cell trong frame
        int xStart = gridCol * (cellWidth + gapPixels);
        int yStart = gridRow * (cellHeight + gapPixels);

        // Tính UV scale và offset
        // Scale = kích thước cell / kích thước frame
        float scaleX = (float)cellWidth / frameWidth;
        float scaleY = (float)cellHeight / frameHeight;

        // Offset: vị trí bắt đầu trong UV space
        // UV origin (0,0) = bottom-left, nhưng image (0,0) = top-left
        // Nên offset.y = 1 - (yStart + cellHeight) / frameHeight
        float offsetX = (float)xStart / frameWidth;
        float offsetY = 1f - (float)(yStart + cellHeight) / frameHeight;

        // Tạo RenderTexture nếu cần
        if (_croppedRT == null || _croppedRT.width != cellWidth || _croppedRT.height != cellHeight)
        {
            if (_croppedRT != null)
            {
                _croppedRT.Release();
                Destroy(_croppedRT);
            }
            _croppedRT = new RenderTexture(cellWidth, cellHeight, 0, RenderTextureFormat.ARGB32);
            _croppedRT.filterMode = FilterMode.Bilinear;
            _croppedRT.Create();
        }

        // Dùng Graphics.Blit với scale/offset trực tiếp
        Graphics.Blit(source, _croppedRT, new Vector2(scaleX, scaleY), new Vector2(offsetX, offsetY));

        return _croppedRT;
    }

    void OnDestroy()
    {
        if (_croppedRT != null)
        {
            _croppedRT.Release();
            Destroy(_croppedRT);
        }
    }
}
