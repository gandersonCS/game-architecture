using System;
using CIS580.Settings;

namespace GameArchitecture.Settings;

/// <summary>
/// The player's progress: what is kept between runs (as opposed to <see cref="GameSettings"/>,
/// which is how they like the game set up). Saved as save.json, next to the settings.
/// </summary>
/// <remarks>
/// Add a property here to save something new (it's saved automatically). Give it a sensible
/// default, so older save files without it still load.
/// </remarks>
public sealed class SaveData
{
    /// <summary>The furthest run, in whole meters</summary>
    public int HighScoreMeters { get; set; }

    /// <summary>The most recent run, in whole meters</summary>
    public int LastScoreMeters { get; set; }

    /// <summary>How many runs have been finished</summary>
    public int RunsPlayed { get; set; }

    /// <summary>The Spider-Essence the player has to spend in the Closet</summary>
    public int SpiderEssence { get; set; }
}

/// <summary>
/// What happened when a run was recorded, for the end-of-run screen
/// </summary>
/// <param name="Meters">How far the run went</param>
/// <param name="EssenceCollected">The Spider-Essence collected on the run</param>
/// <param name="TotalEssence">The player's Spider-Essence now, including this run's</param>
/// <param name="NewHighScore">Whether the run beat the previous best</param>
/// <param name="HighScoreMeters">The best run now (this one, if it was a new best)</param>
/// <param name="Saved">Whether the save file was written (if not, see the store's LastError)</param>
public readonly record struct RunRecord(
    int Meters, int EssenceCollected, int TotalEssence, bool NewHighScore, int HighScoreMeters, bool Saved);

/// <summary>
/// Loads the player's progress when the game starts, and saves it automatically whenever a
/// run ends (or something is bought). A single instance is shared through the game's services.
/// </summary>
/// <remarks>
/// It uses the same <see cref="SettingsStore"/> as the settings, so a missing or damaged save
/// file starts fresh (the damaged one is kept as save.json.bad) rather than stopping the game.
/// </remarks>
public sealed class SaveFile
{
    public const string FileName = "save.json";

    public SaveFile(SettingsStore store)
    {
        Store = store ?? throw new ArgumentNullException(nameof(store));
        Data = store.Load(FileName, () => new SaveData());
    }

    /// <summary>Where the save file is kept</summary>
    public SettingsStore Store { get; }

    /// <summary>The player's progress</summary>
    public SaveData Data { get; }

    /// <summary>Whether the player has finished a run yet</summary>
    public bool HasPlayed => Data.RunsPlayed > 0;

    /// <summary>
    /// Records a finished run (its distance becomes the last score, and the high score if it's
    /// the best yet, and its Spider-Essence is banked), then saves.
    /// </summary>
    public RunRecord RecordRun(double meters, int essence)
    {
        // Rounded the same way the HUD shows it, so the saved score matches what the player saw
        int score = (int)Math.Round(Math.Max(0, meters));
        bool newBest = score > Data.HighScoreMeters;

        Data.LastScoreMeters = score;
        if (newBest)
            Data.HighScoreMeters = score;
        Data.RunsPlayed++;
        Data.SpiderEssence = AddClamped(Data.SpiderEssence, Math.Max(0, essence));

        bool saved = Save();
        return new RunRecord(score, essence, Data.SpiderEssence, newBest, Data.HighScoreMeters, saved);
    }

    /// <summary>
    /// Spends Spider-Essence (e.g. on a costume in the Closet), if the player has enough, and saves
    /// </summary>
    /// <returns>Whether they had enough (if not, nothing is spent)</returns>
    public bool TrySpend(int amount)
    {
        if (amount < 0 || amount > Data.SpiderEssence)
            return false;
        Data.SpiderEssence -= amount;
        Save();
        return true;
    }

    /// <summary>
    /// Saves the progress
    /// </summary>
    /// <returns>Whether it was saved (if not, see the store's LastError)</returns>
    public bool Save() => Store.Save(FileName, Data);

    private static int AddClamped(int a, int b) => (int)Math.Min(int.MaxValue, (long)a + b);
}
