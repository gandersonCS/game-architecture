namespace GameArchitecture.Simulation;

/// <summary>
/// The actions a player can take in this game, in addition to moving and looking.
/// </summary>
/// <remarks>
/// Each value is a bit index in a <see cref="CIS580.Simulation.ActionSet"/>, so values must be
/// between 0 and 31.
/// Menu actions such as pause are not game actions; they are handled outside the simulation.
/// </remarks>
public enum GameAction
{
    /// <summary>
    /// The one button: jump (hold for a higher jump), keep holding in the air to swing, and
    /// press again after a run ends to restart.
    /// </summary>
    Jump = 0,
}
