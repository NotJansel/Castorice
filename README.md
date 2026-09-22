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

The pool's name is the heading on the Tournament page — click it to rename the pool, then **Save
pool**. Renaming keeps the pool in its existing file, so nothing you have already shared breaks;
the status bar names the file it wrote to.

With API credentials configured, **Fetch metadata** fills in title, artist, mapper, star rating,
BPM, length and cover art for every pick in one request. `coverUrl` is written back into the pool
file, so a pool you share arrives with its artwork already set.

## Running a match

1. Connect on the top bar.
2. Pick a mappool, type the two team names, and press **Create lobby**. Castorice sends
   `!mp make` to BanchoBot, reads the match id out of the reply, joins `#mp_<id>` and pushes the
   pool's `!mp set` configuration. The title is `TP: (Red) vs (Blue)` — the stage is left out
   unless you tick **Put the stage in the lobby name**.
   Already have a lobby? Paste its id, `#mp_` channel or match-history link and press **Attach**.
3. Invite players, then click a mappool button to set the map and its mods.
4. Right-click a pick to **ban** or **protect** it for either team. Banned picks grey out and
   refuse to be sent to the lobby until the ban is cleared, so a misclick cannot burn a map.
5. **Start** runs the pool's countdown; **Start now** skips it; **Abort timer** and
   **Abort match** are one click away.
6. The panel tracks slots, teams and per-player scores as BanchoBot reports them. Each player row
   can **Move** them to a slot, swap their **Team** or **Kick** them, and **+1 Red** / **+1 Blue**
   keep the running score by hand whenever you want them to.

### FreeMod rule check

A FreeMod pick is not "everybody needs a mod" but a per-team quota: a team owes one HardRock player
and one on Hidden or Easy, and whoever is left over may play NoMod. The NoMod allowance falls out
of that on its own — a 3v3 team has one spare slot and a 4v4 team two — so the team size is never
configured anywhere.

With **FreeMod check** on, the lobby is checked the moment everyone is ready, and a warning naming
what is missing goes into the chat:

```
FreeMod check: Red needs 1x HR
```

It stays quiet when the lobby is fine, and **Check FreeMod** runs it on demand. A player counts
towards the first group they match, which is why HDHR fills the HardRock slot rather than the
Hidden one. NoFail is tolerated on top of anything but never fills a slot by itself.

The quota and the set of mods allowed at all live in the pool file; the minimums are editable on
the Tournament page:

```json
{
  "freeModAllowedMods": "Hidden, HardRock, Easy, Flashlight",
  "freeModGroups": [
    { "name": "HR",    "anyOf": "HardRock",       "minimumPerTeam": 1 },
    { "name": "HD/EZ", "anyOf": "Hidden, Easy",   "minimumPerTeam": 1 }
  ]
}
```

### Scoring a map

With **Auto-score** on, a finished map is totalled by team, the point is awarded, and the result is
posted into the lobby:

```
[FM1] Kobaryo - Ironclad [Overkill] | Red 1,234,567 - 1,000,000 Blue | Red wins by 234,567
Multipliers: SomePlayer EZ x1.75 (100,000 -> 175,000)
Match score: Red 3 - 2 Blue (first to 7)
```

- **Warmup** is on when the page opens and blocks scoring entirely, so a warmup can never take a
  point by accident. Turn it off when the match proper starts.
- **Best of** sets the target; `13` means first to 7.
- A failed score counts as zero, the way bracket rules treat it.
- If the map ends and no scores arrive, nothing is awarded and the status bar says so rather than
  guessing.

### FreeMod multipliers

On a **FreeMod** pick, players who take Easy have their score multiplied before the teams are
totalled. Easy and Easy+Hidden carry separate factors, because Hidden already earns its own ScoreV2
bonus.

Multipliers are set **per FreeMod pick** — a map where Easy barely helps can be scored differently
from one where it does a lot. The Tournament page lists every FreeMod pick with its own two boxes,
and each pick is marked `pool default` or `custom`:

```json
{
  "easyMultiplier": 1.75,
  "easyHiddenMultiplier": 1.75,
  "slots": [
    { "label": "FM1", "mods": "FreeMod" },
    { "label": "FM2", "mods": "FreeMod", "easyMultiplier": 1.30, "easyHiddenMultiplier": 1.20 }
  ]
}
```

The two values at the pool level are the **default** a pick falls back to, so a bracket with one
rule sets it once. `FM1` above follows that default; `FM2` overrides it. Changing the default moves
every pick that follows it and leaves the overrides alone, and **Use pool default** on a pick drops
its override again.

Multipliers apply only to FreeMod picks — on a forced-mod pick everyone is on the same mods, so
there is nothing to even out. Every adjustment is named in the message posted to the lobby, so the
teams can check the maths. Set the values to `1.00` to score a pick raw.

Everything the referee panel sends is an ordinary `!mp` command, and it all shows up in the
`#mp_…` channel on the Chat page — nothing happens behind your back.

## Chat

The Chat page is a normal IRC client: channel list with unread counts, private messages, message
history on <kbd>↑</kbd>/<kbd>↓</kbd>, and a raw wire console for debugging. Joins and parts produce
no chat line — in a busy channel they drown out the conversation, and the user count above the
backlog already says who is there. The usual commands work:

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
  Fixtures/                Every distinct BanchoBot line from a real bracket match, so the
                           parser is checked against what Bancho says, not what it was assumed to
samples/
```

The core library never references Avalonia. UI thread marshalling goes through the small
`IUiDispatcher` seam, which the desktop project implements and tests replace with a pass-through.

## Development

```bash
dotnet build              # whole solution
dotnet test               # 174 tests, no network needed
dotnet run --project src/Castorice.Desktop
```

Tests run on Microsoft.Testing.Platform, which xunit v4 requires and which the .NET 10 SDK selects
through the `test` section of [`global.json`](global.json) — the old VSTest bridge is gone.

### Restore fails with NU1100

```
error NU1100: Unable to resolve 'Avalonia (>= 11.3.22)' for 'net10.0'
```

If *every* package fails this way — including ordinary ones like CommunityToolkit.Mvvm — the
problem is the package sources, not the versions. NU1100 means NuGet had nowhere to look, which
happens when the source list is empty or nuget.org has been switched off, often by the IDE or by a
`NuGet.config` in a parent folder such as `RiderProjects\`.

The [`NuGet.config`](NuGet.config) at the repository root pins nuget.org and clears anything
inherited, so a fresh clone restores the same way everywhere. If you still see NU1100, check what
NuGet actually resolves from the repository folder:

```bash
dotnet nuget list source
```

nuget.org must be listed and `[Enabled]`. Then retry with a clean cache:

```bash
dotnet nuget locals http-cache --clear
dotnet restore --force
```

In Rider, the same list lives under **Settings → Build, Execution, Deployment → NuGet → Sources**;
in Visual Studio under **Tools → NuGet Package Manager → Package Sources**.

Publishing a self-contained binary:

```bash
dotnet publish src/Castorice.Desktop -c Release -r win-x64   --self-contained
dotnet publish src/Castorice.Desktop -c Release -r osx-arm64 --self-contained
dotnet publish src/Castorice.Desktop -c Release -r linux-x64 --self-contained
```

## License

MIT — see [LICENSE](LICENSE).
