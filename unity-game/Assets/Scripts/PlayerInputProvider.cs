using UnityEngine;
using UnityEngine.InputSystem;
using System;
using System.Runtime.InteropServices;

public class PlayerInputProvider : MonoBehaviour
{
    [Serializable] private class GripFrame { public bool available; public float left, right; }
    private float gripIntent;
    private double lastGrip = double.NegativeInfinity;

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void BakeCatchGripStart(string target);
    [DllImport("__Internal")] private static extern void BakeCatchGripStop();
#endif

    private void OnEnable()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        BakeCatchGripStart(gameObject.name);
#endif
    }

    private void OnDisable()
    {
        lastGrip = double.NegativeInfinity;
#if UNITY_WEBGL && !UNITY_EDITOR
        BakeCatchGripStop();
#endif
    }

    // Called by the browser bridge on Unity's main thread. No sensor statistics are invented.
    public void ReceiveGrip(string json)
    {
        lastGrip = double.NegativeInfinity;
        try
        {
            GripFrame frame = JsonUtility.FromJson<GripFrame>(json);
            if (frame == null || !frame.available || float.IsNaN(frame.left) || float.IsNaN(frame.right) ||
                float.IsInfinity(frame.left) || float.IsInfinity(frame.right) ||
                frame.left < 0 || frame.left > 1 || frame.right < 0 || frame.right > 1) return;
            gripIntent = frame.right - frame.left;
            lastGrip = Time.realtimeSinceStartupAsDouble;
        }
        catch (ArgumentException) { }
    }
    // Movement consumes only this value, independent of the input source.
    public float HorizontalInput
    {
        get
        {
            Keyboard keyboard = Keyboard.current;
            if (!isActiveAndEnabled)
                return 0f;

            bool left = keyboard != null && (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed);
            bool right = keyboard != null && (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed);

            // Opposite directions cancel, including mixed letter/arrow keys.
            if (left || right) return (right ? 1f : 0f) - (left ? 1f : 0f);
            return Time.realtimeSinceStartupAsDouble - lastGrip <= .5 ? gripIntent : 0f;
        }
    }
}
