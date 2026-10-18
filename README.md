# Poker Table Manager

English · [Русский](README.ru.md)

A small Windows tray app that tiles poker table windows into fixed slots
across all your monitors. The standard build only moves and resizes windows:
it never clicks anything in the poker client, never reads the tables and
never takes focus. A separate build adds hotkeys at the tables (fold,
check/call, bet).

> [!WARNING]
> **Check every poker room's rules before you play with it.** Each room
> decides for itself which third-party software is allowed at its tables.
> Window managers are usually tolerated, but a program that presses buttons
> in the client (the hotkeys build) may count as automation in some rooms.
> Ask each room's support whether you may use it before playing (some rooms
> review a program on request), and keep their answer in writing. This
> project is not affiliated with, endorsed or approved by any poker room. Use
> it at your own risk: the authors are not responsible for closed accounts or
> confiscated funds.

What it does **not** do, in either build: it does not make decisions, does
not read cards, stacks or the pot, and does not act on its own. In the
hotkeys build every keypress by the player results in exactly one press of a
button the player picked.

The user interface (tray menu, notifications, log), the code comments and
`config.json` labels are in **Russian**. This README covers what you need to
build and run it.

## Requirements

- Windows 10 or 11.
- .NET Framework 4.8 — already part of Windows 10/11. Nothing to install:
  the build uses the C# compiler that ships with it.

## Build

```powershell
.\build.ps1          # standard build -> bin\TableManager.exe
.\build.ps1 -Keys    # hotkeys build  -> bin-keys\TableManagerKeys.exe
```

Each build also produces `LayoutTests.exe` (unit tests, exit code 0 when all
pass) and `FakeTable.exe` (test "tables" with Fold/Call/Bet buttons, for
trying things out without a real poker client).

