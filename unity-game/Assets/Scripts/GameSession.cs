using UnityEngine;
using UnityEngine.InputSystem;
using System.Runtime.InteropServices;

public enum SessionState { Ready, Playing, Paused, Ended }

public class GameSession : MonoBehaviour
{
    [SerializeField, Min(1f)] private float sessionDuration = 45f;
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
    private float feedbackUntil;
    private Vector3 playerStart;
    public PlayerProfile Player { get; private set; }
    private PlayerProfile playerAtStart;

    public bool SelectPlayer(PlayerProfile profile)
    {
        if (State == SessionState.Playing || State == SessionState.Paused || profile == null) return false;
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
            if (IsPlaying) Pause();
            else if (State == SessionState.Paused) Resume();
        }
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
        player.transform.position = playerStart;
        spawner.ResetSpawning();
        State = SessionState.Playing;
    }

    public void Pause() { if (IsPlaying) State = SessionState.Paused; }
    public void Resume() { if (State == SessionState.Paused) State = SessionState.Playing; }
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
