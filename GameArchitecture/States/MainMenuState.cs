using CIS580.Flow;
using CIS580.Input;
using GameArchitecture.Audio;
using GameArchitecture.Input;
using GameArchitecture.UI;

namespace GameArchitecture.States;

/// <summary>
/// The main menu, where the game starts. Any player, on any device, may use it.
/// </summary>
public sealed class MainMenuState : AppState
{
    protected override void Enter()
    {
        ListenTo(Controls.AllDevices());
        Game.Services.GetService<GameAudio>().PlayMenuMusic();

        // Shows behind the submenus (Options, The Closet); the main menu draws its own scene
        Show(new BackgroundLayer());
        Show(new MainMenuLayer(
            onPlay: StartGame,
            onExit: () => Game.Exit()));
    }

    /// <summary>
    /// Starts a single player run for whoever chose "Run", using the device they chose it with
    /// </summary>
    private void StartGame(InputSource chosenWith)
    {
        var game = new InGameState(new[] { Controls.SinglePlayerFor(chosenWith) });
        Flow.Change(new LoadingState(game));
    }

    private GameControls Controls => Game.Services.GetService<GameControls>();
}
