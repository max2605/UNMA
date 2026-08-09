using System;
using Mafi;
using Mafi.Unity;
using Mafi.Unity.InputControl;
using UnityEngine;

namespace UNMA.Ui;

/// <summary>
/// Bridges the IMGUI based UNMA windows into Captain of Industry's input
/// controller chain. IMGUI consumes its own events, but it is not represented
/// by Unity's EventSystem and would otherwise let world clicks and camera
/// scrolling pass through.
/// </summary>
public sealed class UnmaInputBlocker : IUnityInputController, IDisposable
{
    private readonly IUnityInputMgr m_inputManager;
    private readonly Func<bool> m_shouldBlockPointer;

    private volatile bool m_pointerOverUnma;
    private volatile bool m_blockingEnabled = true;
    private volatile bool m_keyboardCaptured;
    private bool m_registrationRequested;
    private bool m_controllerIsActive;
    private bool m_pointerCaptured;
    private bool m_predicateFailureLogged;
    private bool m_disposed;

    private ControllerConfig m_config = CreateControllerConfig();

    public ControllerConfig Config => m_config;

    /// <summary>
    /// Creates a blocker. The optional predicate is evaluated from COI's input
    /// update and should return true while the current pointer is over any
    /// visible UNMA surface. When no predicate is supplied, callers update the
    /// same state through <see cref="SetPointerState"/>.
    /// </summary>
    public UnmaInputBlocker(
        IUnityInputMgr inputManager,
        Func<bool> shouldBlockPointer = null)
    {
        m_inputManager = inputManager ??
                         throw new ArgumentNullException(nameof(inputManager));
        m_shouldBlockPointer = shouldBlockPointer;
    }

    /// <summary>
    /// Requests activation in COI's controller chain. Calling this repeatedly
    /// is safe; the game manager deduplicates active and pending controllers.
    /// </summary>
    public void EnsureActive()
    {
        ThrowIfDisposed();
        m_registrationRequested = true;
        if (!m_controllerIsActive)
        {
            m_inputManager.ActivateNewController(this);
        }
    }

    /// <summary>
    /// Removes this blocker from COI's active controller list.
    /// </summary>
    public void Unregister()
    {
        SetKeyboardCaptured(false);
        if (!m_registrationRequested)
        {
            return;
        }

        m_registrationRequested = false;
        m_inputManager.DeactivateController(this);
        m_controllerIsActive = false;
        m_pointerCaptured = false;
    }

    /// <summary>
    /// Supplies the pointer hit-test result when no live predicate was passed
    /// to the constructor. Update this before COI processes input each frame.
    /// </summary>
    public void SetPointerState(bool pointerOverUnma)
    {
        m_pointerOverUnma = pointerOverUnma;
    }

    /// <summary>
    /// Temporarily enables or disables blocking without unregistering the
    /// controller. Disabling also releases an in-progress pointer capture.
    /// </summary>
    public void SetBlockingEnabled(bool enabled)
    {
        m_blockingEnabled = enabled;
        if (!enabled)
        {
            m_pointerOverUnma = false;
            m_pointerCaptured = false;
            SetKeyboardCaptured(false);
        }
    }

    /// <summary>
    /// Blocks game shortcuts while an IMGUI text field owns keyboard focus.
    /// The flag is deliberately independent of mere pointer hover so normal
    /// game shortcuts remain available whenever the user is not typing.
    /// </summary>
    public void SetKeyboardCaptured(bool captured)
    {
        m_keyboardCaptured = captured;
        m_config.BlockShortcuts = captured;
        m_config.PreventSpeedControl = captured;
    }

    public void Activate()
    {
        if (!m_disposed)
        {
            m_controllerIsActive = true;
        }
    }

    public void Deactivate()
    {
        m_controllerIsActive = false;
        m_pointerCaptured = false;
        SetKeyboardCaptured(false);
    }

    /// <summary>
    /// Returns true for pointer activity over an UNMA surface and for keyboard
    /// activity while an IMGUI text field is focused. Mere hover does not
    /// consume shortcuts. A press that starts inside UNMA remains captured
    /// through drag and release even if the pointer leaves the window.
    /// </summary>
    public bool InputUpdate()
    {
        if (m_disposed || !m_controllerIsActive || !m_blockingEnabled)
        {
            return false;
        }

        var pointerOverUnma = GetPointerOverUnma();
        if (pointerOverUnma && IsTrackedMouseButtonDown())
        {
            m_pointerCaptured = true;
        }

        var wasCaptured = m_pointerCaptured;
        var shouldBlock = wasCaptured ||
                          pointerOverUnma && HasPointerActivity();

        // Keep the release frame consumed. This also recovers cleanly when
        // Unity did not deliver a MouseUp event to IMGUI outside the window.
        if (wasCaptured && !IsTrackedMouseButtonHeld())
        {
            m_pointerCaptured = false;
        }

        return shouldBlock ||
               m_keyboardCaptured && HasKeyboardActivity();
    }

    public void Dispose()
    {
        if (m_disposed)
        {
            return;
        }

        Unregister();
        m_disposed = true;
    }

    private bool GetPointerOverUnma()
    {
        if (m_shouldBlockPointer == null)
        {
            return m_pointerOverUnma;
        }

        try
        {
            var pointerOverUnma = m_shouldBlockPointer();
            m_pointerOverUnma = pointerOverUnma;
            return pointerOverUnma;
        }
        catch (Exception exception)
        {
            if (!m_predicateFailureLogged)
            {
                m_predicateFailureLogged = true;
                Log.Warning(
                    "UNMA: Eingabe-HitTest fehlgeschlagen; " +
                    "verwende den zuletzt gesetzten Zustand. " +
                    exception.GetType().Name + ": " + exception.Message);
            }
            return m_pointerOverUnma;
        }
    }

    private static bool IsTrackedMouseButtonDown()
    {
        return Input.GetMouseButtonDown(0) ||
               Input.GetMouseButtonDown(1) ||
               Input.GetMouseButtonDown(2);
    }

    private static bool IsTrackedMouseButtonHeld()
    {
        return Input.GetMouseButton(0) ||
               Input.GetMouseButton(1) ||
               Input.GetMouseButton(2);
    }

    private static bool HasPointerActivity()
    {
        return IsTrackedMouseButtonDown() ||
               IsTrackedMouseButtonHeld() ||
               Input.GetMouseButtonUp(0) ||
               Input.GetMouseButtonUp(1) ||
               Input.GetMouseButtonUp(2) ||
               Input.mouseScrollDelta.sqrMagnitude > 0f;
    }

    private static bool HasKeyboardActivity()
    {
        // Input.anyKey also includes mouse buttons. Exclude them so the first
        // click outside a text field can both reach the game and release the
        // IMGUI focus instead of forcing the user to click twice.
        return Input.anyKey && !IsTrackedMouseButtonHeld();
    }

    private static ControllerConfig CreateControllerConfig()
    {
        return new ControllerConfig
        {
            Group = ControllerGroup.AlwaysActive,
            IgnoreEscapeKey = true,
            DeactivateOnOtherControllerActive = false,
            DeactivateOnNonUiClick = false,
            AllowInspectorCursor = true,
            BlockShortcuts = false,
            DisableCameraControl = false,
            BlockCameraControlIfInputWasProcessed = true,
            PreventSpeedControl = false,
        };
    }

    private void ThrowIfDisposed()
    {
        if (m_disposed)
        {
            throw new ObjectDisposedException(nameof(UnmaInputBlocker));
        }
    }
}
