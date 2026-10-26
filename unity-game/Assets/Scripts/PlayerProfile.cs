using System;
using System.Text.RegularExpressions;

[Serializable]
public sealed class PlayerProfile
{
    public string code, displayName;
    public PlayerProfile(string code, string displayName)
    {
        this.code = (code ?? "").Trim().ToUpperInvariant();
        this.displayName = (displayName ?? "").Trim();
        if (!Regex.IsMatch(this.code, "^[A-Z0-9][A-Z0-9_-]{0,23}$") || this.displayName.Length == 0 ||
            this.displayName.Length > 40 || Regex.IsMatch(this.displayName, "[<>\\x00-\\x1f\\x7f]"))
            throw new ArgumentException("Enter a name (1–40 characters) and a code using letters, numbers, _ or -.");
    }
    public PlayerProfile Copy() => new PlayerProfile(code, displayName);
}
