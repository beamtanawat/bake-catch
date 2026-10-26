using System;
using UnityEditor;
using UnityEngine;

// Editor-only fixtures: never sent to MQTT, hardware, or Firebase.
public static class BakeCatchBonusValidation
{
    const string Shake = "{\"available\":true,\"left\":0,\"right\":0,\"shake\":1,\"hasShake\":true}";
    const string Still = "{\"available\":true,\"left\":0,\"right\":0,\"shake\":0,\"hasShake\":true}";

    [MenuItem("Bake Catch/Validate Bonus Time (Play Mode)")]
    public static void Run()
    {
        if (!EditorApplication.isPlaying) throw new Exception("Enter Play Mode first.");
        GameSession session = UnityEngine.Object.FindFirstObjectByType<GameSession>();
        PlayerInputProvider input = UnityEngine.Object.FindFirstObjectByType<PlayerInputProvider>();
        input.ConsumeShakes();

        // Sensor events: a rise from still is one event; a held level is not a new event every frame.
        input.ReceiveGrip("{\"available\":true,\"left\":0,\"right\":0,\"shake\":1}");
        Check(input.ConsumeShakes() == 0, "Frames without the GripShake field never become shakes.");
        input.ReceiveGrip("{\"available\":true,\"left\":1,\"right\":1,\"hasShake\":true}");
        Check(input.ConsumeShakes() == 0, "Grip force is never a shake.");
        input.ReceiveGrip(Shake);
        input.ReceiveGrip(Shake);
        input.ReceiveGrip(Shake);
        Check(input.ConsumeShakes() == 1, "A held shake level is one event, not one per frame.");
        input.ReceiveGrip(Still);
        input.ReceiveGrip(Shake);
        input.ReceiveGrip(Still);
        input.ReceiveGrip(Shake);
        Check(input.ConsumeShakes() == 2, "Each new rise is counted, even several in one Unity frame.");
        input.ReceiveGrip(Still);
        input.ReceiveGrip(Shake);
        input.ReceiveGrip("{\"available\":false}");
        Check(input.ConsumeShakes() == 0, "A lost sensor frame drops pending shakes.");
        input.ReceiveGrip(Still);

        foreach (DifficultyLevel level in new[] { DifficultyLevel.Easy, DifficultyLevel.Medium, DifficultyLevel.Hard })
        {
            session.SelectPlayer(new PlayerProfile("EDITOR", "Bonus validation"));
            session.StartSession(level);
            Check(Mathf.Approximately(session.Remaining, 45f), level + ": the round still starts at 45 seconds.");
            Complete(session, IngredientType.Strawberry, IngredientType.Milk, IngredientType.Egg);
            Check(session.IsPlaying && session.Statistics.RecipeComplete == 1, level + ": one order does not start Bonus Time.");
            input.ReceiveGrip(Still);
            input.ReceiveGrip(Shake);
            Complete(session, IngredientType.Cheese, IngredientType.Tomato, IngredientType.Dough);
            Check(session.InBonus && Mathf.Approximately(session.BonusRemaining, GameSession.BonusDuration), level + ": second order starts Bonus Time.");
            Check(input.ConsumeShakes() == 0, level + ": shakes before Bonus Time are discarded.");
            float round = session.Remaining;
            int score = session.Statistics.Score, samples = session.Statistics.GripSampleCount;
            input.ReceiveGrip("{\"available\":true,\"left\":1,\"right\":1}");
            Check(session.Statistics.GripSampleCount == samples, level + ": grip statistics pause during Bonus Time.");

            session.AdvanceBonus(.5f, 0);
            Check(session.BonusSlices == 0 && Mathf.Approximately(session.BonusRemaining, 4.5f), level + ": no input, no slice; the countdown runs.");
            session.AdvanceBonus(0f, 1);
            Check(session.BonusSlices == 1 && session.Statistics.Score == score + GameSession.SliceScore,
                level + ": one input slices at once and scores once.");
            session.AdvanceBonus(.016f, 1);
            session.AdvanceBonus(0f, 3);
            session.AdvanceBonus(0f, 11);
            Check(session.BonusSlices == 16 && session.BonusSeconds == 0 && Mathf.Approximately(session.BonusRemaining, 4.484f),
                level + ": rapid and same-frame inputs each count; Bonus Time stays a fixed 5 seconds.");
            session.Pause();
            Check(session.State == SessionState.Paused && session.BonusVisible, level + ": Pause works during Bonus Time and keeps the scene.");
            float paused = session.BonusRemaining;
            session.AdvanceBonus(1f, 4);
            input.ReceiveGrip(Still);
            input.ReceiveGrip(Shake);
            session.Resume();
            Check(session.InBonus && session.BonusSlices == 16 && Mathf.Approximately(session.BonusRemaining, paused) && input.ConsumeShakes() == 0,
                level + ": paused input and time are ignored and not replayed on Resume.");

            session.AdvanceBonus(session.BonusRemaining, 0);
            Check(session.IsPlaying && session.Feedback.StartsWith("Bonus Time!") && !session.BonusVisible, level + ": Bonus Time ends once with a summary.");
            session.AdvanceBonus(0f, 5);
            Check(session.BonusSlices == 16 && session.Statistics.BonusSlices == 16, level + ": input after Bonus Time ends does not count.");
            Check(Mathf.Approximately(session.Remaining, round), level + ": earned seconds go to Bonus Time, not the 45-second round.");

            Complete(session, IngredientType.Bread, IngredientType.Meat, IngredientType.Cheese);
            Complete(session, IngredientType.Strawberry, IngredientType.Milk, IngredientType.Egg);
            Check(session.InBonus && session.BonusSlices == 0 && session.BonusSeconds == 0 && Mathf.Approximately(session.BonusRemaining, GameSession.BonusDuration),
                level + ": a new Bonus Time starts its counters fresh.");
            session.AdvanceBonus(0f, 1);
            Check(session.BonusSeconds == 0, level + ": no leftover half pair carries into a new Bonus Time.");
            session.AdvanceBonus(session.BonusRemaining, 0);
            session.EndSession();
            Check(Mathf.Approximately(session.Result.durationSec, session.Duration - session.Remaining) && session.Result.bonusSlices == 17,
                level + ": play time stays real round time; bonus slices recorded.");
        }
        session.MainMenu();
        Debug.Log("BONUS_TIME_VALIDATION_PASS: Easy/Medium/Hard, instant slices, sensor rise events, fixed 5 s Bonus Time, multi-event frames, Pause, end, fresh counters, 45 s round unchanged.");
    }

    static void Complete(GameSession session, params IngredientType[] types)
    {
        foreach (IngredientType type in types) session.Resolve(IngredientDefinition.Get(type), true);
    }

    static void Check(bool condition, string message) { if (!condition) throw new Exception("BONUS_TIME_VALIDATION_FAIL: " + message); }
}
