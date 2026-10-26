using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Repeatable integration checks against the real Game scene, prefab, UI, and 2D physics.
// Run from the Bake Catch menu. Test changes exist only in Play Mode.
[InitializeOnLoad]
public static class BakeCatchValidation
{
    private const string Pending = "BakeCatch.ValidationPending";
    private static IEnumerator<double> routine;
    private static double resumeAt;
    private static int assertions;
    private static string runtimeError;
    private static GameSession session;
    private static IngredientSpawner spawner;
    private static PlayerController player;
    private static BakeCatchUI ui;
    private static Keyboard keyboard;
    private static Keyboard originalKeyboard;
    private static Key[] heldKeys = Array.Empty<Key>();
    private static float sampledInput;

    static BakeCatchValidation()
    {
        EditorApplication.playModeStateChanged += OnPlayMode;
    }

    [MenuItem("Bake Catch/Validate Standalone Game")]
    public static void Run()
    {
        if (routine != null) return;
        if (!EditorApplication.isPlaying)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene("Assets/Scenes/Game.unity");
            UnityEditor.SessionState.SetBool(Pending, true);
            EditorApplication.isPlaying = true;
        }
        else Begin();
    }

    private static void OnPlayMode(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode && UnityEditor.SessionState.GetBool(Pending, false))
        {
            UnityEditor.SessionState.SetBool(Pending, false);
            Begin();
        }
        if (state == PlayModeStateChange.ExitingPlayMode) Stop();
    }

    private static void Begin()
    {
        assertions = 0;
        runtimeError = null;
        routine = CheckGame();
        resumeAt = EditorApplication.timeSinceStartup + 1;
        Application.logMessageReceived += CaptureError;
        EditorApplication.update += Tick;
        EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor")).Focus();
        Debug.Log("BAKE_CATCH_VALIDATION_STARTED");
    }

    private static void CaptureError(string message, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) runtimeError = message;
    }

    private static void Tick()
    {
        if (EditorApplication.timeSinceStartup < resumeAt || EditorApplication.isCompiling) return;
        try
        {
            if (runtimeError != null) throw new Exception("Runtime error: " + runtimeError);
            if (routine.MoveNext()) resumeAt = EditorApplication.timeSinceStartup + routine.Current;
            else
            {
                Stop();
                Debug.Log($"BAKE_CATCH_VALIDATION_PASS: {assertions} assertions; physics, input, statistics, recipes, difficulty, timer, and UI flow.");
            }
        }
        catch (Exception error)
        {
            Stop();
            Debug.LogError("BAKE_CATCH_VALIDATION_FAIL: " + error);
        }
    }

    private static void Stop()
    {
        EditorApplication.update -= Tick;
        Application.logMessageReceived -= CaptureError;
        routine = null;
        InputSystem.onBeforeUpdate -= FeedInput;
        InputSystem.onAfterUpdate -= SampleInput;
        if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
        if (originalKeyboard != null && originalKeyboard.added) originalKeyboard.MakeCurrent();
        keyboard = null;
        heldKeys = Array.Empty<Key>();
        if (spawner != null) spawner.enabled = true;
    }

    private static void Expect(bool condition, string description)
    {
        if (!condition) throw new Exception(description);
        assertions++;
    }

    private static void Click(string screen, string button)
    {
        Transform target = ui.transform.Find("Layout/" + screen + "/" + button);
        Expect(target != null && target.gameObject.activeInHierarchy, "Missing active button: " + screen + "/" + button);
        target.GetComponent<Button>().onClick.Invoke();
    }

    private static bool Visible(string screen) => ui.transform.Find("Layout/" + screen).gameObject.activeInHierarchy;
    private static void Keys(params Key[] keys) => heldKeys = keys;
    private static void FeedInput()
    {
        if (keyboard != null && InputState.currentUpdateType == InputUpdateType.Dynamic)
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(heldKeys));
    }
    private static void SampleInput()
    {
        if (player != null && InputState.currentUpdateType == InputUpdateType.Dynamic)
            sampledInput = player.GetComponent<PlayerInputProvider>().HorizontalInput;
    }

    private static FallingObject Spawn(IngredientType type, Vector3 position, float speed = 4f)
    {
        FallingObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Ingredient.prefab").GetComponent<FallingObject>();
        FallingObject item = Object.Instantiate(prefab, position, Quaternion.identity);
        item.Configure(session, type, speed);
        return item;
    }

    private static IEnumerator<double> CheckGame()
    {
        CheckRules();
        session = Object.FindFirstObjectByType<GameSession>();
        spawner = Object.FindFirstObjectByType<IngredientSpawner>();
        player = Object.FindFirstObjectByType<PlayerController>();
        ui = Object.FindFirstObjectByType<BakeCatchUI>();
        // Local Editor fixture only; production now requires a selected profile before playing.
        session.SelectPlayer(new PlayerProfile("EDITOR", "Gameplay validation"));
        originalKeyboard = Keyboard.current;
        keyboard = InputSystem.AddDevice<Keyboard>("Bake Catch Validation Keyboard");
        keyboard.MakeCurrent();
        InputSystem.onBeforeUpdate += FeedInput;
        InputSystem.onAfterUpdate += SampleInput;
        Expect(session != null && spawner != null && player != null && ui != null && keyboard != null, "Scene wiring incomplete");
        Expect(Visible("Main Menu") && session.State == SessionState.Ready, "Launch must show Main Menu");
        System.IO.Directory.CreateDirectory("Library/BakeCatchValidation");
        ScreenCapture.CaptureScreenshot("Library/BakeCatchValidation/main-menu.png");
        yield return 0.15;
        Expect(Object.FindObjectsByType<FallingObject>(FindObjectsSortMode.None).Length == 0, "Original ingredient must stay inactive");
        Font font = AssetDatabase.LoadAssetAtPath<Font>("Assets/UI/Fonts/NotoSansThai.ttf");
        foreach (char c in "Bake Catchดีมากที่สุด") Expect(font.HasCharacter(c), "Missing font character: " + c);
        Click("Main Menu", "How to Play");
        Expect(Visible("How to Play"), "How to Play navigation");
        Click("How to Play", "Ready to bake");
        Expect(Visible("Level Select"), "Level Select navigation");
        Click("Level Select", "Select Easy");
        yield return 0.15;
        Expect(session.IsPlaying && session.Difficulty == DifficultyLevel.Easy && Visible("Gameplay HUD"), "Start Easy flow");
        spawner.enabled = false;

        float startX = player.transform.position.x;
        int inputStartFrame = Time.frameCount;
        Keys(Key.A);
        yield return 0.2;
        Expect(sampledInput == -1 && player.transform.position.x < startX, $"A must move left (input={sampledInput}, x={player.transform.position.x}, start={startX}, frames={Time.frameCount - inputStartFrame}, focused={Application.isFocused}, state={session.State})");
        Keys(Key.D);
        yield return 0.2;
        Expect(sampledInput == 1, "D must move right");
        Keys(Key.LeftArrow);
        yield return 0.2;
        Expect(sampledInput == -1, "Left Arrow input");
        Keys(Key.RightArrow);
        yield return 0.2;
        Expect(sampledInput == 1, "Right Arrow input");
        Keys(Key.A, Key.RightArrow);
        yield return 0.1;
        float stoppedX = player.transform.position.x;
        yield return 0.15;
        Expect(sampledInput == 0 && Mathf.Approximately(stoppedX, player.transform.position.x), "Opposite keys cancel");
        Keys();
        player.transform.position = new Vector3(7, -4, 0);
        Keys(Key.D);
        yield return 0.2;
        Expect(Mathf.Approximately(player.transform.position.x, 7) && player.transform.position.y == -4, "Right boundary and horizontal-only movement");
        player.transform.position = new Vector3(-7, -4, 0);
        Keys(Key.A);
        yield return 0.2;
        Expect(Mathf.Approximately(player.transform.position.x, -7), "Left boundary");
        Keys();
        player.transform.position = new Vector3(0, -4, 0);

        // Let real Rigidbody2D trigger callbacks resolve the whole first recipe.
        foreach (IngredientType type in new[] { IngredientType.Strawberry, IngredientType.Milk, IngredientType.Egg })
        {
            int before = session.Statistics.CatchCount;
            FallingObject item = Spawn(type, new Vector3(0, -1.5f, 0));
            yield return 0.6;
            Expect(item == null && session.Statistics.CatchCount == before + 1, "Physics catch must resolve exactly once: " + type);
        }
        Expect(session.Statistics.Score == 40 && session.Statistics.RecipeComplete == 1 && session.Recipe.Current.Name == "Pizza", "Recipe/score via real catches");
        FallingObject duplicate = Spawn(IngredientType.Chocolate, new Vector3(5, 1, 0));
        int count = session.Statistics.CatchCount;
        Expect(duplicate.TryCatch() && !duplicate.TryCatch(), "Duplicate catch guard");
        yield return 0.1;
        Expect(session.Statistics.CatchCount == count + 1, "Only one count for duplicate resolution");
        Spawn(IngredientType.Egg, new Vector3(5, -6.1f, 0));
        yield return 0.15;
        Expect(session.Statistics.MissCount == 1 && session.Statistics.CurrentCombo == 0, "Miss resolves and resets combo");
        int hazardScore = session.Statistics.Score;
        Spawn(IngredientType.Hazard, new Vector3(0, -1.5f, 0));
        yield return 0.6;
        Expect(session.Statistics.Score == Mathf.Max(0, hazardScore - 20) && session.Statistics.CatchCount == count + 1, "Hazard catch penalty without normal catch");
        Spawn(IngredientType.Hazard, new Vector3(5, -6.1f, 0));
        yield return 0.15;
        Expect(session.Statistics.MissCount == 1, "Avoided hazard is not a miss");

        FallingObject frozen = Spawn(IngredientType.Milk, new Vector3(5, 2, 0), 2);
        yield return 0.2;
        Keys(Key.Escape);
        yield return 0.1;
        Keys();
        Expect(session.State == SessionState.Paused && Visible("Pause Menu"), "Escape opens Pause");
        float pausedTime = session.Remaining;
        Vector3 pausedPosition = frozen.transform.position;
        Vector3 pausedPlayer = player.transform.position;
        count = session.Statistics.CatchCount;
        Keys(Key.D);
        yield return 0.35;
        Keys();
        Expect(session.Remaining == pausedTime && Vector3.Distance(frozen.transform.position, pausedPosition) < 0.01f && player.transform.position == pausedPlayer, "Pause freezes timer, ingredients, and player");
        Expect(!frozen.TryCatch() && session.Statistics.CatchCount == count, "Pause blocks catch statistics");
        Click("Pause Menu", "Resume");
        yield return 0.2;
        Expect(session.IsPlaying && session.Remaining < pausedTime && frozen.transform.position.y < pausedPosition.y, "Resume unfreezes session");
        Object.Destroy(frozen.gameObject);
        Click("Gameplay HUD", "Pause");
        yield return 0.1;
        Click("Pause Menu", "Restart");
        yield return 0.1;
        Expect(session.Statistics.Score == 0 && session.Statistics.CatchCount == 0 && session.Statistics.MissCount == 0 && session.Statistics.RecipeComplete == 0, "Restart clears statistics");
        Click("Gameplay HUD", "Pause");
        yield return 0.1;
        Click("Pause Menu", "Main Menu");
        yield return 0.1;
        Expect(Visible("Main Menu") && session.State == SessionState.Ready, "Pause to Main Menu");

        spawner.enabled = true;
        foreach (DifficultyLevel level in Enum.GetValues(typeof(DifficultyLevel)))
        {
            Click("Main Menu", "Start Game");
            Click("Level Select", "Select " + level);
            player.transform.position = new Vector3(-7, -4, 0);
            yield return level == DifficultyLevel.Easy ? 2.5 : 3.4;
            FallingObject[] items = Object.FindObjectsByType<FallingObject>(FindObjectsSortMode.None);
            Expect(items.Length == DifficultySettings.Get(level).MaxObjects, "Actual object density for " + level);
            if (level == DifficultyLevel.Easy)
                foreach (FallingObject item in items) Expect(!item.Definition.IsHazard, "Easy has no hazards");
            Click("Gameplay HUD", "Pause");
            yield return 0.1;
            Click("Pause Menu", "Main Menu");
            yield return 0.1;
            Expect(Object.FindObjectsByType<FallingObject>(FindObjectsSortMode.None).Length == 0, "Menu cleans live objects");
        }

        // Shorten only the live test instance; no scene or Inspector asset is saved.
        float configuredDuration = session.Duration;
        SerializedObject liveSession = new SerializedObject(session);
        liveSession.FindProperty("sessionDuration").floatValue = 1;
        liveSession.ApplyModifiedPropertiesWithoutUndo();
        Click("Main Menu", "Start Game");
        Click("Level Select", "Select Medium");
        player.transform.position = new Vector3(0, -4, 0);
        Spawn(IngredientType.Strawberry, new Vector3(0, -1.5f, 0));
        yield return 1.4;
        Expect(session.State == SessionState.Ended && session.Remaining == 0 && Visible("Result Screen"), "Timer ends at zero and shows Result");
        SessionResult snapshot = session.Result;
        Expect(snapshot.durationSec == 1 && snapshot.level == "Medium" && snapshot.score == 10 && snapshot.catchCount == 1 && snapshot.maxCombo == 1 && snapshot.accuracy == 100 && snapshot.leftAvgGrip == 0 && snapshot.rightAvgGrip == 0, "Result snapshot contains real catches and no invented sensor values");
        bool accuracyDisplayed = false;
        foreach (Text text in ui.transform.Find("Layout/Result Screen").GetComponentsInChildren<Text>())
            if (text.text.Contains("100.0%")) accuracyDisplayed = true;
        Expect(accuracyDisplayed, "Result UI displays snapshot accuracy");
        ScreenCapture.CaptureScreenshot("Library/BakeCatchValidation/result.png");
        yield return 0.15;
        session.Resolve(IngredientDefinition.Get(IngredientType.Golden), true);
        session.EndSession();
        Expect(ReferenceEquals(snapshot, session.Result) && session.Result.score == 10, "End is idempotent; scoring closed");
        Click("Result Screen", "Play Again");
        yield return 0.1;
        Expect(session.IsPlaying && session.Result == null && session.Difficulty == DifficultyLevel.Medium, "Play Again resets session, keeps selected level");
        yield return 1.3;
        Click("Result Screen", "Main Menu");
        yield return 0.1;
        Expect(Visible("Main Menu"), "Result to Main Menu");
        liveSession.Update();
        liveSession.FindProperty("sessionDuration").floatValue = configuredDuration;
        liveSession.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void CheckRules()
    {
        SessionStatistics stats = new SessionStatistics();
        RecipeProgress recipe = new RecipeProgress();
        Expect(stats.Accuracy == 0, "Empty accuracy is zero");
        stats.Resolve(IngredientDefinition.Get(IngredientType.Hazard), true, recipe);
        Expect(stats.Score == 0 && stats.CatchCount == 0, "Hazard score floor");
        stats.Resolve(IngredientDefinition.Get(IngredientType.Chocolate), true, recipe);
        Expect(recipe.Collected[0] == 0 && stats.Score == 30, "Non-required ingredient scores but does not progress recipe");
        string[] feedback = { "OK", "ดี", "ดีมาก", "ดีที่สุด", "Combo x6" };
        foreach (string expected in feedback)
        {
            stats.Resolve(IngredientDefinition.Get(IngredientType.Golden), true, recipe);
            Expect(stats.ComboFeedback == expected, "Required combo feedback: " + expected);
        }
        Expect(stats.MaxCombo == 6 && stats.Score == 280, "Combo maximum and golden score");
        stats.Resolve(IngredientDefinition.Get(IngredientType.Hazard), false, recipe);
        Expect(stats.MissCount == 0 && stats.CurrentCombo == 6, "Avoided hazards preserve accuracy and combo");
        stats.Resolve(IngredientDefinition.Get(IngredientType.Egg), false, recipe);
        Expect(stats.CurrentCombo == 0 && Mathf.Abs(stats.Accuracy - 600f / 7) < 0.01f, "Miss and accuracy formula");
        foreach (string name in new[] { "Strawberry Cake", "Pizza", "Burger" })
        {
            Expect(recipe.Current.Name == name, "Recipe sequence " + name);
            IngredientType[] required = recipe.Current.Ingredients;
            foreach (IngredientType type in required) stats.Resolve(IngredientDefinition.Get(type), true, recipe);
        }
        Expect(stats.RecipeComplete == 3 && recipe.Current.Name == "Strawberry Cake", "All recipes complete and cycle");
        Expect(DifficultySettings.Get(DifficultyLevel.Easy).FallSpeed < DifficultySettings.Get(DifficultyLevel.Medium).FallSpeed &&
               DifficultySettings.Get(DifficultyLevel.Medium).FallSpeed < DifficultySettings.Get(DifficultyLevel.Hard).FallSpeed, "Difficulty speeds increase");
        Expect(DifficultySettings.Get(DifficultyLevel.Hard).HazardChance > DifficultySettings.Get(DifficultyLevel.Medium).HazardChance, "Hard has more hazards");
    }
}
