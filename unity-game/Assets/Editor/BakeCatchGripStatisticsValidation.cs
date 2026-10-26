using System;
using UnityEditor;
using UnityEngine;

// Editor-only fixtures: never sent to MQTT, hardware, or Firebase.
public static class BakeCatchGripStatisticsValidation
{
    [MenuItem("Bake Catch/Validate Grip Statistics (Play Mode)")]
    public static void Run()
    {
        if (!EditorApplication.isPlaying) throw new Exception("Enter Play Mode first.");
        var stats = new SessionStatistics();
        Check(!new SessionResult(stats, 45f, DifficultyLevel.Easy).hasGripSamples, "Keyboard-only is unmeasured.");
        stats.RecordGrip(.25f, 0f);
        stats.RecordGrip(.75f, .5f);
        stats.RecordGrip(float.NaN, 0f);
        stats.RecordGrip(0f, float.PositiveInfinity);
        stats.RecordGrip(-1f, 0f);
        var result = new SessionResult(stats, 45f, DifficultyLevel.Easy);
        Check(Mathf.Approximately(result.leftAvgGrip, 50f), "Left mean must be 50%.");
        Check(Mathf.Approximately(result.rightAvgGrip, 25f), "Right mean must include real released zero.");
        Check(result.gripSampleCount == 2 && result.hasGripSamples, "Reject invalid samples.");

        GameSession session = UnityEngine.Object.FindFirstObjectByType<GameSession>();
        PlayerInputProvider input = UnityEngine.Object.FindFirstObjectByType<PlayerInputProvider>();
        session.SelectPlayer(new PlayerProfile("EDITOR", "Grip validation"));
        session.StartSession(DifficultyLevel.Easy);
        input.ReceiveGrip("{\"available\":true,\"left\":1,\"right\":0}");
        Check(session.Statistics.GripSampleCount == 1, "Collect only new hardware frames.");
        session.Pause();
        input.ReceiveGrip("{\"available\":true,\"left\":0,\"right\":1}");
        Check(session.Statistics.GripSampleCount == 1, "Exclude Pause samples.");
        session.Resume();
        input.ReceiveGrip("{\"available\":false}");
        input.ReceiveGrip("{\"available\":true,\"left\":2,\"right\":0}");
        Check(session.Statistics.GripSampleCount == 1, "Exclude disconnected/invalid data, never insert zero.");
        input.ReceiveGrip("{\"available\":true,\"left\":0,\"right\":0}");
        session.EndSession();
        Check(session.Result.gripSampleCount == 2 && Mathf.Approximately(session.Result.leftAvgGrip, 50f), "Snapshot valid sample mean.");
        input.ReceiveGrip("{\"available\":true,\"left\":1,\"right\":1}");
        Check(session.Statistics.GripSampleCount == 2, "Exclude Ended samples.");
        session.StartSession(DifficultyLevel.Medium);
        Check(session.Statistics.GripSampleCount == 0, "Replay resets measurements.");
        session.MainMenu();
        input.ReceiveGrip("{\"available\":true,\"left\":1,\"right\":1}");
        Check(session.Statistics.GripSampleCount == 0, "Exclude Main Menu samples.");
        Debug.Log("GRIP_STATISTICS_PASS: sample mean, released zero, invalid/missing data, Pause/Resume, Ended, replay, keyboard-only.");
    }
    static void Check(bool condition, string message) { if (!condition) throw new Exception("GRIP_STATISTICS_FAIL: " + message); }
}
