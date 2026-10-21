# Plant-cloth tents

Status: plan of record for new worlds (`SimulationConfig.PlantTentsEnabled = true`,
`NeolithicConfig.ClothPerLightHousing = 1.5`, both in `OpeningConfig.json` `Save15Rules`). Saves without the Save15
envelope load with the rule off and keep the earlier behaviour exactly (see `Docs/SettlingGrace.md` for the envelope).

## Problem

Tents (`CraftGood.LightHousing`) came only from Animal Fabric and were `HusbandryOnly`. Farming and fishing
settlements had only fixed Housing, which stays at its site, so a camp that had to move arrived without shelter: the
movement audit measured housing coverage 0.60 after a move against 0.86 when staying, followed by fission and
collapse.

## Rule

- A second route of the same Light Housing recipe: Builder, Construction track, the same rate and equipment as the
  hide tent, consuming `ClothPerLightHousing` plant Cloth per unit instead of Animal Fabric
  (`NeolithicProduction.PlantTentRecipe`). The good, its `LightHousingPeoplePerUnit` capacity (3, against 4 for a
  House) and its decay are unchanged; no new good, observation field or contract change.
- **Who.** `NeolithicProduction.PlantTentRoute`: the rule is on, the camp has no Husbandry (herding camps keep the
  hide route unchanged), it farms or fishes, and its Society holds Weaving and Construction.
  `NeolithicProduction.Recipes(world, camp)` returns the camp's recipe table with the tent on its route; every
  per-camp caller (resolver, forecast, DemandControl, the value planner's labour prices, social relocation, trade,
  goods storage) uses it.
- **Calibration.** One Cloth or one Animal Fabric each costs one food unit and the same Weaver time, and a hide tent
  takes one Animal Fabric. A plant tent takes 1.5 Cloth, so it is never cheaper in fibre, food or Weaver labour than a
  hide tent, and it stays inferior to a House (fewer people per unit, faster replacement).
- **Protected clothing first.** Tents use only Cloth beyond this turn's clothing consumption (residents x
  `FabricConsumptionPerPersonTurn`, less Animal Fabric held): `NeolithicProduction.TentClothReserve` in `Produce`,
  `MaterialEconomy.Forecast`, the tool-replacement estimator and trade capacity; `DemandControl` holds the same Cloth
  back (`freeTentCloth`) and prices any further Cloth as ordinary manufacturing, after the Protected clothing class.
- **Planner.** `DemandControl` treats tents as possible for plant-route camps; portable shelter replacement, a
  mobile camp's shelter deficit and a learned portable `shelterRoute` can now be met with Cloth (route feasibility
  checks Cloth stock and the plant envelope). The kernel shelter cost includes the woven Cloth. The value planner's
  labour price adds the Cloth's Weaver labour. `NeolithicProduction.OwnDemand` and the forecast's Cloth demand add
  the tent Cloth for plant-route camps.
- **Fission.** `Fission.DaughterShelter` lets a plant-route parent's daughter sew tents from her share of Cloth
  beyond her first-turn clothing; she always carries her share of existing tents.
- Production receipts record the Cloth a tent consumed in a transient `ProductionReceipt.Cloth`; the Cloth itself is
  conserved in the camp inventory.

## Validation

`Tools/Headless/SettlingGraceChecks.cs`: route gating (rule, Husbandry, Weaving), recipe identity, fibre calibration,
protected clothing in `Produce`, Cloth conservation, daughter shelter, a full DemandControl turn for a moved farming
camp against the rule-off twin (tents are made, clothing coverage is not lower, housing coverage is higher, Cloth is
conserved through weaving, tents and clothing), Save15 round trip and continuation, and the byte-identical rule-off
fingerprints.
