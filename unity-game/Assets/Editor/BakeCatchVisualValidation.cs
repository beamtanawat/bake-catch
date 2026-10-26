using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using Object = UnityEngine.Object;

// Asset regressions are checked separately from the unchanged 103 gameplay assertions.
public static class BakeCatchVisualValidation
{
    private static IEnumerator<double> tour;
    private static double next;
    private static BakeCatchUI ui;
    private static GameSession session;
    private static IngredientSpawner spawner;
    private static EditorWindow gameView;
    private static bool wasMaximized;
    private static readonly List<GameObject> samples = new List<GameObject>();

    [MenuItem("Bake Catch/Check Gameplay Layout (Play Mode)")]
    public static void CheckGameplayLayout()
    {
        if (!EditorApplication.isPlaying) throw new Exception("Enter Play Mode first.");
        BakeCatchUI screens = Object.FindFirstObjectByType<BakeCatchUI>();
        Transform hud = screens.transform.Find("Layout/Gameplay HUD");
        foreach (Image image in hud.GetComponentsInChildren<Image>(true))
        {
            RectTransform rect = image.rectTransform;
            if (rect.sizeDelta.x > 200 && rect.sizeDelta.y > 70 && rect.anchoredPosition.y < -700)
                throw new Exception("Gameplay must not contain a large bottom-corner control card: " + image.name);
        }
        Debug.Log("BAKE_CATCH_BOTTOM_PANELS_PASS: no large bottom control cards.");
    }

    [MenuItem("Bake Catch/Review Visual Screens (Play Mode)")]
    public static void ReviewScreens()
    {
        if (!EditorApplication.isPlaying || tour != null)
        {
            Debug.LogWarning("Enter Play Mode before reviewing visuals; do not run alongside gameplay validation.");
            return;
        }
        ValidateAssets();
        ui = Object.FindFirstObjectByType<BakeCatchUI>();
        session = Object.FindFirstObjectByType<GameSession>();
        spawner = Object.FindFirstObjectByType<IngredientSpawner>();
        session.MainMenu();
        session.SelectPlayer(new PlayerProfile("EDITOR", "Visual validation"));
        spawner.enabled = false;
        gameView = EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor"));
        wasMaximized = gameView.maximized;
        gameView.maximized = true;
        gameView.Focus();
        System.IO.Directory.CreateDirectory("Library/BakeCatchVisualReview");
        tour = Screens();
        next = EditorApplication.timeSinceStartup + .3;
        EditorApplication.update += Tick;
    }

    private static void Tick()
    {
        if (EditorApplication.timeSinceStartup < next) return;
        try
        {
            if (!EditorApplication.isPlaying) { Finish(); return; }
            if (tour.MoveNext()) next = EditorApplication.timeSinceStartup + tour.Current;
            else { Finish(); Debug.Log("BAKE_CATCH_VISUAL_REVIEW_PASS: real recipe checks, all ingredient sprites, collider preservation, keyboard labels; screens captured."); }
        }
        catch (Exception e) { Finish(); Debug.LogError("BAKE_CATCH_VISUAL_REVIEW_FAIL: " + e); }
    }

    private static void Finish()
    {
        EditorApplication.update -= Tick;
        tour = null;
        foreach (GameObject sample in samples) if (sample != null) Object.Destroy(sample);
        samples.Clear();
        if (session != null) session.MainMenu();
        if (spawner != null) spawner.enabled = true;
        if (gameView != null) gameView.maximized = wasMaximized;
    }

    private static void Click(string screen, string button) =>
        ui.transform.Find("Layout/" + screen + "/" + button).GetComponent<Button>().onClick.Invoke();

    private static void Capture(string name) => ScreenCapture.CaptureScreenshot("Library/BakeCatchVisualReview/" + name + ".png");

