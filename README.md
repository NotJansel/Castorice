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
profile page loads avatars and banners the same way. Images are fetched once and kept on disk
under `cache/images` in the config directory, so a pool opens instantly — and still shows its
covers with no connection at all. Settings shows how much the cache holds and has a button to
clear it; anything cleared is simply downloaded again when next shown. A cached file that turns
out to be cut short is thrown away and fetched again rather than shown as a blank tile.

## Getting started

### Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

### Run it

```bash
dotnet run --project src/Castorice.Desktop
```

### Installers

Every push builds installers for all three systems; open the run under **Actions → Package** and
download them from its **Artifacts**. Pushing a version tag publishes them as a GitHub release:

```bash
git tag v0.2.0 && git push origin v0.2.0      # a tag with a suffix, e.g. v0.2.0-beta.1, is a pre-release
```

A release takes its version from the tag. Everything else — `dotnet run`, a local build, the
packaging scripts run by hand, the installers from a plain push — is a development build named
after the release being worked towards, `<VersionPrefix>-dev` from `Directory.Build.props` (e.g.
`0.2.0-dev`). Settings and About show it with its commit, and it only looks for updates when
asked. After tagging a release, raise `VersionPrefix` to the next version.

| System | File | Install |
| --- | --- | --- |
| Windows | `Castorice-<version>-windows-x64-setup.exe` | Run it. Installs for the current user without an admin prompt, with a Start menu entry and an uninstaller. |
| Windows | `…-windows-x64-portable.zip` | Unzip anywhere and start `Castorice.exe`. |
| macOS | `Castorice-<version>-macos-arm64.dmg` (Apple silicon) or `…-macos-x64.dmg` (Intel) | Open it and drag Castorice into Applications. |
| Linux | `Castorice-<version>-linux-x86_64.AppImage` | `chmod +x` it and start it. Needs FUSE 2 (`libfuse2`); without it, start it with `--appimage-extract-and-run`. |
| Linux | `…-linux-x86_64.tar.gz` | Unpack anywhere and start `Castorice`. |

All builds are self-contained, so no .NET is needed on the machine. Linux needs the usual desktop
libraries and ICU (`libicu`), which every desktop distribution ships.

Windows SmartScreen warns about the installer until it is signed with a paid certificate: **More
info → Run anyway**. Settings, pools and the image cache live in the user's config directory and
survive an uninstall.

#### Updates

Castorice looks for a new release on GitHub when it starts and every six hours, and offers it in a
banner at the top of the window: **Update now**, **What's new**, **Skip this version**, or ✕ to be
reminded next time. **Settings → Check for updates** asks straight away, and the automatic check
can be switched off there. Only published releases are offered, never drafts or pre-releases.

**Update now** downloads the installer for the system, checks it against the SHA-256 checksum
GitHub lists for it, and puts it in place:

| Installed with | What happens |
| --- | --- |
| Windows setup | The new setup runs silently over the installation and starts Castorice again. |
| macOS disk image | Castorice quits, the app in Applications is swapped for the new one, and it starts again. If the folder is not writable, the new disk image opens for dragging in by hand. |
| Linux AppImage | The AppImage file is replaced and started again. |
| Portable zip, .tar.gz, `dotnet run` | The release page opens. |

The check asks `api.github.com` for the repository's latest release, so the repository (or at
least its releases) has to be public. A release's tag is its version: tag `v0.2.0` is offered to
everything older than 0.2.0, `0.2.0-dev` included.

The app only looks at the release GitHub marks as **Latest**. The packaging workflow gives that
mark to a release only when it is the highest version published, so a patch for an older line —
`v0.1.1` tagged after `v0.2.0` is out — goes out without it, and 0.1.x users are still offered
0.2.0.

#### Signing and notarising for macOS

A Mac only opens a downloaded app without complaint when it is signed with a Developer ID and
notarised by Apple. Until that is set up the app is signed ad hoc, and macOS says it cannot check
it for malware. To open such a build anyway, try to open it once, then allow it under **System
Settings → Privacy & Security → Open Anyway** (on macOS 14 and older right-click → **Open** also
works), or clear the download flag: `xattr -dr com.apple.quarantine /Applications/Castorice.app`.

The Package workflow signs and notarises by itself once these repository secrets exist
(**Settings → Secrets and variables → Actions**); it needs a membership in the Apple Developer
Program:

