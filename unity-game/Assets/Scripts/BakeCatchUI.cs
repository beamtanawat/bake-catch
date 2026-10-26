using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using System.Runtime.InteropServices;

// Presentation only: all values and actions still come from the existing session.
public class BakeCatchUI : MonoBehaviour
{
    [SerializeField] private GameSession session;
    [SerializeField] private Font font;
    private Transform root;
    private GameObject menu, levels, instructions, hud, pause, result, profileScreen, historyScreen, historyDetail, historyReturn;
    private InputField profileName;
    private bool findingPlayer;
    private Text profileError, playerLabel;
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void BakeCatchFirebaseProfile(string profile);
    [DllImport("__Internal")] private static extern void BakeCatchFirebaseFindPlayer(string profile, string target);
    [DllImport("__Internal")] private static extern void BakeCatchFirebaseHistory(string request, string target);
    [DllImport("__Internal")] private static extern int BakeCatchFirebaseSaveState();
#endif
    private Text timeLabel, scoreLabel, comboLabel, levelLabel, recipeLabel, feedback;
    private Text resultPlayer, saveStatus, historySummary, historyMessage, historyPageLabel, detailTitle;
    private readonly Text[] resultValues = new Text[10], detailValues = new Text[10];
    private readonly Text[] historyDates = new Text[10], historyRows = new Text[10];
    private readonly Button[] historyDetails = new Button[10];
    private Button historyNewer, historyOlder;
    private PlayerHistory loadedHistory;
    private int historyRequest, historyPage;
    private float nextSaveCheck;
    private Text catchLabel, resultScore;
    private readonly Text[] rowNames = new Text[3], rowCounts = new Text[3], rowChecks = new Text[3];
    private readonly Image[] rowIcons = new Image[3];
    private Image timeFill, recipeIcon;
    private Transform chef;
    private SessionState shownState;
    private readonly Color ink = BakeryArt.Hex("#592A24");
    private readonly Color cream = BakeryArt.Hex("#FFF8EB");
    private readonly Color pink = BakeryArt.Hex("#FF537D");