    private static IEnumerator<double> Screens()
    {
        Debug.Log($"BAKE_CATCH_VISUAL_REVIEW_SIZE: {Screen.width}x{Screen.height}");
        Capture("01-main-menu"); yield return .3;
        Click("Main Menu", "How to Play"); yield return .3;
        Capture("02-how-to-play"); yield return .3;
        Click("How to Play", "Ready to bake"); yield return .3;
        Capture("03-level-select"); yield return .3;
        Click("Level Select", "Select Medium"); yield return .3;
        spawner.enabled = true;
        yield return 2.2;
        Capture("04-live-gameplay"); yield return .3;
        spawner.enabled = false;
        spawner.ClearObjects();
        Transform hud = ui.transform.Find("Layout/Gameplay HUD");
        CheckGameplayLayout();
        RectTransform layout = (RectTransform)ui.transform.Find("Layout");
        Vector2 originalPosition = layout.anchoredPosition;
        layout.anchoredPosition += new Vector2(37, 23);
        yield return .3;
        Transform chef = Object.FindFirstObjectByType<PlayerController>().transform;
        Vector2 expectedCatch = Camera.main.WorldToScreenPoint(chef.TransformPoint(new Vector3(0, 2.02f, 0)));
        Vector2 shownCatch = RectTransformUtility.WorldToScreenPoint(null,
            hud.Find("Catch Zone").TransformPoint(new Vector3(100, -18, 0)));
        layout.anchoredPosition = originalPosition;
        bool aligned = Vector2.Distance(expectedCatch, shownCatch) <= 3;
        float originalAspect = Camera.main.aspect;
        Camera.main.aspect = 2.4f;
        yield return .3;
        SpriteRenderer backdrop = Object.FindFirstObjectByType<BakeryBackdrop>().GetComponentInChildren<SpriteRenderer>();
        bool covers = backdrop.bounds.size.x >= Camera.main.orthographicSize * 2 * Camera.main.aspect - .01f;
        Camera.main.aspect = originalAspect;
        if (!aligned || !covers) throw new Exception($"Viewport regression: catch label aligned={aligned}, bakery covers wider viewport={covers}");
        yield return .3;
        foreach (Text text in hud.GetComponentsInChildren<Text>())
            if (text.text.Contains("%")) throw new Exception("Keyboard HUD must not present fake grip percentages.");
        FallingObject prefab = AssetDatabase.LoadAssetAtPath<FallingObject>("Assets/Prefabs/Ingredient.prefab");
        for (int i = 0; i < 11; i++)
        {
            FallingObject item = Object.Instantiate(prefab, new Vector3(-3.7f + i % 6 * 1.9f, 2.15f - i / 6 * 1.9f, 0), Quaternion.identity);
            item.Configure(session, (IngredientType)i, 0);
            samples.Add(item.gameObject);
        }
        yield return .3;
        for (int i = 0; i < samples.Count; i++)
        {
            GameObject sample = samples[i];
            if (sample.GetComponent<SpriteRenderer>().sprite != BakeryArt.Icon(i)) throw new Exception("Wrong ingredient sprite: " + i);
            if (sample.GetComponent<CircleCollider2D>().radius != .44f || sample.transform.localScale.x != .72f)
                throw new Exception("Presentation changed ingredient collider dimensions.");
        }
        session.Resolve(IngredientDefinition.Get(IngredientType.Strawberry), true);
        yield return .3;
        bool progress = false, check = false;
        foreach (Text text in hud.GetComponentsInChildren<Text>())
        {
            progress |= text.text == "1/1";
            check |= text.text == "✓";
        }
        if (!progress || !check) throw new Exception("Caught strawberry must update recipe count and completion check.");
        Capture("04-gameplay-ingredients"); yield return .3;
        session.Pause(); yield return .3;
        Capture("05-pause"); yield return .3;
        session.Resume();
        session.Resolve(IngredientDefinition.Get(IngredientType.Milk), true);
        session.Resolve(IngredientDefinition.Get(IngredientType.Egg), true);
        yield return .3;
        if (hud.Find("Recipe illustration").GetComponent<Image>().sprite != BakeryArt.Icon(12))
            throw new Exception("Completed cake must show pizza artwork.");
        foreach (IngredientType type in new[] { IngredientType.Cheese, IngredientType.Tomato, IngredientType.Dough })
            session.Resolve(IngredientDefinition.Get(type), true);
        yield return .3;
        if (hud.Find("Recipe illustration").GetComponent<Image>().sprite != BakeryArt.Icon(13))
            throw new Exception("Completed pizza must show burger artwork.");
        session.EndSession(); yield return .3;
        Capture("06-result"); yield return .3;
        Click("Result Screen", "Main Menu"); yield return .3;
    }

    [MenuItem("Bake Catch/Validate Visual Assets")]
    public static void ValidateAssets()
    {
        foreach (string name in new[] { "Bakery", "Chef", "Ingredients", "Logo" })
        {
            Texture2D texture = Resources.Load<Texture2D>("BakeCatch/" + name);
            if (texture == null || texture.width < 1000)
                throw new Exception("Missing full-resolution presentation artwork: " + name);
            string path = AssetDatabase.GetAssetPath(texture);
            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (name != "Bakery" && !importer.DoesSourceTextureHaveAlpha())
                throw new Exception("Cutout artwork needs an alpha channel: " + name);
        }
        Debug.Log("BAKE_CATCH_VISUAL_ASSETS_PASS: four full-resolution assets; cutouts contain alpha.");
    }
}
