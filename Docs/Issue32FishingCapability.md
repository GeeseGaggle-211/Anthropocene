# Issue #32: Fishing capability matrix

**Status:** design proposal on baseline `4db1fb6424fb1949c6b34c9f1a2f9b0a36f06122`.
The current contract index classifies first-breakthrough extensions and
Fishing-first capability as deferred. The route mappings below are proposed
for Issue #32; they are not a claim that later discoveries already run.

The proposal reuses the existing Fishing practice, Fish action, Watercraft
technology, and aquatic food stocks. It adds no separate Fishing economy,
recipe, occupation, fish-extraction path, fishery capital, saved field, or
observation schema. Any post-first-breakthrough routing depends on the shared
discovery rules in Issue #31 when those rules are available.

## Capability matrix

In this table, **current** describes the inspected baseline. **Proposed route**
describes the intended capability relationship for Issue #32. “Agriculture” is
the player-facing route implemented as `Technology.Cultivation`.

| Route | Current capability and food | Proposed route after the first breakthrough | Prerequisites and blockers |
| --- | --- | --- | --- |
| **Paleo-only** | Before the first breakthrough, camps can passively gather eligible aquatic food, and an established hunter can already submit a legal Fish order from within one hex of a visible aquatic tile. This shore-accessible Fishing does not require Watercraft. Hunters cannot enter Shallows without Watercraft. | Fishing practice remains the signal for a Fishing-first Watercraft breakthrough. This row does not grant boats or a fishery investment before that breakthrough. | Passive collection needs gatherers and an eligible tile within the camp's gathering radius. Active fishing needs a hunter, a visible aquatic target and adjacency after movement. Ocean is excluded. |
| **Agriculture-only** | A Gathering-led first breakthrough is `Technology.Cultivation`; it does not remove the shared passive or shore-accessible fishing hooks. The agriculture route itself adds no water movement. | If later discovery is enabled by #31, keep Fishing capability available as in the Paleo row. The exact reciprocal rule for discovering Watercraft after Agriculture belongs to #31. | At this baseline, the resolver grants only the first breakthrough. #31 is open, so no later Watercraft discovery is implemented here. |
| **Husbandry-only** | A Hunting-led first breakthrough is `Technology.AnimalHusbandry`; it also leaves shared passive and shore-accessible fishing available. Hunting remains a separate food receipt and practice domain. | If later discovery is enabled by #31, keep Fishing capability available as in the Paleo row. The exact reciprocal rule for discovering Watercraft after Husbandry belongs to #31. | At this baseline, the resolver grants only the first breakthrough. #31 is open, so no later Watercraft discovery is implemented here. |
| **Fishing/Watercraft-only** | A Fishing-led first breakthrough maps to `Technology.Watercraft`. The existing capability is hunter movement through eligible Shallows; it does not grant deep-ocean travel and does not make fishing itself newly legal. When opening compatibility effects are enabled and a suitable Shallows tile exists nearby, the transition may also place a `FisherySeed`. | With shared #31 progression available, ordinary Gathering practice can discover Agriculture/Cultivation, and Hunting practice can discover Animal Husbandry. This is the required Watercraft-first secondary route mapping; it adds no route-specific Fishing economy. | Watercraft enables hunter entry to Shallows. A Fish order still requires a visible aquatic target within one hex of the hunter's final tile and finite available aquatic food. The baseline has no shared second-discovery resolver. |
| **Mixed-capability routes** | Aquatic gathering and active Fishing can contribute food alongside terrestrial gathering, hunting, cultivation or animal products. These are distinct receipt categories over the existing food and ecology ledgers; they are not exclusive economy modes. | A society may combine Watercraft with Agriculture/Cultivation and/or Animal Husbandry through later activity-based discoveries. For a Watercraft-first society, use the Gathering-to-Agriculture and Hunting-to-Husbandry mapping above. Other secondary thresholds and reciprocal routes remain #31 decisions. | Mixed capability requires a later-discovery rule and must preserve the current pass order, finite aquatic stocks, deterministic receipts and filtered observations. The baseline implements no second discovery. |

## Existing fishing hooks

