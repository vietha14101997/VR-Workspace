using UnityEngine;

public class MainMenuButton : MonoBehaviour
{
    public string label;
    public Color normalColor = new Color(0.2f, 0.4f, 0.8f, 1f);
    public Color hoverColor = new Color(0.3f, 0.5f, 0.9f, 1f);
    public Material buttonMaterial;
    public MainMenuPanel menuPanel;

    [Header("Gaze Dwell")]
    public float dwellTime = 1.5f;

    private float _dwellTimer = 0f;
    private bool _isHovered = false;
    private Camera _vrCamera;
    private Renderer _bgRenderer;

    void Start()
    {
        _vrCamera = Camera.main;
        _bgRenderer = GetComponentInChildren<MeshRenderer>();
        if (_bgRenderer && buttonMaterial == null)
            buttonMaterial = _bgRenderer.material;
    }

    void Update()
    {
        if (_vrCamera == null) _vrCamera = Camera.main;
        if (_vrCamera == null) return;

        Ray ray = new Ray(_vrCamera.transform.position, _vrCamera.transform.forward);
        RaycastHit hit;
        bool wasHovered = _isHovered;

        if (Physics.Raycast(ray, out hit, 10f))
        {
            _isHovered = (hit.collider.gameObject == gameObject || hit.collider.transform.IsChildOf(transform));
        }
        else
        {
            _isHovered = false;
        }

        if (_isHovered)
        {
            _dwellTimer += Time.deltaTime;
            float progress = Mathf.Clamp01(_dwellTimer / dwellTime);
            SetHoverVisual(progress);

            if (_dwellTimer >= dwellTime)
            {
                OnClick();
                _dwellTimer = 0f;
            }
        }
        else
        {
            _dwellTimer = 0f;
            SetHoverVisual(0f);
        }

        // Tap/click support
        if (_isHovered && Input.GetMouseButtonDown(0))
        {
            OnClick();
        }

#if UNITY_ANDROID
        try
        {
            if (_isHovered && Google.XR.Cardboard.Api.IsTriggerPressed)
            {
                OnClick();
            }
        }
        catch { }
#endif
    }

    void SetHoverVisual(float progress)
    {
        if (buttonMaterial == null) return;

        Color targetColor = Color.Lerp(normalColor, hoverColor, progress);
        // Add green tint as dwell progresses
        if (progress > 0.5f)
        {
            float greenLerp = (progress - 0.5f) * 2f;
            targetColor = Color.Lerp(targetColor, Color.green, greenLerp * 0.5f);
        }
        buttonMaterial.color = targetColor;
    }

    void OnClick()
    {
        if (menuPanel != null)
        {
            menuPanel.OnButtonClicked(label);
        }
    }
}
