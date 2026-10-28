using UnityEngine;
using UnityEngine.InputSystem;
using System.Runtime.InteropServices;

public enum SessionState { Ready, Playing, Paused, Ended, Bonus, Countdown }

public class GameSession : MonoBehaviour
{
    [SerializeField, Min(1f)] private float sessionDuration = 60f;
    [SerializeField] private PlayerController player;
    [SerializeField] private IngredientSpawner spawner;

    public SessionState State { get; private set; } = SessionState.Ready;
    public bool IsPlaying => State == SessionState.Playing;
    public DifficultyLevel Difficulty { get; private set; }
    public float Remaining { get; private set; }
    public float Duration => sessionDuration;
    public SessionStatistics Statistics { get; private set; } = new SessionStatistics();
    public RecipeProgress Recipe { get; private set; } = new RecipeProgress();
    public SessionResult Result { get; private set; }
    public string Feedback { get; private set; } = "";
    // Bonus Time: every second completed order pauses the round for a right-hand shake (or Space) cake cut.
    // Every accepted shake or key press is one slice at once; animation only shows it.
    public const int RecipesPerBonus = 2, SliceScore = 5, SlicesPerBonusSecond = 2;
    public const float BonusDuration = 5f;
    // Seconds slices may add to one Bonus Time. 0 keeps Bonus Time a fixed BonusDuration (5 s);
    // raise it to let every SlicesPerBonusSecond slices extend the countdown. Slices always score.
    public const int MaxBonusSeconds = 0;
    // Optional gameplay floor between slices; 0 = every valid input event counts.
    public static float MinSliceInterval = 0f;
    public bool InBonus => State == SessionState.Bonus;
    // A 3-2-1 countdown runs before Bonus Time starts and again before play resumes; the round timer stays paused.
    public const float ResumeCountdown = 3f;
    public bool CountdownToBonus => countdownNext == SessionState.Bonus;
    private SessionState countdownNext = SessionState.Playing;
    public float CountdownRemaining { get; private set; }
    public bool InCountdown => State == SessionState.Countdown;
    public bool CountdownVisible => InCountdown || (State == SessionState.Paused && resumeState == SessionState.Countdown);
    // Paused from Bonus Time keeps the cake scene frozen on screen.
    public bool BonusVisible => InBonus || (InCountdown && CountdownToBonus) ||
        (State == SessionState.Paused && (resumeState == SessionState.Bonus || (resumeState == SessionState.Countdown && CountdownToBonus)));
    private SessionState resumeState = SessionState.Playing;
    public float BonusRemaining { get; private set; }
    public int BonusSlices { get; private set; }
    public int BonusSeconds { get; private set; }
    private float bonusClock, lastSliceClock;
    private PlayerInputProvider input;
    private float feedbackUntil;
    private Vector3 playerStart;
    public PlayerProfile Player { get; private set; }
    private PlayerProfile playerAtStart;

    public bool SelectPlayer(PlayerProfile profile)
    {
        if (State == SessionState.Playing || State == SessionState.Paused || State == SessionState.Bonus || State == SessionState.Countdown || profile == null) return false;
        Player = profile.Copy();
        return true;
    }
#if UNITY_WEBGL && !UNITY_EDITOR
    private string sessionId;
    [DllImport("__Internal")] private static extern void BakeCatchFirebaseInit();
    [DllImport("__Internal")] private static extern void BakeCatchFirebaseSave(string id, string result);
#endif

