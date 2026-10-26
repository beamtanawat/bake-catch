using System;
using UnityEditor;
using UnityEngine;

// Isolated Editor fixtures. No cloud profile/session is created by these checks.
public static class BakeCatchProfileValidation
{
    [MenuItem("Bake Catch/Validate Player Profiles (Play Mode)")]
    public static void Run()
    {
        if (!EditorApplication.isPlaying) throw new Exception("Enter Play Mode first.");
        PlayerProfile a = new PlayerProfile(" a ", " Player A ");
        Check(a.code == "A" && a.displayName == "Player A", "Profile normalization");
        foreach (string code in new[] { "", "../P001", "bad/code" })
        {
            bool rejected = false;
            try { new PlayerProfile(code, "Name"); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "Reject unsafe/empty code");
        }
        bool invalidName = false;
        try { new PlayerProfile("A", "<b>name</b>"); } catch (ArgumentException) { invalidName = true; }
        Check(invalidName, "Reject markup names");
        GameSession session = UnityEngine.Object.FindFirstObjectByType<GameSession>();
        session.MainMenu();
        Check(session.SelectPlayer(a), "Select A");
        a.code = "MUTATED";
        session.StartSession(DifficultyLevel.Easy);
        Check(!session.SelectPlayer(new PlayerProfile("B", "Player B")), "Do not switch while Playing");
        session.Pause();
        Check(!session.SelectPlayer(new PlayerProfile("B", "Player B")), "Do not switch while Paused");
        session.Resume();
        session.EndSession();
        Check(session.Result.profile.code == "A", "Snapshot A independent of external mutation");
        Check(session.SelectPlayer(new PlayerProfile("B", "Player B")), "Switch after Ended");
        Check(session.Result.profile.code == "A", "Switch cannot relabel the completed round");
        session.StartSession(DifficultyLevel.Hard);
        session.EndSession();
        Check(session.Result.profile.code == "B", "Next round belongs to B");
        session.MainMenu();
        Debug.Log("PLAYER_PROFILE_VALIDATION_PASS: normalization, unsafe input, snapshot, active-round lock, replay isolation.");
    }
    static void Check(bool condition, string message) { if (!condition) throw new Exception("PLAYER_PROFILE_FAIL: " + message); }
}
