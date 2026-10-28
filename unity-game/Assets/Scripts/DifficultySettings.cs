public enum DifficultyLevel { Easy, Medium, Hard }

public sealed class DifficultySettings
{
    public float FallSpeed { get; }
    public float SpawnInterval { get; }
    public int MaxObjects { get; }
    public float HazardChance { get; }
    public bool Predictable { get; }

    private DifficultySettings(float speed, float interval, int maxObjects, float hazards, bool predictable)
    {
        FallSpeed = speed;
        SpawnInterval = interval;
        MaxObjects = maxObjects;
        HazardChance = hazards;
        Predictable = predictable;
    }

    private static readonly DifficultySettings[] Modes =
    {
        new DifficultySettings(2.2f, 1.5f, 1, 0f, true),
        new DifficultySettings(3.2f, 1.6f, 2, 0.12f, false),
        new DifficultySettings(4.8f, 0.75f, 4, 0.22f, false)
    };

    public static DifficultySettings Get(DifficultyLevel level) => Modes[(int)level];
}
