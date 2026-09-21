# Development

Version 0.5.1, built against local Schedule I 0.4.6f13 IL2CPP interop assemblies.

## Layout

`Ledger.cs` and `CasinoStats.cs` have no game dependencies and are covered by `tests/`.
`Ledger` holds up to 32 players, lifetime and day `Totals` per game, and a day series of at
most 64 points per player that halves itself when full. `CasinoStats` is the public API: it
owns the live ledger, raises `round_settled`, and isolates throwing subscribers.
`Hooks.cs` observes the game, `Hud.cs` and `DaySummary.cs` draw, `Ui.cs` holds shared
uGUI helpers. Layout follows Gamble With Your Friends: a bordered standings box on the
right and a "stats for day N" screen with a per-player profit chart.

## Observing rounds

Slots: `SlotMachine.RpcLogic___StartSpin` reaches every peer with the spinner connection,
symbols and bet. Only the local player's spins are recorded, matched by owner client id, and the payout is
computed with the machine's own `EvaluateOutcome` and `GetWinAmount`. The round is held
until `DisplayOutcome` fires for that machine or eight seconds pass, so the HUD does not spoil
a spin. A second `StartSpin` within a second of the first on the same machine is ignored, so an
RPC that runs twice on one peer cannot count a spin twice (confirmed cause of doubled totals
when many machines are spun at once); a later one settles the held round first.

Blackjack and Ride the Bus settle on the local client only. As in Death Notices 0.2,
`AddPlayerToCurrentRound` arms a table and the `RemoveLocalPlayerFromGame` finalizer
measures stake and cash returned.

Every peer records only its own rounds and reports each one to the session through the game's
Steam lobby chat (`Lobby.SendLobbyMessage`, received through `SteamLobbyService.OnLobbyMessage`).
`Wire` defines the text messages: `round` carries up to twelve settled rounds, flushed once a second so spinning every machine
at once stays a trickle of messages, `hello` is sent by a client
once its ledger has loaded, and the host answers with one `total` message per ledger row, which
clients adopt in place of their own. The host repeats the totals at every day end, so a missed
message heals within a day. A row that is older than the rounds already counted today is
skipped. Peers ignore the echo of their own rounds. Players without the mod are not tracked.
Values are validated and bounded by `Ledger.settle`; these are cosmetic client-reported
figures, not an anti-cheat ledger.

`DailySummary.Close` ends the casino day: pending spins settle, the stats screen shows if
any round was played, and day totals reset. Day totals are not persisted; lifetime totals
are written after every round with a temporary-file rename. Player keys are SHA-256 hashes
of `PlayerCode`. The ledger file is `<world_key>.txt`, where the world key hashes
`GameManager.seed` and `OrganisationName`, both replicated to clients, so every peer in a
world names the same save. It loads on the first update after `IsGameLoaded`, once clients
have that data; hooks ignore rounds until then. The HUD shows these per-save totals.
In-game options go through the Mod Settings mod (`ModSettings.Settings`), a required
dependency; the build references `../mod-settings/bin/Release/net6.0/ModSettings.dll`
(override with `-p:ModSettingsDll=`), so build Mod Settings first.
Hook failures pause tracking for the scene; overlay failures disable only the overlay.

## Build

```sh
MELONLOADER_DIR='/path/to/profile/MelonLoader' ./build.sh
nix-shell -p dotnet-sdk_8 --run 'dotnet run --project tests/Tests.csproj'
```

## Manual checks still required

Build and tests do not establish native UI layout, Harmony RPC ordering or replication.

- Slots: compare logged `stake`/`returned` with the wallet for a loss, each win tier and a
  jackpot. Confirm `GetWinAmount` is the gross payout and that `DisplayOutcome` fires on a
  losing spin (otherwise losses land after the eight second fallback).
- Blackjack: bust, dealer loss, push, win, blackjack. Ride the Bus: fail and cash out at
  each stage. Compare logs with the wallet.
- Co-op: every player's slot spins and card rounds appear on host and clients, once each, and
  the standings match on all screens. A client who joins late or restarts shows the host's
  totals within seconds. A long slot session on the host counts each spin once.
- HUD position against the quest tracker at 16:9 and ultrawide; hidden with the game HUD.
- Day stats text matches the game's sleep summary (12 to 18 units there; the layout is authored
  at double size and drawn at three quarter scale, since half was too small to read), and the backdrop hides the HUD and minimap.
- Sleep with and without casino rounds; the stats screen follows the sleep summary, shows a
  cursor, stays until Continue is clicked or the game's Submit button is pressed (A on a controller;
  input is ignored for the first 0.4 seconds so the press that closed the sleep summary does not
  skip it; Escape is the fallback), and the next day starts from zero.
- Mods tab: Casino Ledger heading with Show standings, Standings size and Day-end stats
  screen; F7 flips the first while the tab is open.
- Two saves keep separate totals; a co-op client logs the same ledger file name as the host
  and keeps its totals after rejoining. HUD text matches the Clear Minimap clock (12 units) at `hud_scale` 1; corners and outline stay
  concentric at 0.5 and 3; F7 toggles it and the settings switch follows.
