using UnityEngine;
using UnityEngine.InputSystem;
using System;
using System.Runtime.InteropServices;

public class PlayerInputProvider : MonoBehaviour
{
    [Serializable] private class GripFrame { public bool available; public float left, right; public int shake; public bool hasShake; }
    private float gripIntent;
    private double lastGrip = double.NegativeInfinity;
    private GameSession session;
    // Sensor shake events. GripShake firmware already debounces the switch and reports a level
    // (0 still, 1 moving, 2 strong) per frame, not one packet per stroke. A rise from 0 is one event;
    // while the level stays up, one more event per interval (faster when strong). Tune from real shaking.
    public static float ShakeRepeatMoving = .16f, ShakeRepeatStrong = .1f;
    private int pendingShakes, shakeLevel;
    private double nextShakeRepeat;
    private bool lastHasShake;
    // Device status for the Bonus Time hint: a fresh hardware frame within the input timeout.
    public bool SensorLive => Time.realtimeSinceStartupAsDouble - lastGrip <= .5;
    public bool ShakeSensorLive => SensorLive && lastHasShake;

    public void SetSession(GameSession value) => session = value;

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
        pendingShakes = shakeLevel = 0;
#if UNITY_WEBGL && !UNITY_EDITOR
        BakeCatchGripStop();
#endif
    }

    // Called by the browser bridge on Unity's main thread. No sensor statistics are invented.
    public void ReceiveGrip(string json)
    {
        lastGrip = double.NegativeInfinity;
        // A lost or invalid frame never keeps cutting from the last shake.
        int previousLevel = shakeLevel;
        shakeLevel = 0;
        try
        {
            GripFrame frame = JsonUtility.FromJson<GripFrame>(json);
            if (frame == null || !frame.available || float.IsNaN(frame.left) || float.IsNaN(frame.right) ||
                float.IsInfinity(frame.left) || float.IsInfinity(frame.right) ||
                frame.left < 0 || frame.left > 1 || frame.right < 0 || frame.right > 1) { pendingShakes = 0; return; }
            gripIntent = frame.right - frame.left;
            lastHasShake = frame.hasShake;
            // Right-hand vibration only; grip force is never turned into a shake.
            if (frame.hasShake && frame.shake > 0)
            {
                double now = Time.realtimeSinceStartupAsDouble;
                if (previousLevel == 0 || now >= nextShakeRepeat)
                {
                    pendingShakes++;
                    nextShakeRepeat = now + (frame.shake >= 2 ? ShakeRepeatStrong : ShakeRepeatMoving);
                }
                shakeLevel = frame.shake;
            }
            lastGrip = Time.realtimeSinceStartupAsDouble;
            if (session != null && session.IsPlaying) session.Statistics.RecordGrip(frame.left, frame.right);
        }
        catch (ArgumentException) { }
    }
    // Slice requests since the last call: sensor events plus a Space key-down (holding Space repeats nothing).
    public int ConsumeShakes()
    {
        Keyboard keyboard = Keyboard.current;
        int shakes = pendingShakes + (isActiveAndEnabled && keyboard != null && keyboard.spaceKey.wasPressedThisFrame ? 1 : 0);
        pendingShakes = 0;
        return shakes;
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
