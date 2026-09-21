# Castorice

An osu! IRC client with a tournament referee panel, built on .NET 10 and Avalonia.
Runs on Windows, macOS and Linux from one codebase.

Three things it sets out to do better than a generic IRC client:

- **Mappool buttons.** Configure a pool once, then pick maps with one click. Each button sends
  `!mp map <id>` and `!mp mods <mods>` for you, so nobody types a beatmap id wrong at 2am.
- **A referee toolbar.** Start, abort, timers, invites, team swaps, lock/unlock and `!mp settings`
  are buttons, and the lobby state is parsed back out of BanchoBot's replies.
- **A readable profile view.** Ranks, accuracy, play time, grades, a 90-day rank sparkline, and the
  top and recent plays — without leaving the client.

## The interface

A navigation rail on the left carries the four pages and the Bancho connection; the rest of the
window belongs to whichever page is open:

```
┌──────────────┬──────────────────────────────────────────────────────────────┐
│ C Castorice  │ [Mappool ▾][Red team][Blue team][Create lobby][id][Attach]   │
│              ├───────────────────────────────────────┬──────────────────────┤
│ ▍Chat        │ Castorice Cup — Finals                │ LOBBY                │
│  Tournament  │ NoMod    ┌────────┐┌────────┐         │ RED 2  vs  BLUE 1    │
│  Profile     │          │NM1  NM │││NM2  NM│         │ MATCH CONTROL        │
│  Settings    │          │cover…  ││cover…  │         │ [Start][Abort][Timer]│
│              │ Hidden   └────────┘└────────┘         │ PLAYERS / LOBBY LOG  │
│ ● BANCHO     │          ┌────────┐                   │                      │
│  [Connect]   │          │HD1  HD │                   │                      │
└──────────────┴───────────────────────────────────────┴──────────────────────┘
```

Pick tiles show the beatmap's cover art behind the title, mods and difficulty stats, and the
profile page loads avatars and banners the same way. Images are fetched once and cached under
`cache/images` in the config directory, so reopening a pool costs nothing.

## Getting started

### Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

### Run it

```bash
dotnet run --project src/Castorice.Desktop
```

### Credentials

Castorice needs two sets of credentials, both entered on the **Settings** page.

1. **IRC** — your osu! username and the *IRC server password* from
   <https://osu.ppy.sh/home/account/edit#legacy-api>. This is **not** your account password.
2. **osu! API** (optional, for the profile view and beatmap metadata) — a client id and secret
   from an OAuth application created at <https://osu.ppy.sh/home/account/edit#oauth>.
   Only the `public` scope is used.

Both are stored in `settings.json` under the platform config directory, shown on the Settings page:

| Platform | Location |
| --- | --- |
| Windows | `%APPDATA%\Castorice\` |
| macOS | `~/Library/Application Support/Castorice/` |
| Linux | `~/.config/Castorice/` |

The file is plain text, created with owner-only permissions on macOS and Linux. Treat it the way
you would treat the password itself.

## Mappools

A pool is one JSON file in the `mappools/` subdirectory of the config directory, so sharing a pool
with the rest of the staff means sending a single file. See
[`samples/example-mappool.json`](samples/example-mappool.json) for the full shape:

```json
{
  "name": "Castorice Cup — Quarterfinals",
  "acronym": "CC",
  "stage": "Quarterfinals",
  "teamMode": "TeamVs",
  "scoreMode": "ScoreV2",
  "slots": [
    { "label": "NM1", "category": "NoMod",  "beatmapId": 1234567, "mods": "None" },
    { "label": "HD1", "category": "Hidden", "beatmapId": 1234570, "mods": "Hidden" }
  ]
}
```

- `beatmapId` is the **difficulty** id — the last number in an `osu.ppy.sh/b/…` link, not the
  beatmapset id from a `/s/` link.
- `mods` accepts `None`, a single mod (`Hidden`), or a combination (`HDHR`, `HD HR`, `HR+FM`).
  `FreeMod` tells the lobby that players pick their own.
- `category` groups the picks into rows in the UI. Leave it out and the leading letters of `label`
  are used, so `HD2` lands in an `HD` row on its own.

With API credentials configured, **Fetch metadata** fills in title, artist, mapper, star rating,
BPM, length and cover art for every pick in one request. `coverUrl` is written back into the pool
file, so a pool you share arrives with its artwork already set.

## Running a match

1. Connect on the top bar.
2. Pick a mappool, type the two team names, and press **Create lobby**. Castorice sends
   `!mp make` to BanchoBot, reads the match id out of the reply, joins `#mp_<id>` and pushes the
   pool's `!mp set` configuration.
   Already have a lobby? Paste its id, `#mp_` channel or match-history link and press **Attach**.
3. Invite players, then click a mappool button to set the map and its mods.
4. **Start** runs the pool's countdown; **Start now** skips it; **Abort timer** and
   **Abort match** are one click away.
5. The panel tracks slots, teams and per-player scores as BanchoBot reports them, and **+1 Red** /
   **+1 Blue** keep the running score.

Everything the referee panel sends is an ordinary `!mp` command, and it all shows up in the
`#mp_…` channel on the Chat page — nothing happens behind your back.

## Chat

The Chat page is a normal IRC client: channel list with unread counts, private messages, message
history on <kbd>↑</kbd>/<kbd>↓</kbd>, and a raw wire console for debugging. The usual commands work:

| Command | Effect |
| --- | --- |
| `/join #channel`, `/j` | Join a channel |
| `/part [#channel]` | Leave the current or named channel |
| `/msg <user> [text]`, `/query` | Open a private conversation |
| `/me <action>` | Send a CTCP action |
| `/raw <line>` | Send a raw IRC line |
| `/quit [reason]` | Disconnect |
| `//text` | Send text starting with a literal `/` |

Outgoing messages go through a sliding-window rate limiter set to osu!'s quota of 10 messages per
5 seconds. If osu! staff have flagged your account as a bot, the Settings page has a switch that
raises it to 300 per 60 seconds. Leave it off otherwise — exceeding the quota gets you silenced.

## Project layout

```
src/Castorice.Core/        No UI dependencies; all of it is unit-testable
  Irc/                     Line parser, Bancho client, rate limiter
  Chat/                    Per-target backlogs, slash commands
  Bancho/                  !mp command builder, BanchoBot reply parser, live room state
  Tournament/              Mappool model + JSON store, referee controller
  Osu/                     osu! API v2 client (client-credentials grant)
  Configuration/           Settings model, atomic writes, platform paths
src/Castorice.Desktop/     Avalonia UI (MVVM, CommunityToolkit.Mvvm)
tests/Castorice.Core.Tests/
samples/
```

The core library never references Avalonia. UI thread marshalling goes through the small
`IUiDispatcher` seam, which the desktop project implements and tests replace with a pass-through.

## Development

```bash
dotnet build              # whole solution
dotnet test               # 97 tests, no network needed
dotnet run --project src/Castorice.Desktop
```

Publishing a self-contained binary:

```bash
dotnet publish src/Castorice.Desktop -c Release -r win-x64   --self-contained
dotnet publish src/Castorice.Desktop -c Release -r osx-arm64 --self-contained
dotnet publish src/Castorice.Desktop -c Release -r linux-x64 --self-contained
```

## License

MIT — see [LICENSE](LICENSE).
