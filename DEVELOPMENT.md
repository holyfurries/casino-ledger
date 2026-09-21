# Development

Version 0.2.2, built against local Schedule I 0.4.6f13 IL2CPP interop assemblies.

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
symbols and bet. The spinner is matched to a `Player` by owner client id and the payout is
computed with the machine's own `EvaluateOutcome` and `GetWinAmount`. The round is held
until `DisplayOutcome` fires for that machine, a new spin starts on it, or eight seconds
pass, so the HUD does not spoil a spin. No custom networking is involved.

Blackjack and Ride the Bus settle on the local client only. As in Death Notices 0.2,
`AddPlayerToCurrentRound` arms a table and the `RemoveLocalPlayerFromGame` finalizer
measures stake and cash returned. The round is then shared through the game's own
`CasinoGamePlayers.SendPlayerFloat` with key `CasinoLedger|<game>|<stake>` and the returned
cash as the value. Peers record rounds received for other players and ignore their own echo.
Values are validated and bounded by `Ledger.settle`; these are cosmetic client-reported
figures, not an anti-cheat ledger.

`DailySummary.Close` ends the casino day: pending spins settle, the stats screen shows if
any round was played, and day totals reset. Day totals are not persisted; lifetime totals
are written after every round with a temporary-file rename. Player keys are SHA-256 hashes
of `PlayerCode`. The ledger file is `<world_key>.txt`, where the world key hashes
`GameManager.seed` and `OrganisationName`, both replicated to clients, so every peer in a
world names the same save. It loads on the first update after `IsGameLoaded`, once clients
have that data; hooks ignore rounds until then. The HUD shows these per-save totals.
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
- Co-op: a remote player's slot spins and card rounds appear on host and clients, once each.
  Confirm `SendPlayerFloat` with a custom key is relayed for a player leaving the round.
- HUD position against the quest tracker at 16:9 and ultrawide; hidden with the game HUD.
- Sleep with and without casino rounds; the stats screen follows the sleep summary, closes
  on Space, Escape or after thirty seconds, and the next day starts from zero.
- Two saves keep separate totals; a co-op client logs the same ledger file name as the host
  and keeps its totals after rejoining. HUD readable at `hud_text_size` 10, 16 and 32.
