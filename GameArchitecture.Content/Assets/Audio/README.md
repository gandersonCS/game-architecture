# Audio

Sources and licenses for everything in here are in the top-level `ASSETS.md`.

| File | Use |
|---|---|
| `Music/Title Menu.mp3` | Main menu music (Spider-Man Unlimited) |
| `Loops/Action A.wav` | Run music, intro + loop (Spider-Man Unlimited) |
| `Loops/Action B.wav` | Run music, intro + loop (Spider-Man Unlimited) |
| `Loops/Tutorial.wav` | Run music, intro + loop (Spider-Man Unlimited) |
| `Sfx/ui_move.wav` | Menu selection moved (starter placeholder) |
| `Sfx/ui_confirm.wav` | Menu item confirmed (starter placeholder) |
| `Sfx/ui_back.wav` | Menu back/cancel (starter placeholder) |
| `Sfx/player_join.wav` | Player joined (starter placeholder) |
| `Sfx/player_leave.wav` | Player left (starter placeholder) |
| `Music/menu_loop.wav`, `Music/game_loop.wav` | Old starter placeholder loops, not played anymore |

The other mp3s in `Music/` are from the same Spider-Man Unlimited rip and are saved for later
(boss fights etc.).

How it gets built (see `Builder.cs`):
- `Sfx/*.wav` and `Music/*.wav` build as `SoundEffect`.
- mp3s only get built if they're listed in `Builder.cs`, and they're built as `SoundEffect` instead
  of `Song` so the music player can cross-fade them.
- `Loops/*.wav` are copied as-is and read directly by `GameAudio` so it can loop past the intro.

Load content by its path relative to `Assets/` with no extension, e.g.
`Content.Load<SoundEffect>("Audio/Sfx/ui_move")`.
