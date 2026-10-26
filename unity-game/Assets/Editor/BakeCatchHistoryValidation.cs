using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Isolated UI fixtures only: no fake sessions or sensor measurements are sent to Firebase.
public static class BakeCatchHistoryValidation
{
    [MenuItem("Bake Catch/Validate History UI (Play Mode)")]
    public static void Run()
    {
        if (!EditorApplication.isPlaying) throw new Exception("Enter Play Mode first.");
        BakeCatchUI ui = UnityEngine.Object.FindFirstObjectByType<BakeCatchUI>();
        GameSession session = UnityEngine.Object.FindFirstObjectByType<GameSession>();
        session.MainMenu();
        session.SelectPlayer(new PlayerProfile("HISTORY_EDITOR", "History UI validation"));
        typeof(BakeCatchUI).GetMethod("ReturnMenu", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(ui, null);
        Transform layout = ui.transform.Find("Layout");
        layout.Find("Main Menu/My History").GetComponent<Button>().onClick.Invoke();
        Check(layout.Find("My History").gameObject.activeSelf, "Menu opens in-game history");
        int request = (int)typeof(BakeCatchUI).GetField("historyRequest", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(ui);
        var data = new PlayerHistory { requestId = request, code = session.Player.code, displayName = session.Player.displayName,
            totalPlayCount = 12, bestScore = 120, items = new PlayerHistoryItem[12] };
        for (int i = 0; i < 12; i++) data.items[i] = new PlayerHistoryItem { id = "editor-" + i, date = "Editor fixture",
            result = new SessionResult(new SessionStatistics(), 45, DifficultyLevel.Easy) };
        ui.ReceivePlayerHistory(JsonUtility.ToJson(data));
        Check(CountDetails(layout.Find("My History")) == 10, "First page has ten rounds");
        layout.Find("My History/Details 1").GetComponent<Button>().onClick.Invoke();
        Check(layout.Find("History Details").gameObject.activeSelf, "Round details open");
        bool noData = false;
        foreach (Text text in layout.Find("History Details").GetComponentsInChildren<Text>()) if (text.text == "No data") noData = true;
        Check(noData, "No sensor samples show no data, not fabricated zero");
        layout.Find("History Details/Back to History").GetComponent<Button>().onClick.Invoke();
        layout.Find("My History/Older").GetComponent<Button>().onClick.Invoke();
        Check(CountDetails(layout.Find("My History")) == 2, "Older page has remaining rounds");
        layout.Find("My History/Newer").GetComponent<Button>().onClick.Invoke();
        Check(CountDetails(layout.Find("My History")) == 10, "Newer returns to first page");
        layout.Find("My History/Refresh").GetComponent<Button>().onClick.Invoke();
        ui.ReceivePlayerHistory(JsonUtility.ToJson(data));
        Check(CountDetails(layout.Find("My History")) == 0, "Stale response cannot repopulate a refreshed view");
        data.requestId = (int)typeof(BakeCatchUI).GetField("historyRequest", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(ui);
        data.code = "OTHER";
        ui.ReceivePlayerHistory(JsonUtility.ToJson(data));
        Check(CountDetails(layout.Find("My History")) == 0, "Other profile response is ignored");
        data.code = session.Player.code; data.items = Array.Empty<PlayerHistoryItem>(); data.totalPlayCount = 0;
        ui.ReceivePlayerHistory(JsonUtility.ToJson(data));
        Check(CountDetails(layout.Find("My History")) == 0, "Empty profile has no fake rounds");
        layout.Find("My History/Back").GetComponent<Button>().onClick.Invoke();
        Check(layout.Find("Main Menu").gameObject.activeSelf, "History returns to menu");
        Debug.Log("HISTORY_UI_VALIDATION_PASS: ten-row paging, details, no-data, stale/profile isolation, empty state, navigation.");
    }
    static int CountDetails(Transform screen)
    {
        int count = 0;
        foreach (Button button in screen.GetComponentsInChildren<Button>()) if (button.name.StartsWith("Details ")) count++;
        return count;
    }
    static void Check(bool condition, string message) { if (!condition) throw new Exception("HISTORY_UI_FAIL: " + message); }
}
