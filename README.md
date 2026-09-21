# Casino Ledger

Who is actually winning at the casino. Tracks every player's profit and loss across
**Blackjack**, **Ride the Bus** and **Slots**.

- **Live standings.** A panel on the right of the screen lists each player's total casino
  profit in this save, green when up, red when down, best first. It is always on screen
  with the game HUD, starting at $0 for everyone.
- **Day-end stats.** After the sleep summary closes, a stats screen shows the group's
  wagered, returned and profit totals, a chart of each player's running profit over the day,
  profit per game, and the best and worst single rounds. Click Continue, or press the confirm button on a controller, to close it.
- **Totals per save.** Rounds, wins, losses, pushes, amount wagered, amount returned and
  largest win and loss, per player and per game, kept in one file per save under
  `UserData/CasinoLedger`. A co-op world uses the same file name on the host and every client.

Install it on every player. Each player's game reports its own rounds to the rest of the
session, and the host's totals are the shared truth: anyone who joins late or missed rounds is
brought in line when they join and again at the end of each day. Players without the mod are
not tracked.

Totals from Casino Ledger 0.1 (`ledger.txt`) and Death Notices 0.2 mixed every save together, so
they are not carried over; the old files are left in place. The mod never changes payouts, odds or money.

## Settings

The **Mods** tab of the game's settings screen (from [Mod Settings](https://thunderstore.io/c/schedule-i/p/holyfurries/ModSettings/),
installed automatically) has the standings switch, the standings size and the day-end stats
switch. F7 also toggles the standings in game. Everything is stored in `UserData/MelonPreferences.cfg`, section `CasinoLedger`:

| Key | Default | Meaning |
| --- | --- | --- |
| `hud_enabled` | `true` | Show the live standings panel |
| `day_summary_enabled` | `true` | Show the day-end stats screen |
| `hud_margin_right` | `13` | Panel distance from the right edge, in 1920x1080 units; the default lines up with Clear Minimap |
| `hud_margin_top` | `420` | Panel distance from the top edge, in 1920x1080 units |
| `hud_scale` | `1` | Panel size multiplier from 0.5 to 3 |
| `hud_toggle_key` | `F7` | Key that shows or hides the panel in game and saves the choice; `None` disables the key |

## For mod authors

Reference `CasinoLedger.dll` and use the `CasinoLedger.CasinoStats` class:

```csharp
CasinoStats.round_settled += report =>
{
    // report.round: player_key, player_name, game, stake, returned, net
    // report.lifetime / report.day: Totals for that player after the round
    if (report.round.net < 0) MelonLogger.Msg($"{report.round.player_name} is at {report.lifetime.net}");
};
Totals slots_today = CasinoStats.day(CasinoStats.player_key(player.PlayerCode), CasinoGame.Slots);
```

The event fires on the Unity main thread for every player's rounds, local and remote.
To keep Casino Ledger optional, add `[assembly: MelonOptionalDependencies("CasinoLedger")]`,
check `MelonBase.FindMelon("Casino Ledger", "holyfurries")`, and only touch `CasinoStats`
from a separate non-inlined method. Death Notices does exactly this.

## Installation

Install through **r2modman** or **Thunderstore Mod Manager**.

For manual installation, put `CasinoLedger.dll` in your game's `Mods` folder.
Requires **MelonLoader 0.7.3**. **IL2CPP only.**

## Development

Build instructions: [BUILDING.md](https://github.com/holyfurries/casino-ledger/blob/main/BUILDING.md).

## License

[MIT](https://github.com/holyfurries/casino-ledger/blob/main/LICENSE).
