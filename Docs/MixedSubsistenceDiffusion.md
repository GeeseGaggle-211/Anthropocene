# Mixed subsistence integration

This branch extends the Elder-RL substrate with resident Fishing, bilateral technology knowledge, local Husbandry exposure and physical animal acquisition. Technologies expose capabilities; they do not select an agricultural, pastoral or fishing strategic identity. The accepted CurrentPlusExitOpportunity fission rule at threshold 0.05 remains unchanged.

## Fishing

The resident Fishing implementation was already present behind feature gates. New worlds now enable NeolithicFishingEnabled and CorrectedAquaticSeasonalPressureEnabled together, using DemandControl. This is the existing bounded three-ground, range-three implementation, with distance retention 0.75, local knowledge/snapshot filtering, sparse fishery capital, real Fishing Tool service and shared ecological resolution. We did not substitute the older source-sweep adapter or rewrite its calibration. Explicitly stored configurations remain authoritative; missing gates in older saves migrate disabled.

## Knowledge is not productive proficiency

Actual Hunting, Gathering and Fishing still accrue their existing practice. Trade and observational contact instead accrue Society.TechnologyExposure, a separate three-domain, saved knowledge ledger. Secondary progress combines post-breakthrough practice with this exposure. Neither channel grants a performance bonus merely for observing another society. Social exposure cannot replace activity for the first Paleolithic breakthrough.

Threshold crossings queue the ordinary Close-phase discovery path. A newly enabled activity becomes available to the next planning boundary; discovery does not replay OpeningTransitions or its free initial animals/improvements. Fission daughters deep-copy knowledge rather than sharing a mutable array. Older saves missing the exposure ledger load zeros.

## Bilateral trade knowledge

A realized settled exchange permits both societies to learn previously unknown opening technologies held by the counterparty. The requester learns from the responder and the responder from the requester. This applies to Cultivation, Animal Husbandry and Watercraft, independently of the first-breakthrough label or the specific commodity exchanged.

Each receiver's knowledge increment is its realized delivered-leg value multiplied by TradePracticePerValue, currently 0.1. A counterparty cannot teach a technology it does not hold. An undelivered leg supplies no knowledge, and knowledge does not change the economic production portfolio directly. The existing trade matching, escrow and material conservation remain authoritative.

This is knowledge exchange, NOT live-animal commerce. No livestock TradeGood, physical livestock transport or gift interface has been added. Barter valuation and the initial learning coefficient still require longer-run balance evaluation; passing correctness checks does not establish an optimal diffusion rate.

## Local Husbandry exposure

At the end boundary, live Neolithic settlements receive bounded exposure from domesticable animals within one hex. Wild animals provide ordinary familiarity; foreign managed animals receive the configured stronger demonstration weight; own managed animals do not reteach their owner. The same herd is counted once per society, even if multiple owned settlements can see it. Species familiarity is stored for acquisition. Paleo societies receive neither this secondary-technology exposure nor species-practice changes from this new channel.

The implementation indexes herds by tile and camps by society once, then performs local radius-one lookups. Cost follows active herds, camps and local contacts, not a society-by-all-herds Cartesian scan or a seasonal loop.

Initial tuning: 2.5 exposure points per simulated year at 20 weighted reference animals; foreign managed animals count 2x; species familiarity threshold 50; the ordinary secondary unlock threshold remains 225. These are configurable starting values, not an assertion of historical timing or final calibration. Boundary contact is a coarse turn-horizon approximation: it does not model each animal's residence time or introduce a new herd-attraction/luring behavior.

## Physical taming, including settlements without hunters

A live Neolithic settlement with Husbandry, sufficient species familiarity and an actual nearby wild herd can acquire animals at the economic end boundary. Hunter interactions remain valid opportunities, but a resident agricultural or fishing settlement does not need to recreate a hunter detachment merely to tame familiar nearby animals.

Capture transfers real living animals from the wild herd; it does not spawn a technology reward. It leaves the species' protected breeding reserve, conserves animal heads and proportional fractional birth/death progress, and commits after this horizon's production. Captured animals cannot generate managed products or breed retroactively. Claims are deduplicated and reconciled in canonical camp/herd order.

The first acquisition is a small bootstrap, capped by InitialDomesticAnimals (five) across the whole settlement, not five per species. Once a managed herd record exists, the existing retained-herd target also constrains refilling: a deliberately liquidated zero herd is not automatically restocked against the economic decision. This is a bounded bootstrap mechanism, not a complete future acquisition-investment controller. The economic allocator decides how much herd to retain and operate on subsequent turns.

## Resident cohort correction

The expanded integration exposed a pre-existing dispatch/automation mismatch: an accepted hunter dispatch reduced the population during Movement, but automatic economy planning had already staffed the original resident count. One matched seed consequently reached 29 assigned workers for 28 residents.

Automatic planning, resident Fishing access and draft reuse now use the accepted post-dispatch resident cohort. A transient, nonserialized PendingDispatchConsumers preview retains departing residents' meals until their real hunter entity exists. They cease to be village workers but do not cease to be food consumers. Rejected dispatches cannot shrink the cohort. This is an upstream planning correction, not a post-resolution workforce clamp or a changed fission threshold.

## Validation and scope

See MixedSubsistenceDiffusionValidation.md for exact tested commits, run IDs, complete-bank counts and results. Focused suites cover bilateral settled trade, knowledge/proficiency separation, nonlocal/no-technology/no-animal cases, resident taming without hunters, breeding reserves, liquidation, fractional conservation, persistence, collection-order independence and dispatch/draft/scalar-parallel parity. Existing Fishing, economic settlement and fission suites remain required.

Mixed capability counts are not evidence that all three production channels are actually active. World reporting must distinguish capabilities, resident workforce, realized production and livestock holdings. Five matched worlds are an initial regression screen, not proof of every ecological or strategic outcome. No RL weights, natural disasters or Copper Working gameplay are implemented by this change.
