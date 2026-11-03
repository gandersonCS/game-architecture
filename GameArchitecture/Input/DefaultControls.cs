using Microsoft.Xna.Framework.Input;
using CIS580.Input;
using GameArchitecture.Simulation;

namespace GameArchitecture.Input;

/// <summary>
/// This game's default controls: the library's standard schemes (movement, menus, pause),
/// plus bindings for this game's own actions.
/// </summary>
/// <remarks>
/// Game actions are bound using the names of the <see cref="GameAction"/> values. When you add
/// an action to GameAction, add default bindings for it to each scheme here. These are only
/// the defaults: the controls the game actually uses (which players can rebind) are in
/// <see cref="GameControls"/>.
/// </remarks>
public static class DefaultControls
{
    private static KeyBinding Key(Keys key) => new(key);
    private static GamePadButtonBinding Button(Buttons button) => new(button);

    /// <summary>
    /// The whole keyboard and mouse, for a player who isn't sharing them
    /// </summary>
    public static ControlScheme Keyboard() => StandardSchemes.Keyboard()
        .Bind(nameof(GameAction.Jump), Key(Keys.Space), new MouseButtonBinding(MouseButton.Left));

    /// <summary>
    /// The left side of a shared keyboard
    /// </summary>
    public static ControlScheme KeyboardLeft() => StandardSchemes.KeyboardLeft()
        .Bind(nameof(GameAction.Jump), Key(Keys.Space));

    /// <summary>
    /// The right side of a shared keyboard
    /// </summary>
    public static ControlScheme KeyboardRight() => StandardSchemes.KeyboardRight()
        .Bind(nameof(GameAction.Jump), Key(Keys.Enter));

    /// <summary>
    /// A gamepad
    /// </summary>
    public static ControlScheme GamePad() => StandardSchemes.GamePad()
        .Bind(nameof(GameAction.Jump), Button(Buttons.A), new GamePadAxisBinding(GamePadAxis.RightTrigger));
}
