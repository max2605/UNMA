using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace UNMA.Ui;

/// <summary>
/// Represents the IMGUI based UNMA windows inside Unity's EventSystem. Captain
/// of Industry asks the EventSystem whether the pointer is over UI before its
/// early right-click camera handling runs. Transparent raycast targets are the
/// only ownership-safe way to answer that question for IMGUI: they do not
/// mutate the camera's shared right-click suppression flag used by game tools.
/// </summary>
public sealed class UnmaPointerRaycastShield : IDisposable
{
    private readonly GameObject m_root;
    private readonly List<GameObject> m_targets = new();
    private bool m_disposed;

    public UnmaPointerRaycastShield(Transform parent)
    {
        m_root = new GameObject(
            "UNMA EventSystem Raycast Shield",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(GraphicRaycaster));
        m_root.hideFlags = HideFlags.HideAndDontSave;
        if (parent != null)
        {
            m_root.transform.SetParent(parent, false);
        }

        var uiLayer = LayerMask.NameToLayer("UI");
        if (uiLayer >= 0)
        {
            m_root.layer = uiLayer;
        }

        var canvas = m_root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue;

        var raycaster = m_root.GetComponent<GraphicRaycaster>();
        raycaster.ignoreReversedGraphics = true;
        raycaster.blockingObjects = GraphicRaycaster.BlockingObjects.None;
    }

    /// <summary>
    /// Updates physical EventSystem hit regions from logical IMGUI rectangles.
    /// Rectangles use IMGUI's top-left origin and are scaled to screen pixels.
    /// </summary>
    public void UpdateSurfaces(
        IReadOnlyList<Rect> logicalRects,
        float uiScale,
        bool enabled)
    {
        if (m_disposed)
        {
            return;
        }

        var count = enabled && logicalRects != null
            ? logicalRects.Count
            : 0;
        EnsureTargetCount(count);

        uiScale = Mathf.Max(0.01f, uiScale);
        for (var index = 0; index < m_targets.Count; index++)
        {
            var target = m_targets[index];
            var isActive = index < count;
            if (target.activeSelf != isActive)
            {
                target.SetActive(isActive);
            }
            if (!isActive)
            {
                continue;
            }

            var logicalRect = logicalRects[index];
            var rectTransform = (RectTransform)target.transform;
            rectTransform.anchoredPosition = new Vector2(
                logicalRect.x * uiScale,
                -logicalRect.y * uiScale);
            rectTransform.sizeDelta = new Vector2(
                logicalRect.width * uiScale,
                logicalRect.height * uiScale);
        }
    }

    public void Dispose()
    {
        if (m_disposed)
        {
            return;
        }
        m_disposed = true;
        if (m_root != null)
        {
            UnityEngine.Object.Destroy(m_root);
        }
        m_targets.Clear();
    }

    private void EnsureTargetCount(int count)
    {
        while (m_targets.Count < count)
        {
            var target = new GameObject(
                "UNMA Raycast Surface " + (m_targets.Count + 1),
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            target.hideFlags = HideFlags.HideAndDontSave;
            target.layer = m_root.layer;
            target.transform.SetParent(m_root.transform, false);

            var rectTransform = (RectTransform)target.transform;
            rectTransform.anchorMin = new Vector2(0f, 1f);
            rectTransform.anchorMax = new Vector2(0f, 1f);
            rectTransform.pivot = new Vector2(0f, 1f);

            var image = target.GetComponent<Image>();
            image.color = new Color(1f, 1f, 1f, 0.001f);
            image.raycastTarget = true;
            m_targets.Add(target);
        }
    }
}
