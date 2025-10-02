using UnityEngine;
using UnityEngine.EventSystems;

/// Gaze bridge – phiên bản MOVE bằng handle_center, Dock không còn nút Move
public class WorldPanelGazeBridge : MonoBehaviour
{
    public Camera eventCamera;
    public LayerMask interactMask = ~0;
    public float maxDistance = 10f;

    [Header("Gaze dwell")]
    public bool useDwellActivation = true;
    public float dwellSeconds = 1.0f;
    public float dwellMaxAngle = 3.0f;

    [Header("Thoát khi giữ yên")]
    public float exitAfterSeconds = 2f;
    public float stillAngleDeg = 0.4f;

    WorldPanelPlus _hoverPanel;
    WorldPanelPlusHandle _hoverHandle;
    WorldPanelPlusHandle _activeHandle;   // đang kéo

    WorldPanelPlusHandle _candidateHandle; // cho dwell
    WorldPanelPlus _candidatePanel;        // cho dwell
    float _dwellTimer = 0f; Vector3 _dwellDir;

    bool _prevPressed = false; float _stillTimer = 0f; Vector3 _prevDir;
    Collider _lockoutCollider = null;

    void Reset() { eventCamera = GetComponent<Camera>(); }

    void Update()
    {
        if (!eventCamera) eventCamera = Camera.main; if (!eventCamera) return;

        Ray ray = new Ray(eventCamera.transform.position, eventCamera.transform.forward);
        var hits = Physics.RaycastAll(ray, maxDistance, interactMask.value);

        WorldPanelPlusHandle hitHandle = null; WorldPanelPlus hitPanel = null; Collider hitCol = null;
        int bestScore = int.MinValue; float bestDist = float.MaxValue;

        foreach (var h in hits)
        {
            var handle = h.collider.GetComponent<WorldPanelPlusHandle>();
            var board = h.collider.GetComponent<WorldPanelPlusBoardRaycatcher>();
            var hoverC = h.collider.GetComponent<WorldPanelPlusTrayRaycatcher>();

            if (handle)
            {
                int score = (handle.type == WPHandleType.Center) ? 3 : 4; // corner ưu tiên cao hơn chút
                if (score > bestScore || (score == bestScore && h.distance < bestDist))
                { bestScore = score; bestDist = h.distance; hitHandle = handle; hitPanel = handle.panel; hitCol = h.collider; }
            }
            else if (board || hoverC)
            {
                int score = 1; var p = board ? board.panel : hoverC.panel;
                if (score > bestScore || (score == bestScore && h.distance < bestDist))
                { bestScore = score; bestDist = h.distance; hitHandle = null; hitPanel = p; hitCol = h.collider; }
            }
        }

        if (_lockoutCollider != null && hitCol == _lockoutCollider) { hitHandle = null; }
        else if (_lockoutCollider != null && hitCol != _lockoutCollider) { _lockoutCollider = null; }

        bool hovering = (hitPanel != null);
        if (!hovering && _hoverPanel) { _hoverPanel.OnHover(false, null); _hoverPanel = null; _hoverHandle = null; }
        if (hovering) { hitPanel.OnHover(true, hitHandle ? (WPHandleType?)hitHandle.type : null); _hoverPanel = hitPanel; _hoverHandle = hitHandle; }

        bool pressed = IsPressed(); bool down = pressed && !_prevPressed; bool up = !pressed && _prevPressed; _prevPressed = pressed;

        if (_activeHandle == null)
        {
            HandleDwellOnHandles(hitPanel, hitHandle, ray);
        }

        // Nhấn để bắt đầu kéo – corner (resize) và center (move)
        if (_activeHandle == null && down && hitHandle != null)
        {
            _activeHandle = hitHandle;
            _activeHandle.BeginDragFromRay(ray, eventCamera);
            _stillTimer = 0f; _prevDir = ray.direction;
        }

        // Kéo + auto thoát khi giữ yên
        if (_activeHandle != null && (pressed || useDwellActivation))
        {
            _activeHandle.UpdateDragFromRay(ray, eventCamera, Time.deltaTime);
            if (_activeHandle.type == WPHandleType.Center && _activeHandle.panel)
                _activeHandle.panel.EnforceNoRoll();

            float ang = Vector3.Angle(_prevDir, ray.direction);
            if (ang < stillAngleDeg) _stillTimer += Time.deltaTime; else _stillTimer = 0f;
            _prevDir = ray.direction;

            if (_stillTimer >= exitAfterSeconds)
            {
                _activeHandle.EndDrag();
                _activeHandle = null;
                if (hitCol) _lockoutCollider = hitCol;
            }
        }

        if (_activeHandle != null && up)
        {
            _activeHandle.EndDrag();
            _activeHandle = null;
            if (hitCol) _lockoutCollider = hitCol;
        }
    }

    void HandleDwellOnHandles(WorldPanelPlus hitPanel, WorldPanelPlusHandle hitHandle, Ray ray)
    {
        if (hitPanel != _candidatePanel || hitHandle != _candidateHandle)
        { _candidatePanel = hitPanel; _candidateHandle = hitHandle; _dwellTimer = 0f; _dwellDir = ray.direction; }
        else if (useDwellActivation && _candidatePanel != null && _candidateHandle != null)
        {
            float a = Vector3.Angle(_dwellDir, ray.direction);
            if (a <= dwellMaxAngle) _dwellTimer += Time.deltaTime; else { _dwellTimer = 0f; _dwellDir = ray.direction; }
            if (_dwellTimer >= dwellSeconds)
            {
                _activeHandle = _candidateHandle;
                _activeHandle.BeginDragFromRay(ray, eventCamera);
                _stillTimer = 0f; _prevDir = ray.direction;
                _dwellTimer = 0f;
            }
        }
    }

    bool IsPressed()
    {
#if UNITY_ANDROID
        try { return Google.XR.Cardboard.Api.IsTriggerPressed; } catch { }
#endif
        if (Input.touchCount > 0)
        {
            var ph = Input.GetTouch(0).phase;
            return ph != TouchPhase.Ended && ph != TouchPhase.Canceled;
        }
        return Input.GetMouseButton(0);
    }
}