`Domain.Fishing` and `Technology.Watercraft` already exist side by side with
Hunting, Gathering, Animal Husbandry and Cultivation in the simulation model
([`Model.cs`](../Assets/_Anthropocene/Simulation/Model.cs#L9-L10)). The first
Paleolithic breakthrough selects the strongest of the three experience
domains; once the society enters the Neolithic, the current breakthrough
resolver returns without checking another technology
([`TurnResolver.cs`](../Assets/_Anthropocene/Simulation/TurnResolver.cs#L285-L307)).
The contract index therefore correctly marks later discovery and Fishing-first
capability as deferred ([`CurrentSimulationContract.md`](CurrentSimulationContract.md#L38)).

The legal Fish action is distinct from boat movement. A hunter may target a
visible freshwater or Shallows tile at distance one from its final position;
that validation does not require Watercraft. The movement rule separately
allows a hunter with Watercraft to enter Shallows and continues to exclude
deep ocean ([`ActionContract.cs`](../Assets/_Anthropocene/Simulation/ActionContract.cs#L36-L43),
[`Model.cs`](../Assets/_Anthropocene/Simulation/Model.cs#L475-L476)). Thus a
Paleolithic group can fish from shore, while Watercraft expands hunter
movement.

Passive aquatic food is gathered by camps using their shared gathering labor.
It is categorized as Marine gathering or Aquatic gathering, and ordinary
Ocean tiles are excluded from passive opening harvest
([`GenerationEconomy.cs`](../Assets/_Anthropocene/Simulation/GenerationEconomy.cs#L32-L35)).
Active Fish orders create the existing `Fishing` claim against the target
tile's aquatic food stock (`G`), not a separate fish inventory
([`GenerationalEconomy.cs`](../Assets/_Anthropocene/Simulation/GenerationalEconomy.cs#L215-L220)).
Both use the existing finite aquatic ecology and turn-horizon accounting
([`AquaticFoodWeb.md`](AquaticFoodWeb.md#depletion-resilience-and-practice),
[`TurnResolutionPasses.md`](TurnResolutionPasses.md#rules-protecting-the-pass-order)).

## Food provenance

The turn report already distinguishes terrestrial food, marine gathering,
lake gathering, hunting, active Fishing, animal products and foraging.
`FishFood` is the receipt from explicit Fish orders; passive marine and lake
receipts remain `MarineFood` and `LakeFood`
([`GenerationalEconomy.cs`](../Assets/_Anthropocene/Simulation/GenerationalEconomy.cs#L968-L972)).
Passive aquatic receipts award the configured share of practice to Fishing and
the remainder to Gathering; active Fishing orders award Fishing practice
([`GenerationalEconomy.cs`](../Assets/_Anthropocene/Simulation/GenerationalEconomy.cs#L985-L1019)).
These experience credits do not create additional food. The aquatic stock is
shared, finite food and biomass with the saved seasonal recovery profile;
unmet requests do not create catch ([`AquaticFoodWeb.md`](AquaticFoodWeb.md#depletion-resilience-and-practice)).

## FisherySeed versus persistent investment

The existing `FisherySeed` is an opening compatibility effect, not an operated
fishery system. When the Watercraft breakthrough occurs and
`OpeningCompatibilityEffects` is enabled, `OpeningTransitions.Apply` chooses
an eligible Shallows tile in the camp's radius-one area, adds a
`FisherySeed` improvement, and marks the tile as a Fishery. If no suitable
Shallows tile exists, it adds no seed. The current opening configuration sets
the compatibility multiplier to `1.4`; the multiplier scales the tile's food
and biomass capacities while the compatibility flag and Fishery marker apply
([`Societies.cs`](../Assets/_Anthropocene/Simulation/Societies.cs#L89-L112),
[`LazyEcology.cs`](../Assets/_Anthropocene/Simulation/LazyEcology.cs#L17-L25),
[`OpeningConfig.json`](../Assets/_Anthropocene/Resources/OpeningConfig.json#L363-L366)).
This seed supplies an immediate opening reward. It does not define a repeated
investment, an occupation, a cost, a work assignment or an independent
production stream.

A persistent fishery investment remains unconfigured. Defining one would first
need an explicit rule for eligible tiles and ownership, its material/food/labor
cost and duration, the capacity or recovery term it changes, and how extraction
and recovery preserve the shared aquatic stock ledger. Any later persistent
state or player-visible investment control would also need an explicit save and
observation decision. The historical `DesignSpec.txt` mentions Fisheries that
may increase aquatic capacity or regeneration, but that file is explicitly a
historical source and does not authorize those effects today
([`DesignSpec.txt`](DesignSpec.txt#L167-L169),
[`CurrentSimulationContract.md`](CurrentSimulationContract.md#authority-classes)).

## Prerequisites and blockers

- **Later-discovery gate:** #31 owns shared subsequent-discovery eligibility
  and thresholds. The baseline supports the first breakthrough only. The
  Watercraft-first secondary mapping in this proposal should be wired through
  #31 when that shared mechanism is available, rather than introducing a
  second Fishing-specific progression path.
- **Existing food access:** camp gathering needs an eligible aquatic tile in
  gathering range and available labor. Active fishing needs a living hunter,
  an eligible visible freshwater or Shallows tile, and adjacency after
  movement. The actual receipt is bounded by finite aquatic `G` over one
  analytical turn horizon.
- **Watercraft movement:** only hunters with Watercraft may enter Shallows;
  the current traversal contract does not open deep-ocean travel. Fishing from
  an adjacent accessible tile remains possible without Watercraft.
- **Opening seed:** the compatibility seed requires the opening-effects
  setting and a suitable nearby Shallows tile. Its `1.4` multiplier is
  current compatibility tuning, not the design of persistent fisheries.
- **Architecture:** any future fishery work must preserve the authoritative
  one-turn transition and shared finite resource ledger in
  [`TurnResolutionPasses.md`](TurnResolutionPasses.md). No new fishing state
  or observation field is part of this proposal.

`AquaticFoodWeb.md` remains the source for aquatic production profiles,
accessibility, renewal and ecological limits. This document only maps those
existing capabilities onto the five requested progression routes.
