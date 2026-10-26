using System;

// Read-only Firebase presentation data, separate from the live session and its scoring.
[Serializable]
public sealed class PlayerHistory
{
    public int requestId, totalPlayCount, bestScore;
    public string code, displayName, error;
    public PlayerHistoryItem[] items;
}

[Serializable]
public sealed class PlayerHistoryItem
{
    public string id, date;
    public SessionResult result;
}

[Serializable]
public sealed class PlayerHistoryRequest
{
    public int requestId;
    public PlayerProfile profile;
}
