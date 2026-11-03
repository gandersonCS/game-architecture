# Assets & Credits

Everything in this project that I didn't make myself, where it came from, and what license it's under.
Content files live in `GameArchitecture.Content/Assets/`.

This is a fan project made for a university class (CIS 580, Kansas State University). Spider-Man and
all related characters belong to Marvel. It's not for sale and isn't meant to be published.

## Starter code and tools

- **CIS 580 Game Architecture starter project** - the base this game is built on (the `CIS580/`
  framework, app flow, menus, input, audio, settings, tests, and the docs in `Docs/`) By professor Nathan Bean.
  K-State CIS 580 course staff. <https://github.com/ksu-cis-580/game-architecture>
- **MonoGame 3.8** (DesktopGL) - game framework. <https://monogame.net>, Microsoft Public License (Ms-PL).
- **.NET 9** - Microsoft. <https://dotnet.microsoft.com>
- **AI assistance** - Claude (Anthropic) was used to help write and edit parts of the code and
  comments. <https://claude.ai>

## Art

- `Textures/tileset_city_free.png` - building tileset (16x16 tiles), and
  `Textures/background_tileset_city_free.png` - the city skyline used for parallax.
  From the **Free Retro City asset pack** by MariaParraGames ("FREE TILESET by MP" on the image).
  <https://mariaparragames.itch.io/free-retro-city-assetpack>
  License (from the itch.io page): free for personal and commercial use, can be edited,
  can't be redistributed or resold. Credit is optional, but here it is anyway.

- `Textures/spiderman.png` - Spider-Man sprites from the Game Boy Advance game
  *Spider-Man: The Movie* (Activision, 2002). Sheet ripped by ZiggytheNinja (credit is printed on
  the sheet), downloaded from The Spriters Resource:
  <https://www.spriters-resource.com/game_boy_advance/spidermanthemovie/asset/58021/>
  I removed the green background and the web lines that were baked into the sheet.
  The sprites are Marvel/Activision property and are only used here for a class project - ABANDONWARE
- `Textures/Game Boy Advance - Spider-Man_ The Movie - Playable Characters - Spider-Man.png` -
  the original unedited sheet from the same link. Not loaded by the game.

- `Textures/background.png` (submenu background) and `Textures/gradient.png` (message box fill) -
  procedurally generated placeholders that came with the starter project, public domain.

- `Textures/dawn1-7.png`, `Textures/night1-5.png` - parallax city layers (sky first, then
  buildings far to near), used by `Views/CityBackdrop.cs` for the dusk and night skylines.
  `dawn7.png` is a flattened preview of the whole dusk scene and isn't loaded.
  Source: TODO.
- `Textures/day1-6.png` - the daytime skyline. Recolored copies of `dawn1-6.png` (each layer's
  brightness remapped onto a blue day palette), so same source/license as the dawn layers.

- Spider-Essence orbs - drawn in code (`Views/EssenceArt.cs`), no image file.

## Music

Main menu and in-run music is from **Spider-Man Unlimited** (Gameloft, 2014, Android/iOS/Windows
Phone), music composed by Pascal Dion. The tracks are from the game rip on KHInsider:
<https://downloads.khinsider.com/game-soundtracks/album/spider-man-unlimited-gamerip-2014-android-ios-windows>
Gameloft/Marvel own the music; it's only used here for a class project. - ABANDONWARE(game was shut down and made innaccesible)

| File | Track | Used for |
|---|---|---|
| `Audio/Music/Title Menu.mp3` | "Title Menu" | Main menu |
| `Audio/Loops/Action A.wav` | "Action A" | Run music (random pick) |
| `Audio/Loops/Action B.wav` | "Action B" | Run music (random pick) |
| `Audio/Loops/Tutorial.wav` | "Tutorial" | Run music (random pick) |

The `Loops/` files are the same songs converted to 16-bit .wav so the game can loop them past the
intro (loop points are in `Audio/GameAudio.cs`).

The other .mp3 files in `Audio/Music/` (Boss A, Mechtopus, Subway, Mission Complete, etc.) are from
the same rip. They aren't in the game yet, they're there for boss fights and other screens later.

`Audio/Music/menu_loop.wav` and `Audio/Music/game_loop.wav` are the starter project's placeholder
loops (procedurally generated, public domain). They still get built but the game doesn't play them anymore.

## Sound effects

- `Audio/Sfx/ui_move.wav`, `ui_confirm.wav`, `ui_back.wav`, `player_join.wav`, `player_leave.wav` -
  placeholder sounds from the starter project, procedurally generated (synthesized, no samples),
  public domain.

## Fonts

- `Fonts/LiberationSans-Regular.ttf` - Liberation Sans 2.1.5 from the Liberation Fonts project,
  <https://github.com/liberationfonts/liberation-fonts>, SIL Open Font License 1.1
  (see `Fonts/LiberationSans-LICENSE.txt`). Used by `menufont.spritefont` and `gamefont.spritefont`.
  Came with the starter project.
- `Fonts/Action Comics.ttf`, `Action Comics Black.ttf`, `KOMIKAX_.ttf`, `Super Squad.ttf`,
  `The Amazing Spider-Man.ttf` - comic-style fonts I downloaded for the title/HUD. None of them
  are used by the game yet. Source and license: https://www.dafont.com/theme.php?cat=102 Free for Personal Use

## Application icon

- `GameArchitecture/Icon.ico` and `GameArchitecture/Icon.bmp` - the default icons from the MonoGame
  project templates (<https://monogame.net>), Microsoft Public License (Ms-PL).