| Secret | What goes in it |
| --- | --- |
| `MACOS_CERTIFICATE_P12` | The **Developer ID Application** certificate with its private key, exported from Keychain Access as .p12, then `base64 -i cert.p12 \| pbcopy` |
| `MACOS_CERTIFICATE_PASSWORD` | The password chosen for that export |
| `MACOS_NOTARY_KEY` | An App Store Connect API key (Users and Access → Integrations → Keys, access "Developer"): the .p8 file's contents |
| `MACOS_NOTARY_KEY_ID` | That key's ID |
| `MACOS_NOTARY_ISSUER_ID` | The issuer ID shown above the keys |

Instead of the API key, an Apple ID works too: `MACOS_NOTARY_APPLE_ID`, `MACOS_NOTARY_PASSWORD`
(an app-specific password from account.apple.com) and `MACOS_NOTARY_TEAM_ID`.
`MACOS_SIGNING_IDENTITY` is only needed when the certificate holds more than one identity.

With the certificate alone the app is signed but not notarised, and macOS still warns. With both,
the app and the disk image are notarised and the tickets stapled, so they open without a network
check. A CI job signs every build the same way with a throwaway certificate and starts the app, so
the hardened runtime is known to work before a real certificate is added. Locally the same script
takes the settings as environment variables; its header lists them.

The same packages can be built locally, each on its own system:

```bash
build/package-macos.sh               # Castorice.app and a .dmg; osx-arm64 / osx-x64 to choose
build/package-linux.sh               # AppImage and .tar.gz; linux-arm64 for ARM
```

```powershell
build\package-windows.ps1            # setup.exe (needs Inno Setup 6) and a portable .zip
```

The app icon comes from `src/Castorice.Desktop/Assets`: `castorice.ico` for Windows and the
window, `castorice.png` for Linux and the sidebar, and `castorice-dock.png` (512×512 with a
transparent margin, like other Mac icons) for the macOS app and Dock.

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

### Importing from the Mappool Builder