    private void Start()
    {
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1600, 900);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        gameObject.AddComponent<GraphicRaycaster>();
        if (EventSystem.current == null)
        {
            GameObject events = new GameObject("UI Event System", typeof(EventSystem), typeof(InputSystemUIInputModule));
            events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }
        root = Rect("Layout", transform, 0, 0, 1600, 900);
        RectTransform layout = (RectTransform)root;
        layout.anchorMin = layout.anchorMax = layout.pivot = new Vector2(.5f, .5f);
        layout.anchoredPosition = Vector2.zero;
        chef = FindFirstObjectByType<PlayerController>().transform;
        BuildMenu();
        BuildProfile();
        BuildLevels();
        BuildInstructions();
        BuildHud();
        BuildPause();
        BuildResult();
        BuildHistory();
        OpenProfile();
        shownState = SessionState.Ready;
    }

    private RectTransform Rect(string name, Transform parent, float x, float y, float width, float height)
    {
        RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
        return rect;
    }

    private Image Panel(Transform parent, string name, float x, float y, float width, float height, Color color)
    {
        Image image = Rect(name, parent, x, y, width, height).gameObject.AddComponent<Image>();
        image.sprite = BakeryArt.Round;
        image.type = Image.Type.Sliced;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private Image Card(Transform parent, string name, float x, float y, float width, float height, Color fill, Color edge)
    {
        Panel(parent, name + " shadow", x, y + 7, width, height, new Color(.35f, .13f, .08f, .24f));
        Panel(parent, name + " cream rim", x - 3, y - 3, width + 6, height + 6, cream);
        Image card = Panel(parent, name, x, y, width, height, edge);
        Panel(card.transform, "Inset", 3, 3, width - 6, height - 6, fill);
        return card;
    }

    private Image Picture(Transform parent, string name, Sprite sprite, float x, float y, float width, float height)
    {
        Image image = Rect(name, parent, x, y, width, height).gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = true;
        image.raycastTarget = false;
        return image;
    }

    private void Icon(Transform parent, int icon, float x, float y, float size) =>
        Picture(parent, "Decoration " + icon, BakeryArt.Icon(icon), x, y, size, size);

    private Text Label(Transform parent, string value, float x, float y, float width, float height, int size,
        Color? color = null, TextAnchor alignment = TextAnchor.MiddleCenter)
    {
        Text text = Rect(value, parent, x, y, width, height).gameObject.AddComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.fontStyle = FontStyle.Bold;
        text.color = color ?? ink;
        text.alignment = alignment;
        text.text = value;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        return text;
    }

    private Button Action(Transform parent, string title, float x, float y, float width, float height,
        UnityEngine.Events.UnityAction action, bool primary = true, Color? accent = null)
    {
        Color tone = accent ?? pink;
        Image image = Card(parent, title, x, y, width, height, primary ? tone : cream, tone);
        image.raycastTarget = true;
        Button button = image.gameObject.AddComponent<Button>();
        // Tint the inset, so hover/focus never erases the decorative outline.
        button.targetGraphic = image.transform.GetChild(0).GetComponent<Image>();
        ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(1, .92f, .8f);
        colors.selectedColor = new Color(1, .92f, .8f);
        colors.pressedColor = new Color(.9f, .75f, .7f);
        button.colors = colors;
        button.onClick.AddListener(action);
        Panel(image.transform, "Shine", 12, 7, width - 24, 3, new Color(1, 1, 1, .35f));
        Label(image.transform, title, 8, 0, width - 16, height, 27, primary ? Color.white : ink);
        return button;
    }

    private GameObject Screen(string name)
    {
        GameObject screen = Rect(name, root, 0, 0, 1600, 900).gameObject;
        screen.SetActive(false);
        return screen;
    }

    private void Heading(Transform parent, string title, string subtitle)
    {
        Card(parent, "Title ribbon", 480, 55, 640, 80, pink, BakeryArt.Hex("#D83965"));
        Icon(parent, 15, 488, 46, 91);
        Label(parent, title, 578, 60, 508, 75, 38, Color.white);
        Label(parent, subtitle, 190, 155, 1220, 45, 24);
    }

    private void BuildMenu()
    {
        menu = Screen("Main Menu");
        Card(menu.transform, "Welcome", 138, 150, 690, 618, new Color(1, .96f, .88f, .96f), BakeryArt.Hex("#EDAD83"));
        Picture(menu.transform, "Bake Catch logo", BakeryArt.Artwork("Logo"), 195, 60, 580, 350);
        Label(menu.transform, "A little movement. A little sweetness.", 185, 422, 595, 44, 26);
        Action(menu.transform, "Start Game", 238, 505, 490, 76, () => { if (session.Player == null) OpenProfile(); else Show(levels); });
        Action(menu.transform, "How to Play", 238, 595, 490, 58, () => Show(instructions), false);
        Action(menu.transform, "Player Profile", 238, 669, 235, 50, OpenProfile, false).GetComponentInChildren<Text>().fontSize = 22;
        Action(menu.transform, "My History", 493, 669, 235, 50, OpenHistory, false).GetComponentInChildren<Text>().fontSize = 22;
        playerLabel = Label(menu.transform, "Choose a player before playing", 170, 730, 630, 30, 19);
        Picture(menu.transform, "Welcome chef", BakeryArt.Artwork("Chef"), 900, 247, 555, 590);
        Icon(menu.transform, 0, 835, 300, 100);
        Icon(menu.transform, 11, 1330, 650, 150);
        Icon(menu.transform, 9, 1380, 210, 85);
        Card(menu.transform, "Controls reminder", 430, 820, 740, 47, cream, BakeryArt.Hex("#F0B28F"));
        Label(menu.transform, "A / Left Arrow     •     D / Right Arrow", 450, 820, 700, 47, 22);
    }

    private InputField ProfileField(string title, float y, int limit)
    {
        Label(profileScreen.transform, title, 450, y - 40, 700, 35, 24, null, TextAnchor.MiddleLeft);
        Image card = Card(profileScreen.transform, title + " input", 450, y, 700, 65, Color.white, pink);
        card.raycastTarget = true;
        InputField input = card.gameObject.AddComponent<InputField>();
        input.targetGraphic = card.transform.GetChild(0).GetComponent<Image>();
        input.textComponent = Label(card.transform, "", 18, 0, 664, 65, 27, null, TextAnchor.MiddleLeft);
        input.textComponent.supportRichText = false;
        input.characterLimit = limit;
        input.lineType = InputField.LineType.SingleLine;
        return input;
    }

    private void BuildProfile()
    {
        profileScreen = Screen("Player Profile");
        Heading(profileScreen.transform, "Welcome to Bake Catch", "Enter your player name. Use the same name to continue your history.");
        Card(profileScreen.transform, "Profile form", 390, 235, 820, 515, cream, BakeryArt.Hex("#EDAD83"));
        Picture(profileScreen.transform, "Profile chef", BakeryArt.Artwork("Chef"), 75, 390, 270, 350);
        profileName = ProfileField("Player name / ชื่อผู้เล่น", 365, 40);
        profileError = Label(profileScreen.transform, "", 435, 510, 730, 55, 20, BakeryArt.Hex("#B52D45"));
        Action(profileScreen.transform, "Continue", 525, 580, 550, 68, ConfirmPlayer);
        Label(profileScreen.transform, "A nickname is enough. This is a profile, not a login.", 435, 675, 730, 40, 20);
    }

    private void OpenProfile()
    {
        string lastCode = PlayerPrefs.GetString("BakeCatch.PlayerCode", "");
        profileName.text = session.Player?.displayName ?? PlayerPrefs.GetString("BakeCatch.PlayerName." + lastCode, "");
        profileError.text = "";
        Show(profileScreen);
        profileName.ActivateInputField();
    }

    private void ConfirmPlayer()
    {
        if (findingPlayer) return;
        string name = profileName.text.Trim();
        string code = PlayerPrefs.GetString("BakeCatch.PlayerByName." + name, "");
        string lastCode = PlayerPrefs.GetString("BakeCatch.PlayerCode", "");
        if (code.Length == 0 && name == PlayerPrefs.GetString("BakeCatch.PlayerName." + lastCode, "")) code = lastCode;
        if (code.Length == 0) code = "P" + System.Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant();
        try {
            PlayerProfile profile = new PlayerProfile(code, name);
#if UNITY_WEBGL && !UNITY_EDITOR
            findingPlayer = true;
            profileName.interactable = false;
            profileError.text = "Finding your player history…";
            BakeCatchFirebaseFindPlayer(JsonUtility.ToJson(profile), gameObject.name);
#else
            ReceivePlayerProfile(JsonUtility.ToJson(profile));
#endif
        } catch (System.ArgumentException) { profileError.text = "Enter a name (1–40 characters), without < or >."; }
    }

    public void ReceivePlayerError(string message)
    {
        findingPlayer = false;
        profileName.interactable = true;
        profileError.text = message;
    }

    public void ReceivePlayerProfile(string json)
    {
        findingPlayer = false;
        profileName.interactable = true;
        try {
            PlayerProfile parsed = JsonUtility.FromJson<PlayerProfile>(json);
            PlayerProfile profile = new PlayerProfile(parsed.code, parsed.displayName);
            if (!UsePlayer(profile)) { profileError.text = "Finish this round before changing player."; return; }
            PlayerPrefs.SetString("BakeCatch.PlayerCode", profile.code);
            PlayerPrefs.SetString("BakeCatch.PlayerName." + profile.code, profile.displayName);
            PlayerPrefs.SetString("BakeCatch.PlayerByName." + profile.displayName, profile.code);
            PlayerPrefs.Save();
            Show(menu);
        } catch (System.ArgumentException error) { profileError.text = error.Message; }
    }

    private bool UsePlayer(PlayerProfile profile)
    {
        if (!session.SelectPlayer(profile)) return false;
        historyRequest++;
        loadedHistory = null;
        playerLabel.text = profile.displayName;
#if UNITY_WEBGL && !UNITY_EDITOR
        BakeCatchFirebaseProfile(JsonUtility.ToJson(profile));
#endif
        return true;
    }

    private void BuildLevels()
    {
        levels = Screen("Level Select");
        Card(levels.transform, "Level backdrop", 75, 75, 1450, 743, cream, BakeryArt.Hex("#F0BDA3"));
        Heading(levels.transform, "Select your level", "Choose a comfortable pace. Every catch is a small win.");
        string[] names = { "Easy", "Medium", "Hard" };
        string[] descriptions = {
            "Slow falling speed\n1 ingredient at a time\nPredictable lanes\nNo hazards",
            "Medium falling speed\n1–2 objects at a time\nRandom positions\nSome hazards",
            "Fast falling speed\n2–3 objects at a time\nRandom positions\nMore hazards"
        };
        string[] colors = { "#58C66B", "#F4AA32", "#FF6582" };
        string[] fills = { "#ECF8E6", "#FFF4D9", "#FFE8ED" };
        for (int i = 0; i < 3; i++)
        {
            DifficultyLevel difficulty = (DifficultyLevel)i;
            float x = 139 + 450 * i;
            Color tone = BakeryArt.Hex(colors[i]);
            Card(levels.transform, names[i] + " card", x, 236, 420, 460, BakeryArt.Hex(fills[i]), tone);
            Panel(levels.transform, names[i] + " cap", x + 4, 240, 412, 116, tone);
            Icon(levels.transform, 15, x + 171, 253, 76);
            Label(levels.transform, names[i], x + 20, 359, 380, 55, 35);
            Label(levels.transform, descriptions[i], x + 25, 424, 370, 140, 23);
            Icon(levels.transform, i == 0 ? 0 : i == 1 ? 3 : 10, x + 136, 565, 65);
            Icon(levels.transform, i == 0 ? 1 : i == 1 ? 8 : 4, x + 219, 565, 65);
            Action(levels.transform, "Select " + names[i], x + 39, 642, 342, 64, () => Begin(difficulty), true, tone);
        }
        Action(levels.transform, "Back", 650, 748, 300, 56, () => Show(menu), false);
    }

    private void BuildInstructions()
    {
        instructions = Screen("How to Play");
        Card(instructions.transform, "Instructions card", 95, 75, 1410, 742, cream, BakeryArt.Hex("#F0BDA3"));
        Heading(instructions.transform, "How to play", "Move underneath an ingredient. Your Catch Zone catches it automatically.");
        string[] titles = { "1   MOVE", "2   COLLECT", "3   WATCH OUT" };
        string[] copy = {
            "A / Left Arrow: move left\nD / Right Arrow: move right\nBoth directions: stop",
            "Follow your order card.\nCollect its three ingredients.\nThen bake the next recipe!",
            "Avoid the dark bombs.\nCatches build your combo.\nMisses reset your combo."
        };
        for (int i = 0; i < 3; i++)
        {
            float x = 140 + 450 * i;
            Card(instructions.transform, titles[i], x, 260, 420, 355, BakeryArt.Hex("#FFF0E6"), BakeryArt.Hex("#F5C6AF"));
            Icon(instructions.transform, i == 0 ? 15 : i == 1 ? 11 : 10, x + 148, 280, 124);
            Label(instructions.transform, titles[i], x + 15, 413, 390, 48, 28, pink);
            Label(instructions.transform, copy[i], x + 15, 470, 390, 125, 22);
        }
        Label(instructions.transform, $"{session.Duration:0} seconds  •  Esc to pause  •  No catch button needed", 200, 648, 1200, 45, 24);
        Action(instructions.transform, "Ready to bake", 550, 721, 500, 65, () => Show(levels));
    }

    private void BuildHud()
    {
        hud = Screen("Gameplay HUD");
        Picture(hud.transform, "Bake Catch logo", BakeryArt.Artwork("Logo"), 12, 6, 330, 180);
        Card(hud.transform, "Top bar", 356, 20, 1092, 119, cream, BakeryArt.Hex("#EEA686"));
        Icon(hud.transform, 14, 368, 33, 80);
        Label(hud.transform, "Time", 451, 23, 144, 32, 22);
        timeLabel = Label(hud.transform, FormatTime(session.Duration), 440, 56, 171, 65, 42);
        Icon(hud.transform, 9, 626, 43, 67);
        Label(hud.transform, "Score", 702, 23, 150, 32, 22);
        scoreLabel = Label(hud.transform, "0", 695, 56, 174, 65, 42);
        Card(hud.transform, "Combo badge", 886, 43, 244, 79, BakeryArt.Hex("#FFE3ED"), BakeryArt.Hex("#FF99B4"));
        Label(hud.transform, "Combo", 916, 19, 184, 34, 22);
        comboLabel = Label(hud.transform, "x0", 900, 50, 217, 63, 43, pink);
        Label(hud.transform, "Difficulty", 1150, 23, 268, 32, 22);
        Card(hud.transform, "Difficulty badge", 1161, 63, 250, 58, BakeryArt.Hex("#FFD978"), BakeryArt.Hex("#EAA12E"));
        levelLabel = Label(hud.transform, "Easy", 1170, 63, 232, 58, 29);
        Button pauseButton = Action(hud.transform, "Pause", 1480, 33, 91, 91, () => session.Pause());
        foreach (string part in new[] { "Pause", "Pause shadow", "Pause cream rim", "Pause/Inset" })
        {
            Image circle = hud.transform.Find(part).GetComponent<Image>();
            circle.sprite = BakeryArt.Circle;
            circle.type = Image.Type.Simple;
        }
        pauseButton.GetComponentInChildren<Text>().text = "";
        pauseButton.transform.Find("Shine").gameObject.SetActive(false);
        Panel(pauseButton.transform, "Left pause bar", 28, 26, 11, 39, Color.white);
        Panel(pauseButton.transform, "Right pause bar", 52, 26, 11, 39, Color.white);
        Label(hud.transform, "Pause", 1473, 137, 105, 28, 17, cream);
        Panel(hud.transform, "Time track", 456, 120, 138, 5, BakeryArt.Hex("#F6DCE0"));
        timeFill = Panel(hud.transform, "Time remaining", 456, 120, 138, 5, pink);

        Card(hud.transform, "Order card", 28, 205, 338, 392, cream, BakeryArt.Hex("#E69C73"));
        Label(hud.transform, "Order", 141, 218, 194, 37, 27);
        recipeIcon = Picture(hud.transform, "Recipe illustration", BakeryArt.Icon(11), 43, 224, 90, 95);
        recipeLabel = Label(hud.transform, "Strawberry Cake", 138, 261, 211, 59, 25, pink);
        for (int i = 0; i < 3; i++)
        {
            float y = 331 + 83 * i;
            Panel(hud.transform, "Row divider " + i, 45, y - 3, 302, 2, BakeryArt.Hex("#F4D6BA"));
            rowIcons[i] = Picture(hud.transform, "Order icon " + i, BakeryArt.Icon(i), 47, y + 7, 62, 63);
            rowNames[i] = Label(hud.transform, "", 116, y + 12, 137, 50, 21, ink, TextAnchor.MiddleLeft);
            rowCounts[i] = Label(hud.transform, "", 252, y + 12, 53, 50, 23);
            rowChecks[i] = Label(hud.transform, "", 310, y + 12, 37, 50, 23, BakeryArt.Hex("#25A94F"));
        }
        feedback = Label(hud.transform, "", 477, 160, 905, 76, 25, ink);
        Outline feedbackOutline = feedback.gameObject.AddComponent<Outline>();
        feedbackOutline.effectColor = cream;
        feedbackOutline.effectDistance = new Vector2(2, -2);
        catchLabel = Label(hud.transform, "Catch Zone", 700, 640, 200, 35, 22, pink);
        Outline outline = catchLabel.gameObject.AddComponent<Outline>();
        outline.effectColor = cream;
        outline.effectDistance = new Vector2(2, -2);
    }

    private void BuildPause()
    {
        pause = Screen("Pause Menu");
        Panel(pause.transform, "Dim", 0, 0, 1600, 900, new Color(.25f, .1f, .09f, .66f)).raycastTarget = true;
        Card(pause.transform, "Pause card", 525, 185, 550, 520, cream, pink);
        Card(pause.transform, "Pause ribbon", 565, 158, 470, 86, pink, BakeryArt.Hex("#DD3A62"));
        Icon(pause.transform, 15, 594, 156, 86);
        Label(pause.transform, "Pause", 697, 173, 260, 67, 40, Color.white);
        Label(pause.transform, "Take a little break.", 575, 275, 450, 42, 24);
        Action(pause.transform, "Resume", 605, 349, 390, 70, () => session.Resume());
        Action(pause.transform, "Restart", 605, 445, 390, 70, () => Begin(session.Difficulty), false);
        Action(pause.transform, "Main Menu", 605, 541, 390, 70, ReturnMenu, false);
        Label(pause.transform, "Your bakery can wait.", 605, 643, 390, 30, 19);
    }

    private void BuildResult()
    {
        result = Screen("Result Screen");
        Card(result.transform, "Results card", 167, 107, 1266, 689, cream, BakeryArt.Hex("#EDAB94"));
        Heading(result.transform, "Sweet work!", "Every session is another step forward.");
        Picture(result.transform, "Celebration chef", BakeryArt.Artwork("Chef"), 215, 278, 407, 376);
        for (int i = 0; i < 3; i++) Icon(result.transform, 9, 277 + i * 96, 223 - (i == 1 ? 23 : 0), 87);
        Icon(result.transform, 11, 190, 583, 119);
        resultPlayer = Label(result.transform, "", 225, 640, 435, 60, 24, pink);
        resultPlayer.supportRichText = false;
        Card(result.transform, "Score plaque", 713, 224, 630, 107, BakeryArt.Hex("#FFE8D6"), BakeryArt.Hex("#F3C596"));
        Label(result.transform, "Score", 744, 234, 170, 77, 28);
        resultScore = Label(result.transform, "0", 929, 234, 370, 77, 49, pink);
        BuildStatRows(result.transform, resultValues, 724, 338, 608);
        saveStatus = Label(result.transform, "", 225, 692, 1150, 30, 19);
        Action(result.transform, "Play Again", 225, 735, 345, 55, () => Begin(session.Difficulty));
        Action(result.transform, "My History", 630, 735, 345, 55, OpenHistory, false);
        Action(result.transform, "Main Menu", 1035, 735, 345, 55, ReturnMenu, false);
    }

    private void BuildStatRows(Transform parent, Text[] values, float x, float y, float width)
    {
        string[] names = { "Score", "Max Combo", "Catch Count", "Miss Count", "Accuracy", "Play Time", "Level", "Recipe Complete", "Left Grip Input", "Right Grip Input" };
        for (int i = 0; i < names.Length; i++)
        {
            Panel(parent, "Stat stripe " + i, x, y + i * 34, width, 33, BakeryArt.Hex(i % 2 == 0 ? "#FFF0E7" : "#FFF8EB"));
            Label(parent, names[i], x + 20, y + i * 34, width * .57f, 33, 21, ink, TextAnchor.MiddleLeft);
            values[i] = Label(parent, "", x + width * .59f, y + i * 34, width * .37f, 33, 21, ink, TextAnchor.MiddleRight);
        }
    }

    private static string GripText(SessionResult r, bool left) => r.hasGripSamples ? (left ? r.leftAvgGrip : r.rightAvgGrip).ToString("0.0") + "%" : "No data";

    private static void FillStatRows(Text[] values, SessionResult r)
    {
        string[] text = { r.score.ToString(), r.maxCombo.ToString(), r.catchCount.ToString(), r.missCount.ToString(),
            r.accuracy.ToString("0.0") + "%", FormatTime(r.durationSec), r.level, r.recipeComplete.ToString(), GripText(r, true), GripText(r, false) };
        for (int i = 0; i < values.Length; i++) values[i].text = text[i];
    }

    private void BuildHistory()
    {
        historyScreen = Screen("My History");
        Card(historyScreen.transform, "History card", 75, 75, 1450, 743, cream, BakeryArt.Hex("#F0BDA3"));
        Heading(historyScreen.transform, "My History", "Your own bakery adventures, newest first.");
        historySummary = Label(historyScreen.transform, "", 150, 205, 1300, 65, 25);
        historySummary.supportRichText = false;
        historyMessage = Label(historyScreen.transform, "", 150, 390, 1300, 90, 26);
        for (int i = 0; i < 10; i++)
        {
            int row = i;
            float y = 285 + i * 39;
            Panel(historyScreen.transform, "History stripe " + i, 130, y, 1340, 37, BakeryArt.Hex(i % 2 == 0 ? "#FFF0E7" : "#FFF8EB"));
            historyDates[i] = Label(historyScreen.transform, "", 155, y, 370, 37, 18, ink, TextAnchor.MiddleLeft);
            historyRows[i] = Label(historyScreen.transform, "", 535, y, 640, 37, 20, ink, TextAnchor.MiddleLeft);
            historyDetails[i] = Action(historyScreen.transform, "Details " + (i + 1), 1220, y, 220, 36, () => OpenHistoryDetail(row), false);
            historyDetails[i].GetComponentInChildren<Text>().fontSize = 20;
        }
        historyPageLabel = Label(historyScreen.transform, "", 550, 683, 500, 35, 21);
        Action(historyScreen.transform, "Back", 170, 738, 250, 55, () => Show(historyReturn), false);
        historyNewer = Action(historyScreen.transform, "Newer", 470, 738, 250, 55, () => { historyPage--; RenderHistory(); }, false);
        historyOlder = Action(historyScreen.transform, "Older", 770, 738, 250, 55, () => { historyPage++; RenderHistory(); }, false);
        Action(historyScreen.transform, "Refresh", 1070, 738, 300, 55, LoadHistory, false).GetComponentInChildren<Text>().fontSize = 23;
        historyDetail = Screen("History Details");
        Card(historyDetail.transform, "Detail card", 220, 75, 1160, 743, cream, BakeryArt.Hex("#F0BDA3"));
        Heading(historyDetail.transform, "Session details", "Grip input is relative to the controller threshold, not muscle strength.");
        detailTitle = Label(historyDetail.transform, "", 280, 210, 1040, 60, 25);
        detailTitle.supportRichText = false;
        BuildStatRows(historyDetail.transform, detailValues, 400, 285, 800);
        Action(historyDetail.transform, "Back to History", 550, 730, 500, 60, () => Show(historyScreen), false);
        historyMessage.transform.SetAsLastSibling();
    }

    private void OpenHistory()
    {
        if (session.Player == null) { OpenProfile(); return; }
        historyReturn = result.activeSelf ? result : menu;
        Show(historyScreen);
        LoadHistory();
    }

    private void LoadHistory()
    {
        loadedHistory = null;
        historyPage = 0;
        historyRequest++;
        RenderHistory();
        historySummary.text = session.Player.displayName;
        historyMessage.text = "Loading your saved rounds…";
#if UNITY_WEBGL && !UNITY_EDITOR
        BakeCatchFirebaseHistory(JsonUtility.ToJson(new PlayerHistoryRequest { requestId = historyRequest, profile = session.Player.Copy() }), gameObject.name);
#else
        historyMessage.text = "Firebase history is available in the WebGL game. No preview data is created.";
#endif
    }

    public void ReceivePlayerHistory(string json)
    {
        PlayerHistory data = JsonUtility.FromJson<PlayerHistory>(json);
        if (!historyScreen.activeSelf || data.requestId != historyRequest || data.code != session.Player?.code) return;
        if (!string.IsNullOrEmpty(data.error)) { historyMessage.text = data.error; return; }
        loadedHistory = data;
        RenderHistory();
    }

    private void RenderHistory()
    {
        PlayerHistoryItem[] items = loadedHistory?.items ?? System.Array.Empty<PlayerHistoryItem>();
        int pages = Mathf.Max(1, (items.Length + 9) / 10);
        historyPage = Mathf.Clamp(historyPage, 0, pages - 1);
        historySummary.text = loadedHistory == null ? "" : $"{loadedHistory.displayName}\nRounds: {loadedHistory.totalPlayCount}     Best Score: {loadedHistory.bestScore}";
        historyMessage.text = loadedHistory != null && items.Length == 0 ? "No saved rounds yet. Play your first game!" : "";
        historyPageLabel.text = loadedHistory == null || items.Length == 0 ? "" : $"Page {historyPage + 1} / {pages}";
        historyNewer.interactable = historyPage > 0;
        historyOlder.interactable = historyPage + 1 < pages;
        for (int i = 0; i < 10; i++)
        {
            int index = historyPage * 10 + i;
            bool visible = index < items.Length;
            historyDetails[i].gameObject.SetActive(visible);
            historyDates[i].text = visible ? items[index].date : "";
            historyRows[i].text = visible ? $"{items[index].result.level}    Score {items[index].result.score}    Accuracy {items[index].result.accuracy:0.0}%" : "";
        }
    }

    private void OpenHistoryDetail(int row)
    {
        int index = historyPage * 10 + row;
        if (loadedHistory?.items == null || index >= loadedHistory.items.Length) return;
        PlayerHistoryItem item = loadedHistory.items[index];
        detailTitle.text = loadedHistory.displayName + "\n" + item.date;
        FillStatRows(detailValues, item.result);
        Show(historyDetail);
    }

    private void Show(GameObject screen)
    {
        foreach (GameObject item in new[] { menu, levels, instructions, hud, pause, result, profileScreen, historyScreen, historyDetail }) item.SetActive(item == screen);
        Button first = screen.GetComponentInChildren<Button>();
        EventSystem.current.SetSelectedGameObject(first != null ? first.gameObject : null);
        // The menu has a large UI portrait; never leave a second small chef behind a card.
        if (chef != null)
            foreach (Renderer renderer in chef.GetComponentsInChildren<Renderer>())
                renderer.enabled = (screen == hud || screen == pause) && renderer.gameObject != chef.gameObject;
    }

    private void Begin(DifficultyLevel difficulty) { if (session.Player == null) { OpenProfile(); return; } session.StartSession(difficulty); Show(hud); }
    private void ReturnMenu() { session.MainMenu(); Show(menu); }

    private void Update()
    {
        if (root == null) return;
        if (shownState != session.State)
        {
            shownState = session.State;
            if (shownState == SessionState.Paused) { Show(pause); hud.SetActive(true); }
            else if (shownState == SessionState.Playing) Show(hud);
            else if (shownState == SessionState.Ready) Show(menu);
            else
            {
                Show(result);
                SessionResult r = session.Result;
                resultScore.text = r.score.ToString();
                resultPlayer.text = r.profile.displayName;
                FillStatRows(resultValues, r);
                nextSaveCheck = 0;
            }
        }
        if (result.activeSelf && Time.unscaledTime >= nextSaveCheck)
        {
            nextSaveCheck = Time.unscaledTime + 1;
#if UNITY_WEBGL && !UNITY_EDITOR
            int saved = BakeCatchFirebaseSaveState();
            saveStatus.text = saved == 2 ? "Saved to Firebase" : saved == 1 ? "Waiting to sync — keep the game open and connect to the internet." : "Save not confirmed — keep the game open.";
#else
            saveStatus.text = "Editor preview — this round is not uploaded to Firebase.";
#endif
        }
        if (levels.activeSelf && Keyboard.current != null)
        {
            if (Keyboard.current.digit1Key.wasPressedThisFrame) Begin(DifficultyLevel.Easy);
            else if (Keyboard.current.digit2Key.wasPressedThisFrame) Begin(DifficultyLevel.Medium);
            else if (Keyboard.current.digit3Key.wasPressedThisFrame) Begin(DifficultyLevel.Hard);
        }
        if (!hud.activeSelf) return;
        timeLabel.text = FormatTime(session.Remaining);
        scoreLabel.text = session.Statistics.Score.ToString();
        comboLabel.text = "x" + session.Statistics.CurrentCombo;
        levelLabel.text = session.Difficulty.ToString();
        timeFill.rectTransform.sizeDelta = new Vector2(138 * session.Remaining / session.Duration, 5);
        RecipeDefinition recipe = session.Recipe.Current;
        recipeLabel.text = recipe.Name;
        recipeIcon.sprite = BakeryArt.Icon(recipe.Name == "Strawberry Cake" ? 11 : recipe.Name == "Pizza" ? 12 : 13);
        for (int i = 0; i < recipe.Ingredients.Length; i++)
        {
            rowIcons[i].sprite = BakeryArt.Icon((int)recipe.Ingredients[i]);
            rowNames[i].text = recipe.Ingredients[i].ToString();
            rowCounts[i].text = $"{session.Recipe.Collected[i]}/{recipe.Quantities[i]}";
            bool complete = session.Recipe.Collected[i] >= recipe.Quantities[i];
            rowCounts[i].color = complete ? BakeryArt.Hex("#259A48") : ink;
            rowChecks[i].text = complete ? "✓" : "";
        }
        feedback.text = string.IsNullOrEmpty(session.Feedback) ? session.Statistics.ComboFeedback :
            session.Feedback + "  " + session.Statistics.ComboFeedback;
        Vector3 point = Camera.main.WorldToScreenPoint(chef.TransformPoint(new Vector3(0, 2.02f, 0)));
        RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)hud.transform, point, null, out Vector2 local);
        catchLabel.rectTransform.anchoredPosition = local + new Vector2(-100, 18);
    }

    private static string FormatTime(float seconds)
    {
        int whole = Mathf.CeilToInt(Mathf.Max(0, seconds));
        return $"{whole / 60:00}:{whole % 60:00}";
    }
}
