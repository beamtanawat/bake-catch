using UnityEngine;

public sealed class SessionStatistics
{
    public int Score { get; private set; }
    public int CatchCount { get; private set; }
    public int MissCount { get; private set; }
    public int CurrentCombo { get; private set; }
    public int MaxCombo { get; private set; }
    public int RecipeComplete { get; private set; }
    public int GripSampleCount { get; private set; }
    public int BonusSlices { get; private set; }
    private double leftGripSum, rightGripSum;
    public float LeftAvgGrip => GripSampleCount == 0 ? 0f : Mathf.Clamp((float)(100 * leftGripSum / GripSampleCount), 0f, 100f);
    public float RightAvgGrip => GripSampleCount == 0 ? 0f : Mathf.Clamp((float)(100 * rightGripSum / GripSampleCount), 0f, 100f);
    public float Accuracy => CatchCount + MissCount == 0 ? 0f : 100f * CatchCount / (CatchCount + MissCount);
    public string ComboFeedback => CurrentCombo < 2 ? "Keep going!" : CurrentCombo == 2 ? "OK" :
        CurrentCombo == 3 ? "ดี" : CurrentCombo == 4 ? "ดีมาก" : CurrentCombo == 5 ? "ดีที่สุด" : "Combo x" + CurrentCombo;

    // One sample per fresh hardware frame, not per rendered frame. Released zero is real data.
    public void RecordGrip(float left, float right)
    {
        if (float.IsNaN(left) || float.IsNaN(right) || float.IsInfinity(left) || float.IsInfinity(right) ||
            left < 0 || left > 1 || right < 0 || right > 1) return;
        leftGripSum += left; rightGripSum += right;
        GripSampleCount++;
    }

    public void AddBonusSlice(int points)
    {
        if (points <= 0) return;
        Score += points;
        BonusSlices++;
    }

    public void Resolve(IngredientDefinition ingredient, bool caught, RecipeProgress recipe)
    {
        if (ingredient.IsHazard)
        {
            if (caught)
            {
                Score = Mathf.Max(0, Score + ingredient.Score);
                CurrentCombo = 0;
            }
            return;
        }
        if (!caught)
        {
            MissCount++;
            CurrentCombo = 0;
            return;
        }

        CatchCount++;
        Score += ingredient.Score;
        CurrentCombo++;
        MaxCombo = Mathf.Max(MaxCombo, CurrentCombo);
        if (ingredient.IsRecipeIngredient && recipe.Collect(ingredient.Type)) RecipeComplete++;
    }
}

[System.Serializable]
public sealed class SessionResult
{
    public int score, catchCount, missCount, maxCombo, recipeComplete;
    public float durationSec, accuracy, leftAvgGrip, rightAvgGrip;
    public string level;
    public bool hasGripSamples;
    public int gripSampleCount;
    public int bonusSlices;
    public PlayerProfile profile;

    public SessionResult(SessionStatistics stats, float duration, DifficultyLevel difficulty)
    {
        score = stats.Score;
        catchCount = stats.CatchCount;
        missCount = stats.MissCount;
        maxCombo = stats.MaxCombo;
        recipeComplete = stats.RecipeComplete;
        accuracy = stats.Accuracy;
        durationSec = duration;
        level = difficulty.ToString();
        gripSampleCount = stats.GripSampleCount;
        hasGripSamples = gripSampleCount > 0;
        leftAvgGrip = stats.LeftAvgGrip;
        rightAvgGrip = stats.RightAvgGrip;
        bonusSlices = stats.BonusSlices;
    }
}