Pools built on the [Mappool Builder](https://pools.jansel.dev) can be pulled straight in: **Import
from Mappool Builder** on the Tournament page lists your own pools and recently updated public ones,
or takes a pool link or id (`https://pools.jansel.dev/pools/Xb3kQ9aZ`, `Xb3kQ9aZ`).

- Public pools need nothing. For your own pools, private ones included, create a **read** token
  under API tokens on the site and paste it on the **Settings** page; **Test connection** shows
  whose it is. Castorice only reads, so a read token is all it ever needs. The token is stored in
  the settings file like the IRC password, and it is only ever sent to the address it was entered
  for.
- Picks are labelled the way the Builder labels them — counted per bracket, `NM1`, `NM2`, `HD1` …
  — and grouped in its bracket order. A lone tiebreaker is `TB`. Titles, mappers, star ratings
  (with the mod), BPM, length, cover art and notes come along, so no metadata fetch is needed.
- Brackets become mods: `FM` and `TB` are FreeMod, `SD` is sent without NoFail (the two cancel each
  other out), everything else gets NoFail per the pool's setting.
- Team names on the Builder pool fill the Red and Blue boxes when those are still empty.
- The imported pool is an ordinary pool file that remembers its source. **Update from Mappool
  Builder** (or importing it again) pulls the current maps: picks that are still there keep their
  multiplier overrides, and the pool's own settings — room defaults, draft order, multipliers,
  referees, even a local rename — stay. Nothing is overwritten when a pool of the same name
  already exists.

## Running a match

1. Connect on the top bar.
2. Pick a mappool, type the two team names, and press **Create lobby**. Castorice sends
   `!mp make` to BanchoBot, reads the match id out of the reply, joins `#mp_<id>` and pushes the
   pool's `!mp set` configuration. The title is `TP: (Red) vs (Blue)` — the stage is left out
   unless you tick **Put the stage in the lobby name**.
   Already have a lobby? Paste its id, `#mp_` channel or match-history link and press **Attach**.
3. Invite players, then click a mappool button to set the map and its mods. NoFail is added to
   every pick that is not FreeMod — `NM1` goes out as `!mp mods NF`, `DT1` as `!mp mods NF DT` —
   so a player who fails still posts a score; FreeMod picks stay `!mp mods Freemod`. Untick
   **Add NoFail to every pick except FreeMod** (`"forceNoFail": false`) for a bracket without it.
4. Protects and bans follow the pool's **draft order** (below): while one is due, clicking a map
   marks it for the team in turn. Right-click any pick to set or correct a ban, protect or pick by
   hand. Banned picks grey out and refuse to be sent to the lobby until the ban is cleared, so a
   misclick cannot burn a map.
5. **Start** runs the pool's countdown; **Start now** skips it; **Abort timer** and
   **Abort match** are one click away.
6. The panel tracks slots, teams and per-player scores as BanchoBot reports them. Each player row
   can **Move** them to a slot, swap their **Team** or **Kick** them, and **+1 Red** / **+1 Blue**
   keep the running score by hand whenever you want them to.

### Draft order: protects, bans and picks

Every pool carries its bracket's draft order, edited in the **Draft order** card and saved with the
pool:

| Setting | Options |
| --- | --- |
| Protects | optional: off for a new pool, tick **Protects** and set how many per team |
| Protect order, ban order | **ABAB** (alternating) or **ABBA** (snake) |
| Bans per team | before the first pick |
| Pick order | **ABAB**, **ABBA**, **loser of the last map picks**, **winner of the last map picks** |
| Second ban round | after N picks, with its own number of bans; optionally opened by the other team |

```json
{
  "draft": {
    "protectsPerTeam": 1, "protectOrder": "Alternating",
    "bansPerTeam": 2,     "banOrder": "Snake",
    "pickOrder": "LoserPicks",
    "secondBanRoundAfterPicks": 4, "secondRoundBansPerTeam": 1, "secondRoundOtherTeamFirst": false
  }
}
```

Who opens each phase is a per-match choice — **First protect**, **First ban** and **First pick** in
the lobby panel, set after the roll. The panel then always shows whose turn it is (`Poland bans ·
3 of 4`), with every protect, ban and pick so far listed underneath. Picks outside warmup are
credited to the team in turn; once both teams are one point short the tiebreaker is called, and
the draft ends with the match.

Protects and bans can also be given up:

- **Skip** — while a protect or ban is due, a button lets the team in turn pass on it
  (`Poland skips their protect`, `Germany skips a ban`) and the draft moves on.
- **Late show** — **… loses bans** takes away every ban that team has left, second round
  included, as brackets rule for a team that shows up late (`Poland forfeits 2 bans`). The other
  team's bans then run back to back.

Skips and forfeits are listed in the draft (`Bans: Germany NM1, Poland forfeited 2`), posted like
any other draft action, and **Undo** takes them back — a forfeit as a whole. **Undo** takes back the newest mark without posting anything.

The draft counts each team's own marks rather than the position in the sequence, so if a ban is
marked for the wrong team the other team's turn does not get skipped — the step it still owes stays
next.

### Lobby messages

Everything the panel posts on its own can be switched off line by line under **Post to the lobby
automatically**, and the choice is remembered:

| Switch | Example |
| --- | --- |
| Map result | `[NM1] Artist - Title [Diff] \| Red 1,234,567 - 1,000,000 Blue \| Red wins by 234,567` |
| FreeMod multipliers | `Multipliers: SomePlayer EZ x1.75 (100,000 -> 175,000)` |
| Match score after each map | `Match score: Red 3 - 2 Blue (first to 7)`, or `Red wins the match 7 - 5` |
| Match score after +1 | the same line when a point is awarded by hand |
| Protects, bans and picks | `Poland bans NM1`, `Germany picks DT1: Artist - Title [Diff]`, skips and forfeits |
| Whose turn is next | `Next: Germany bans (2/4)` — after each protect or ban and after each scored map |
| FreeMod check warnings | `FreeMod check: Red needs 1x HR` |

**Post score** and the draft panel's **Post** send the score or the draft on demand, whatever the
switches say.

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
posted into the lobby (each line can be switched off, see [Lobby messages](#lobby-messages)):

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
history on <kbd>↑</kbd>/<kbd>↓</kbd>, and a raw wire console for debugging. The view follows new
messages as they arrive; scroll up to read and it stops, with a **Jump to latest** button to get
back, and scrolling down to the bottom picks it up again. If the connection drops, reconnecting
rejoins every channel that was open — the attached lobby included — and asks BanchoBot for the
lobby's current settings, so the referee buttons work again straight away. Joins and parts produce
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
  MappoolBuilder/          Read-only client and import for the Mappool Builder API
  Configuration/           Settings model, atomic writes, platform paths
src/Castorice.Desktop/     Avalonia UI (MVVM, CommunityToolkit.Mvvm)
build/                     Installer scripts per system, with their templates (Info.plist,
                           entitlements, Inno Setup script, AppImage desktop entry) and the
                           macOS signing set-up for CI
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
dotnet test               # 361 tests, no network needed
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
