using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public partial class WorldPanelPlusControlDock
{
    IEnumerator AnimateDock(bool minimize)
    {
        float startTime = Time.time;
        float endTime = startTime + _animDuration;

        // Calculate expanded size (same as in Build method)
        float extraGap = 0.03f; // Same as in Build
        float totalW = _buttons.Count * buttonSize.x + (_buttons.Count - 1) * (buttonGap + extraGap);
        float expandedWidth = totalW + border * 2;
        float minimizedWidth = buttonSize.x + border * 2;
        float height = buttonSize.y + border * 2;

        // Starting positions
        Vector3 backplateStartScale = _backplate.localScale;
        Vector3 backplateStartPos = _backplate.localPosition;
        Dictionary<WorldPanelPlusDockButton, Vector3> buttonStartPositions = new Dictionary<WorldPanelPlusDockButton, Vector3>();

        foreach (var btn in _buttons)
        {
            buttonStartPositions[btn] = btn.transform.localPosition;
        }

        // Calculate start and target widths (based on current state)
        float startWidth = _backplate.localScale.x;
        float targetWidth = minimize ? minimizedWidth : expandedWidth; while (Time.time < endTime)
        {
            float t = (Time.time - startTime) / _animDuration;

            // Áp dụng easing để animation mượt mà hơn
            float easedT = minimize ? EaseInQuad(t) : EaseOutQuad(t);

            // Find center position (minimize button position)
            Vector3 centerPos = _toggleBtnTr != null ? _toggleBtnTr.localPosition : Vector3.zero;

            // Animate backplate
            if (_backplate != null)
            {
                // Scale width only
                float currentWidth = Mathf.Lerp(startWidth, targetWidth, easedT);
                _backplate.localScale = new Vector3(currentWidth, height, 1);

                // Keep centered on minimize button
                _backplate.localPosition = new Vector3(
                    centerPos.x,
                    backplateStartPos.y,
                    backplateStartPos.z
                );
            }

            // Animate buttons đồng bộ với backplate
            foreach (var btn in _buttons)
            {
                if (btn.type == WPDockButtonType.MinimizeToggle)
                {
                    // Nút minimize luôn ở vị trí cuối cùng
                    continue;
                }

                Vector3 startPos = buttonStartPositions[btn];
                Vector3 targetPos = minimize ?
                    _toggleBtnTr.localPosition : // Thu về vị trí nút minimize
                    _expandedPos[btn];          // Mở ra vị trí gốc

                // Sử dụng cùng easedT để đồng bộ với backplate
                // Di chuyển theo chiều ngang
                btn.transform.localPosition = Vector3.Lerp(startPos, targetPos, easedT);
            }

            yield return null;
        }

        // Đảm bảo kết thúc ở đúng vị trí
        if (_backplate != null)
        {
            Vector3 centerPos = _toggleBtnTr != null ? _toggleBtnTr.localPosition : Vector3.zero;
            _backplate.localScale = new Vector3(targetWidth, height, 1);
            _backplate.localPosition = new Vector3(
                centerPos.x,
                backplateStartPos.y,
                backplateStartPos.z
            );
        }

        foreach (var btn in _buttons)
        {
            if (btn.type != WPDockButtonType.MinimizeToggle)
            {
                btn.transform.localPosition = minimize ?
                    _toggleBtnTr.localPosition :
                    _expandedPos[btn];
            }
        }

        _minimized = minimize;
    }

    // Hàm hỗ trợ easing
    float EaseInQuad(float t)
    {
        return t * t;
    }

    float EaseOutQuad(float t)
    {
        return t * (2f - t);
    }
}