Source layout: `src\` — the app (`ActionKeys.cs` and the `#if ACTION_KEYS`
blocks exist only in the hotkeys build), `tests\` — unit tests, `tools\` —
`FakeTable`.

## Table layout (both builds)

Run the exe; an icon appears in the tray. To start it with Windows, put a
shortcut into `shell:startup`.

- **Fixed slots.** Each monitor zone is split into a grid. A new table takes
  the first free slot; tables already placed never move. When a table
  closes, its slot is freed.
- **Grid by table count (levels).** By default: up to 14 tables — 3×2 on the
  primary monitor, then 4×2 on the second one; from the 15th table the
  primary monitor switches to 4×3. It switches back only when the table count
  drops with a margin (`Hysteresis`), so the grid doesn't flicker.
- **One table per slot.** If there aren't enough slots, the extra table stays
  where it is instead of covering another one.
- **Real pixels on every monitor.** Per-monitor DPI scaling (e.g. a 4K
  monitor at 150% next to an ultrawide at 100%) and the invisible window
  borders of Windows 10/11 are handled, so tables line up exactly.
- **Tables shrink to fit** and keep their aspect ratio (every client has its
  own), centered in the slot.
- **Dragging:** drop a table onto another slot and it moves there (swapping
  places with the table in that slot). A table the client moved away on its
  own is put back.
- Nothing moves while the left mouse button is held. Minimized tables are
  left alone.

| | |
|---|---|
| `Ctrl+Alt+L` | re-arrange all tables |
| `Ctrl+Alt+K` or double-click the icon | show the slots for 3 seconds |
| `Ctrl+Alt+T` (cursor over a window) | "this is a table" — and windows like it |
| `Ctrl+Alt+N` (cursor over a window) | "this is not a table" |
| tray menu | pause, window list (to add a room), log, reload settings |

### Rooms and "this is a table"

Rules for these clients are built in: GG / PokerOK, Winamax, AKs Poker,
CoinPoker, Redstar, TigerGaming / Pokerdom (WPN), PokerPlanets, QQPK. For any
other client, hover over a table and press `Ctrl+Alt+T`: the app writes a rule
into `config.json` on its own (by window class, by game markers in the title,
"all windows except the lobby", etc.). If a rule picks up a wrong window
(cashier, tournament lobby), hover over it and press `Ctrl+Alt+N`.

If a client runs as administrator (e.g. PokerKing), Windows does not let a
regular program move its windows — run the table manager as administrator
too.

### Settings — `config.json` next to the exe

Created on first start; after editing use tray menu → reload settings.

- `Levels` / `Zones` — where and in which grid tables go. A zone is a part
  of a monitor's work area: `Monitor` (`primary`, `secondary` or a device
  name like `\\.\DISPLAY2`), `X, Y, W, H` as fractions (0…1), `Columns`,
  `Rows`, `Order` (`rows`, `rows-rtl`, `columns`, `columns-rtl`). A level
  applies while there are no more than `MaxTables` tables (0 — as many as it
  has slots).
- `Rooms` — how to recognize a table: `Process` (process name), `Class`
  (window class), `Table` (part of the table title), `Exclude` (lobby and
  other windows) — regular expressions, case-insensitive. `Lobby: "largest"`
  — the largest window of the room is its lobby.
- `Gap` (px between tables), `Align` (`center` / `topleft`),
  `RespectManualMoves` (`true` — a table moved by hand stays where it was
  dropped), hotkeys `RelayoutHotkey`, `ShowSlotsHotkey`, `PickTableHotkey`,
  `NotTableHotkey` (`""` — no hotkey).

## Hotkeys at the tables (separate build)

`bin-keys\TableManagerKeys.exe` is the same table manager plus hotkeys. The
standard `bin\TableManager.exe` doesn't have them. Don't run both at once:
the hotkeys build refuses to start while the standard one is running.
Turn off the hotkeys of other tools (e.g. Jurojin) while using it, otherwise
a key fires twice.

- **Keys:** `Space` — fold, `S` — check/call, `D` — bet (presses the client's
  Bet/Raise button; you set the amount yourself). A key works only while the
  cursor is over a table and a poker client window is active; anywhere else
  it types as usual.
- **Focus under the mouse:** move the cursor onto a table and it becomes the
  active window, no click needed; a violet frame marks the active table.
  Focus is not taken while the cursor merely rests on a table (only when the
  mouse moves), while a mouse button is held, while a menu is open, and for
  1.5 s after you typed in another window.
- **How a button is pressed** — chosen per room in tray menu → hotkeys →
  room:

  | mode | |
  |---|---|
  | `direct` (default) | the click is sent straight to the table window; the cursor doesn't move |
  | `cursor` | the cursor jumps to the button for a moment and comes back — for clients that ignore `direct` |
  | `keys` | sends the client's own hotkey for that action — for rooms with built-in hotkeys that can't be bound to `Space` |

- **Recording buttons** (for `direct` and `cursor`), once per room: on your
  turn, hover over the button and press `Ctrl+Alt+F` (fold), `Ctrl+Alt+S`
  (check/call) or `Ctrl+Alt+D` (bet). The position is stored as a fraction of
  the table, so it still fits after the table is shrunk into a slot, and it
  applies to every room of the same client program.
- **GG / PokerOK — no hotkeys.** The program never presses anything on GG
  tables — no clicks, no client hotkeys, whatever `config.json` says. `Space`,
  `S` and `D` over a GG table reach the client as usual, buttons can't be
  recorded there, and GG rooms are greyed out in the menu. Focus under the
  mouse and the focus frame still work on GG tables.
- `Ctrl+Alt+H` — turn the hotkeys off/on (e.g. to type in the table chat).
- A room can be excluded: tray menu → hotkeys → room → untick it.
- A client running as administrator only accepts clicks and focus from a
  program that also runs as administrator.

Settings — the `ActionKeys` section of `bin-keys\config.json`:

```json
"ActionKeys": {
  "Enabled": true,
  "OnlyWhenPokerActive": true,
  "FocusUnderMouse": true,
  "FocusBorder": true,
  "ClickDelayMs": 20,
  "ToggleHotkey": "Ctrl+Alt+H",
  "Actions": [
    { "Name": "Фолд",     "Key": "Space", "RecordHotkey": "Ctrl+Alt+F" },
    { "Name": "Чек/колл", "Key": "S",     "RecordHotkey": "Ctrl+Alt+S" },
    { "Name": "Ставка",   "Key": "D",     "RecordHotkey": "Ctrl+Alt+D" } ],
  "ClickMode": "direct",
  "RoomClickModes": { "CoinPoker": "cursor" },
  "ClientKeys": [],
  "ExcludeRooms": [ "QQPK" ],
  "Buttons": [ { "Room": "Redstar", "Process": "PokerClient", "Action": "Фолд", "X": 0.5893, "Y": 0.9196 } ]
}
```

(`Фолд` = fold, `Чек/колл` = check/call, `Ставка` = bet.) In `RoomClickModes`
and `ClientKeys` a room can be named by its rule name or by its process
(which covers all its rooms, e.g. a client's tournament and cash tables).
`ClientKeys` is for the `keys` mode:
`{ "Room": …, "Action": "Фолд", "Keys": "Ctrl+F1" }`, where `Keys` is the
combination bound to that action in the client's own settings. `Actions` can be
extended with more client buttons (e.g. a "½ pot" button): a name, a key and
a record hotkey. Increase `ClickDelayMs` if a client misses presses.
`OnlyWhenPokerActive: false` makes the keys work over a table even when
another window is active.

## Status

- Layout: unit tests (`LayoutTests.exe`) and live sessions with TigerGaming,
  Pokerdom, Redstar, AKs Poker, CoinPoker, QQPK and PokerPlanets tables.
- Hotkeys build: unit tests and live tests on `FakeTable` windows — focus
  under the mouse, the focus frame, and all three press modes (with `direct`
  the cursor didn't move a single pixel). **Not yet tested with real poker
  clients.**

## License

[MIT](LICENSE)
