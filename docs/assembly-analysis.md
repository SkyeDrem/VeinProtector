# Local game assembly analysis

Analyzed the local `DSPGAME_Data\Managed\Assembly-CSharp.dll` copy. Its file version metadata is `0.0.0.0`. The local Steam app manifest reports build ID `25503975`, which corresponds to DSP `0.10.35.29088`; the game executable reports Unity `2022.3.62.1451004`. The r2modman BepInEx log is dated April 30, 2026 and predates the current game DLL, so its older `0.10.34.28518` Nebula compatibility line is not treated as the current game version.

## Verified API

- `MinerComponent` is a value type.
- `MinerComponent.InternalUpdate` has six parameters and returns `UInt32`. Its parameter types, from the local signature blob, are `PlanetFactory`, `VeinData[]`, `Single`, `Single`, `Single`, and `Int32[]`.
- `MinerComponent` has `type`, `speed`, `speedDamper`, `time`, `period`, `veins`, `veinCount`, `currentVeinIndex`, `minimumVeinAmount`, `productId`, `productCount`, and `costFrac` fields.
- `VeinData` is a value type with `id`, `type`, `groupIndex`, `amount`, and `productId` among its fields.
- The ordinary solid vein miner enum value is `EMinerType.Vein` (value 2); the oil branch is separate (value 3).
- `GameHistoryData.miningCostRate` and `miningSpeedScale` are present.
- The ordinary-miner speed multiplier in `UIMinerWindow._OnUpdate` multiplies by `veinCount`. Oil has a separate amount and oil-speed multiplier branch.
- `InternalUpdate` updates `productCount` and the passed `Int32[]` product register from the generated output count. It updates vein and group amounts, then removes/notifies a vein only on the vanilla depletion path.

## `InternalUpdate` behavior observed in IL

For the solid-vein branch, vanilla first accumulates time using power, `speedDamper`, miner `speed`, a speed parameter, and `veinCount`. It then resolves the current bound vein and derives `times` from `time / period`. When `miningRate > 0`, it accumulates `costFrac += miningRate * times`. If that batch fits in the vein amount, vanilla floors `costFrac` to obtain resource use and keeps the fractional remainder. If the batch crosses the amount, vanilla computes a reduced legal output count and adjusted fraction before decrementing the amount. Finally it updates the vein group, animation, low-amount minimum, product cache, and product register; normal exhaustion removal and notification follow only if amount reaches zero.

The protection transpiler preserves those production/statistics paths. It uses the effective count for production time, with cleanup-only time advancement at the physical count when no mineable veins remain but zero/invalid entries still need vanilla removal. It skips a selected vein at exactly amount 1 and changes only the amount budget in vanilla's capped-batch branch to `amount - 1`. Amount 0 remains eligible for vanilla cleanup. The UI transpiler replaces only the ordinary solid-vein `veinCount` multiplier.

The source inspection dump used during development is not shipped as a runtime dependency. Current signatures and the project reference copies are checked into this workspace's development tree.

## Current runtime patches

- `MinerComponent.InternalUpdate`: a prefix skips a miner tick when every bound solid vein is protected; a transpiler preserves the vanilla `costFrac` / batch-output path while excluding amount-1 nodes and capping final resource cost at `amount - 1`.
- `UIMinerWindow._OnUpdate` and `UIVeinCollectorPanel._OnUpdate`: replace the ordinary solid-miner speed multiplier with `GetEffectiveVeinCount`.
- `UIOptionWindow._OnOpen`: appends the protection toggle to the confirmed Game settings content.
- `UIReferenceSpeedTip.SetTip`: restores the captured vanilla layout before each call and adds read-only detail rows for supported solid vein items.

The plugin installs these patches individually and verifies each owner in Harmony's patch info. Missing target methods or patch installation failures are logged as errors. It does not use `PatchAll`.

## Reference statistics

The production window's `astroFilter` selects a planet, a star system, or the galaxy factory range. The read-only detail patch reads existing `FactoryProductionStat` product pools for the vanilla total, scans only the factories selected by that filter, and deduplicates protected vein IDs within each factory. Its estimate subtracts the lost contribution of eligible vein miners from the vanilla total; it does not write to factory statistics.

The extension is calculated when `UIReferenceSpeedTip.SetTip` runs. A generation-guarded one-frame coroutine reapplies the layout from the captured vanilla baseline. Ineligible items and disabled/zero-consumption states restore the original tooltip geometry.