    private void Awake()
    {
        playerStart = player.transform.position;
        input = player.InputProvider;
        player.SetSession(this);
        spawner.SetSession(this);
#if UNITY_WEBGL && !UNITY_EDITOR
        BakeCatchFirebaseInit();
#endif
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            if (IsPlaying || InBonus || InCountdown) Pause();
            else if (State == SessionState.Paused) Resume();
        }
        if (InBonus) { AdvanceBonus(Time.deltaTime, input != null ? input.ConsumeShakes() : 0); return; }
        if (InCountdown) { AdvanceCountdown(Time.deltaTime); return; }
        if (!IsPlaying) return;
        Remaining = Mathf.Max(0f, Remaining - Time.deltaTime);
        if (Time.time >= feedbackUntil) Feedback = "";
        if (Remaining <= 0f) EndSession();
    }

    public void StartSession(DifficultyLevel difficulty)
    {
        if (Player == null) { Debug.LogWarning("Select a player profile before starting."); return; }
        playerAtStart = Player.Copy();
#if UNITY_WEBGL && !UNITY_EDITOR
        sessionId = System.Guid.NewGuid().ToString("N");
#endif
        spawner.ClearObjects();
        Difficulty = difficulty;
        Statistics = new SessionStatistics();
        Recipe = new RecipeProgress();
        Result = null;
        Feedback = "";
        Remaining = sessionDuration;
        BonusRemaining = 0f;
        BonusSlices = BonusSeconds = 0;
        player.transform.position = playerStart;
        spawner.ResetSpawning();
        State = SessionState.Playing;
    }

    public void Pause()
    {
        if (!IsPlaying && !InBonus && !InCountdown) return;
        resumeState = State;
        State = SessionState.Paused;
    }

    public void Resume()
    {
        if (State != SessionState.Paused) return;
        if (input != null) input.ConsumeShakes(); // Discard shakes made while paused.
        State = resumeState;
    }
    public void MainMenu()
    {
        State = SessionState.Ready;
        spawner.ClearObjects();
    }

    public void Resolve(IngredientDefinition ingredient, bool caught)
    {
        if (!IsPlaying) return;
        int previousRecipes = Statistics.RecipeComplete;
        Statistics.Resolve(ingredient, caught, Recipe);
        Feedback = ingredient.IsHazard ? (caught ? "Careful! -20" : "") :
            caught ? "+" + ingredient.Score + "  " + ingredient.Name : "Miss — try the next one!";
        if (Statistics.RecipeComplete > previousRecipes) Feedback = "Order complete! Fresh order ready";
        feedbackUntil = Time.time + 1.6f;
        if (Statistics.RecipeComplete > previousRecipes && Statistics.RecipeComplete % RecipesPerBonus == 0 && Remaining > 0f)
            BeginCountdown(SessionState.Bonus);
    }

    private void StartBonus()
    {
        State = SessionState.Bonus;
        BonusRemaining = BonusDuration;
        BonusSlices = BonusSeconds = 0;
        bonusClock = 0f;
        lastSliceClock = float.NegativeInfinity;
        if (input != null) input.ConsumeShakes(); // Shakes before Bonus Time do not count.
    }

    // Order per frame: valid inputs slice (and may extend the countdown), then time passes, then the end check.
    public void AdvanceBonus(float deltaTime, int shakes)
    {
        if (!InBonus) return;
        for (int i = 0; i < shakes; i++)
        {
            if (bonusClock - lastSliceClock < MinSliceInterval) continue;
            lastSliceClock = bonusClock;
            Slice();
        }
        bonusClock += deltaTime;
        BonusRemaining = Mathf.Max(0f, BonusRemaining - deltaTime);
        if (BonusRemaining > 0f) return;
        if (input != null) input.ConsumeShakes();
        BeginCountdown(SessionState.Playing);
        Feedback = $"Bonus Time! +{BonusSlices * SliceScore} points";
        feedbackUntil = Time.time + 2.2f;
    }

    public void AdvanceCountdown(float deltaTime)
    {
        if (!InCountdown) return;
        CountdownRemaining = Mathf.Max(0f, CountdownRemaining - deltaTime);
        if (CountdownRemaining > 0f) return;
        if (CountdownToBonus) StartBonus();
        else State = SessionState.Playing;
    }

    private void BeginCountdown(SessionState next)
    {
        countdownNext = next;
        CountdownRemaining = ResumeCountdown;
        // The cake scene shows during the pre-bonus countdown with fresh counters.
        if (next == SessionState.Bonus) { BonusSlices = BonusSeconds = 0; BonusRemaining = BonusDuration; }
        State = SessionState.Countdown;
    }

    // One successful slice: points now, and each completed pair adds one second to the Bonus Time countdown.
    private void Slice()
    {
        BonusSlices++;
        Statistics.AddBonusSlice(SliceScore);
        int earned = Mathf.Min(BonusSlices / SlicesPerBonusSecond, MaxBonusSeconds) - BonusSeconds;
        if (earned <= 0) return;
        BonusSeconds += earned;
        BonusRemaining += earned;
    }

    public void EndSession()
    {
        if (!IsPlaying) return;
        State = SessionState.Ended;
        Result = new SessionResult(Statistics, sessionDuration - Remaining, Difficulty);
        Result.profile = playerAtStart.Copy();
        spawner.ClearObjects();
#if UNITY_WEBGL && !UNITY_EDITOR
        BakeCatchFirebaseSave(sessionId, JsonUtility.ToJson(Result));
#endif
    }
}
