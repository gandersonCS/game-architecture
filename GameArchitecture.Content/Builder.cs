/// <summary>
/// Entry point for the Content Builder, which builds the files in Assets/ into .xnb content
/// according to the rules in the Builder class below.
/// </summary>
/// <remarks>
/// The game project runs this automatically (see CIS580/Build/CIS580.Content.targets), passing
/// the platform and directories on the command line. Running it with no arguments uses the
/// defaults below, which is handy for debugging the builder itself.
/// Content is loaded in the game by its path relative to Assets/, without an extension:
/// Assets/Fonts/menufont.spritefont is loaded with Content.Load&lt;SpriteFont&gt;("Fonts/menufont").
/// </remarks>

using Microsoft.Xna.Framework.Content.Pipeline;
using Microsoft.Xna.Framework.Content.Pipeline.Processors;
using MonoGame.Framework.Content.Pipeline.Builder;

var defaultArgs = new ContentBuilderParams()
{
    Mode = ContentBuilderMode.Builder,
    WorkingDirectory = $"{AppContext.BaseDirectory}../../",
    SourceDirectory = "Assets",
    Platform = TargetPlatform.DesktopGL
};

var builder = new Builder();

if (args is not null && args.Length > 0)
    builder.Run(args);
else
    builder.Run(defaultArgs);

return builder.FailedToBuild > 0 ? -1 : 0;

/// <summary>
/// Defines which assets are built, and how.
/// </summary>
public class Builder : ContentBuilder
{
    public override IContentCollection GetContentCollection()
    {
        var content = new ContentCollection();

        // Each folder is built with the default importer and processor for its file type.
        // A specific asset can instead be given its own importer, processor and parameters, e.g.:
        //
        //     content.Include("Textures/sprites.png", new TextureImporter(),
        //         new TextureProcessor() { ColorKeyEnabled = false });
        //
        // and files can be copied as-is (e.g. level data) rather than built:
        //
        //     content.IncludeCopy<WildcardRule>("Levels/*.json");

        content.Include<WildcardRule>("Fonts/*.spritefont");
        content.Include<WildcardRule>("Textures/*.png");

        // Both sound effects and music are built as SoundEffects (.wav files import that way by
        // default), so music tracks can be cross-faded by the CIS580.Audio music player.
        content.Include<WildcardRule>("Audio/Sfx/*.wav");
        content.Include<WildcardRule>("Audio/Music/*.wav");

        // The game's .mp3 tracks. An .mp3 would normally build as a Song, so each is built as a
        // SoundEffect instead, like the .wav music. Only the tracks the game uses are listed:
        // every track is decoded into memory, so building all of them would waste build time
        // and space. Add a line here when a new track is needed.
        string[] tracks =
        {
            "Title Menu",                           // Main menu
        };
        foreach (var track in tracks)
            content.Include($"Audio/Music/{track}.mp3", new Mp3Importer(), new SoundEffectProcessor());

        // Music with an intro that plays once before looping (see LoopingTrack). These are 16-bit
        // .wav files copied as-is, because the game reads their samples itself to loop them
        // seamlessly. The loop points are in GameAudio.
        content.IncludeCopy<WildcardRule>("Audio/Loops/*.wav");

        return content;
    }
}
