using System;
using CIS580.Settings;

namespace GameArchitecture.Settings;

/// <summary>
/// Player progress that carries over between runs (different from <see cref="GameSettings"/>,
/// which is just preferences). Gets saved to save.json next to the settings file.
/// </summary>
/// <remarks>
/// To save something new just add a property here, it gets serialized automatically. Give it a
/// default value so old save files still load.
/// </remarks>
public sealed class SaveData
{
    /// <summary>Best distance, in meters</summary>
    public int HighScoreMeters { get; set; }

    /// <summary>Last run's distance, in meters</summary>
    public int LastScoreMeters { get; set; }

    /// <summary>Total runs finished</summary>
    public int RunsPlayed { get; set; }

    /// <summary>Spider-Essence the player can spend in the Closet</summary>
    public int SpiderEssence { get; set; }
}

/// <summary>
/// Result of recording a run, used by the end-of-run screen
/// </summary>
/// <param name="Meters">Distance of the run</param>
/// <param name="EssenceCollected">Essence picked up this run</param>
/// <param name="TotalEssence">Player's total essence after adding this run</param>
/// <param name="NewHighScore">True if this beat the old best</param>
/// <param name="HighScoreMeters">Current best (this run if it was a new record)</param>
/// <param name="Saved">Whether saving worked (check the store's LastError if not)</param>
public readonly record struct RunRecord(
    int Meters, int EssenceCollected, int TotalEssence, bool NewHighScore, int HighScoreMeters, bool Saved);

/// <summary>
/// Loads progress on startup and saves whenever a run ends or something gets bought. One shared
/// instance lives in the game's services.
/// </summary>
/// <remarks>
/// Uses the same <see cref="SettingsStore"/> as the settings, so if the save file is missing or
/// corrupted we just start fresh (the bad file gets kept as save.json.bad) instead of crashing.
/// </remarks>
public sealed class SaveFile
{
    public const string FileName = "save.json";

    public SaveFile(SettingsStore store)
    {
        Store = store ?? throw new ArgumentNullException(nameof(store));
        Data = store.Load(FileName, () => new SaveData());
    }

    /// <summary>Path to the save file</summary>
    public SettingsStore Store { get; }

    /// <summary>Current progress</summary>
    public SaveData Data { get; }

    /// <summary>True once the player has finished at least one run</summary>
    public bool HasPlayed => Data.RunsPlayed > 0;

    /// <summary>
    /// Records a finished run - updates last score, high score if it's a record, adds the essence -
    /// and then saves.
    /// </summary>
    /// <param name="meters">Distance reached</param>
    /// <param name="essence">Essence to add to the total. After a continue this is just what was
    /// collected since the last time the run was recorded, so nothing is counted twice</param>
    /// <param name="countsAsRun">False when this is the same run being recorded again (it died,
    /// continued, and died again), so it doesn't count as another run played</param>
    public RunRecord RecordRun(double meters, int essence, bool countsAsRun = true)
    {
        // round it the same way the HUD does so the saved score matches what the player saw
        int score = (int)Math.Round(Math.Max(0, meters));
        bool newBest = score > Data.HighScoreMeters;

        Data.LastScoreMeters = score;
        if (newBest)
            Data.HighScoreMeters = score;
        if (countsAsRun)
            Data.RunsPlayed++;
        Data.SpiderEssence = AddClamped(Data.SpiderEssence, Math.Max(0, essence));

        bool saved = Save();
        return new RunRecord(score, essence, Data.SpiderEssence, newBest, Data.HighScoreMeters, saved);
    }

    /// <summary>
    /// Spends essence (like buying a suit in the Closet) if the player has enough, then saves
    /// </summary>
    /// <returns>False if they couldn't afford it (nothing gets spent)</returns>
    public bool TrySpend(int amount)
    {
        if (amount < 0 || amount > Data.SpiderEssence)
            return false;
        Data.SpiderEssence -= amount;
        Save();
        return true;
    }

    /// <summary>
    /// Saves progress to disk
    /// </summary>
    /// <returns>Whether it saved (check the store's LastError if not)</returns>
    public bool Save() => Store.Save(FileName, Data);

    private static int AddClamped(int a, int b) => (int)Math.Min(int.MaxValue, (long)a + b);
}
