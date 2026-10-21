# model9 specialist share (reporting only)

Status: implemented 21 October 2026 on `feature/r3-specialist-share`. Not a contract bump: nothing is observed,
rewarded or saved, and fingerprints are unchanged. The learner only reports it.

## Definition (user, 21 October 2026)

Specialist share is the share of residents producing goods, not food, weighted by realised goods output rather than
by role headcount.

```
SpecialistShare = sum_receipts(WorkerIntervals * BaseDays / DaysPerYear) / (PriorPopulation * turn years), clamped to [0, 1]
GoodsOutput     = sum_receipts(Output), excluding HousingMaterial
```

The sums run over the turn's `TurnRecord.Production` receipts (`ProductionReceipt`) of the village with `Output > 0`.
`PriorPopulation` is the village's residents at the start of the turn (the cohort whose labour the turn locked).

- **Counts.** Every goods recipe in `NeolithicProduction.Recipes`, whichever role ran it:
  - craft: Hand Tools, Tools, Pots, Cloth, Animal Fabric woven from animal produce, Farm, Herding and Fishing Tools;
  - construction: Housing Material, fixed Housing, portable (light) housing.
  
  A Craftsperson counts when it makes something. Trade and amenities join when they write goods receipts.
- **Does not count.** Food acquisition (gathering, farming, herding, fishing, hunting), herd yields and raw-material
  extraction (Woodcutter, Stone Quarrier, Clay Miner, recorded as `MaterialReceipt`s). None of these writes a
  `ProductionReceipt`.
- **Realised, not nominal.** A specialist staffed but producing nothing has no receipt, or one with zero output, and
  contributes zero.
- **GoodsOutput unit.** The summed receipt `Output`, in each good's native unit: one tool, one pot, one cloth or
  fabric unit, one housing unit. Housing Material is an intermediate consumed by Housing assembly, so it is excluded
  from `GoodsOutput`; its labour still counts in the share.

Animal Fabric is the Weaver's processing of animal produce into fabric, so it is craft labour; the herd that yields
the produce is not counted.

## Where

- `ElderSpecialistShare.Measure` (`ElderModel9.cs`) computes the share as a pure read of the committed turn record. It
  is called at the village-row point in `ElderModel9Environment.Advance`.
- `ElderModel9Village.SpecialistShare` and `GoodsOutput` hold the result.
- The RL bridge (`integration/Model9BridgeProgram.cs`, `Encode`) emits them as `specialistShare` and `goodsOutput`.

**Cost.** O(receipts of the turn) per village, with no authoritative state, fingerprint or observation change.

## Checks (`ElderModel9Checks.SpecialistShareChecks`)

- With no receipts, or a receipt with zero output, the share is zero.
- In a synthetic record, two specialist lifetimes among twenty residents give 0.1, and the intermediate is excluded
  from output.
- The share is clamped to one.
- Over four live seed-17 turns:
  - every share is in [0, 1];
  - a share is positive exactly when the village's crafts produced;
  - fingerprints match a run with reporting off.
- In a food-only fixture (every recipe rate zero or negligible on the live world), every village reports zero.
