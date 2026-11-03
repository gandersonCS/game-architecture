using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Content;
using CIS580.Audio;
using GameArchitecture.Settings;
using GameArchitecture.Simulation;

namespace GameArchitecture.Audio;

/// <summary>
/// This game's sounds and music, and what plays them.
/// </summary>
/// <remarks>
/// <para>
/// The simulation never plays sounds itself (it doesn't even know sound exists). Instead it
/// raises events, and <see cref="OnGameEvent"/> decides what each one sounds like. To add a
/// sound for a new event, load a cue below and add a case for the event.
/// </para>
/// <para>
/// The player's settings (sound on or off, and the volumes) are applied to the audio buses
/// every frame, so changes in the options menu take effect immediately.
/// </para>
/// </remarks>
public sealed class GameAudio
{
    private readonly GameSettings _settings;
    private readonly Random _random = new();
    private int _lastRunTrack = -1;
    private readonly SoundCue _playerJoined;
    private readonly SoundCue _playerLeft;

    /// <param name="audio">The audio manager</param>
    /// <param name="content">Loads the sounds; they stay loaded while the game runs</param>
    /// <param name="settings">The player's settings</param>
    public GameAudio(AudioManager audio, ContentManager content, GameSettings settings)
    {
        Audio = audio ?? throw new ArgumentNullException(nameof(audio));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        MenuMusic = content.Load<SoundEffect>("Audio/Music/Title Menu");

        // each run picks one of these at random. intro plays once, then it loops
        RunMusic = Array.ConvertAll(RunTracks, track => LoadLoopingTrack(content, track));

        // Gameplay sounds go on the Effects bus, which is paused along with the game
        _playerJoined = new SoundCue(audio.Effects, content.Load<SoundEffect>("Audio/Sfx/player_join"))
        {
            PitchVariation = 0.05f,
        };
        _playerLeft = new SoundCue(audio.Effects, content.Load<SoundEffect>("Audio/Sfx/player_leave"));
    }

    /// <summary>
    /// The audio manager
    /// </summary>
    public AudioManager Audio { get; }

    /// <summary>
    /// The music for the menus
    /// </summary>
    public SoundEffect MenuMusic { get; }

    /// <summary>
    /// The songs that can play during a run (random pick each run)
    /// </summary>
    public LoopingTrack[] RunMusic { get; }

    /// <summary>
    /// A song with an intro, plus its loop points (seconds from the start of the file)
    /// </summary>
    /// <param name="File">Name of the .wav in Assets/Audio/Loops, no extension</param>
    /// <param name="LoopStart">Loop start time. Anything before this is the intro and only plays once</param>
    /// <param name="LoopEnd">Where it jumps back to LoopStart (null = end of the file)</param>
    private readonly record struct LoopInfo(string File, double LoopStart, double? LoopEnd = null);

    // loop points for the run music. I worked these out from each song's tempo and sections so
    // the loop is a whole number of bars and the jump back lands on a downbeat. if one sounds off,
    // nudge the numbers but keep the loop length a whole number of bars
    private static readonly LoopInfo[] RunTracks =
    {
        // 90 BPM, 4s intro, 36 bar loop that stops before the fade-out at the end
        new("Action A", LoopStart: 4.385, LoopEnd: 100.385),
        // 128 BPM, 2s quiet intro, rest of the song loops (53 bars)
        new("Action B", LoopStart: 2.340),
        // 120 BPM, 1s riser, rest of the song loops (34 bars)
        new("Tutorial", LoopStart: 1.0096),
    };

    private static LoopingTrack LoadLoopingTrack(ContentManager content, LoopInfo info)
    {
        // these .wavs get copied as-is by the content builder instead of built, so we read them straight from disk
        string path = Path.Combine(content.RootDirectory, "Audio", "Loops", info.File + ".wav");
        using var stream = TitleContainer.OpenStream(path);
        return LoopingTrack.FromWav(stream, TimeSpan.FromSeconds(info.LoopStart),
            info.LoopEnd is double end ? TimeSpan.FromSeconds(end) : null);
    }

    /// <summary>
    /// Cross-fades to the menu music (if it isn't already playing)
    /// </summary>
    public void PlayMenuMusic() => Audio.MusicPlayer.Play(MenuMusic);

    /// <summary>
    /// Cross-fades into a random run song. Never picks the same one as last run so it doesn't get
    /// repetitive. Plays the intro first, then loops without the intro.
    /// </summary>
    public void PlayGameMusic()
    {
        int next;
        if (_lastRunTrack < 0)
            next = _random.Next(RunMusic.Length);
        else
        {
            next = _random.Next(RunMusic.Length - 1);
            if (next >= _lastRunTrack)
                next++;
        }

        _lastRunTrack = next;
        Audio.MusicPlayer.Play(RunMusic[next]);
    }

    /// <summary>
    /// Plays the sound (if any) for something that happened in the game
    /// </summary>
    /// <param name="gameEvent">What happened</param>
    /// <param name="world">The world it happened in, e.g. to find where a sound comes from</param>
    public void OnGameEvent(GameEvent gameEvent, World world)
    {
        switch (gameEvent)
        {
            case PlayerJoined joined:
                var avatar = world.FindPlayer(joined.Player);
                _playerJoined.Play(pan: avatar != null ? PanFor(avatar.Position) : 0f);
                break;
            case PlayerLeft:
                _playerLeft.Play();
                break;
        }
    }

    /// <summary>
    /// Applies the settings to the audio buses, and updates the audio. Call once per frame.
    /// </summary>
    public void Update(GameTime gameTime)
    {
        Audio.Master.Muted = !_settings.SoundEnabled;
        Audio.Music.Volume = _settings.MusicVolume / (float)GameSettings.MaxVolume;
        Audio.Effects.Volume = _settings.EffectsVolume / (float)GameSettings.MaxVolume;
        Audio.Ui.Volume = Audio.Effects.Volume;

        Audio.Update(gameTime.ElapsedGameTime);
    }

    /// <summary>
    /// Pans a sound toward the side of the world it comes from (gently: hard left or right
    /// sounds odd on headphones)
    /// </summary>
    private static float PanFor(Vector2 position)
        => 0.6f * Math.Clamp(position.X / World.Width * 2f - 1f, -1f, 1f);
}
