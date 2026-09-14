# Aquatic food web — generator 11

Water uses the existing total biomass B, human-harvestable food G, seasonal renewal rates and aquatic condition. Vegetation tiers on water mean plankton/algae abundance, not trees. This is an aggregate fishery model, not individually simulated plankton, fish populations or nutrient transport. No new per-tile objects, fields or scans are introduced.

## Generated profiles

`AquaticEcology` keeps the seeded regional variation, seasonal light/cold limits, cool marine fertility and separate lake warmth/catchment response described in EcologyFoundation.md. Their product is P. Ocean now uses the marine P rather than the former near-zero placeholder. Soil moisture does not determine aquatic production.

| Profile | Natural plankton/algae | Human-harvestable share G/B | Food capacity G | Biomass capacity B | Renewal fraction per BaseDays before P |
| --- | --- | --- | --- | --- | --- |
| Ordinary Shallows | High | 50% | 90P | 180P | 0.65 |
| Deep Ocean | Low | 10% | 90P | 900P | 0.08 |
| Freshwater Lake | Medium | 50% | 90P | 180P | Existing FoodRegeneration configuration |

Fractions are converted to daily exponential rates with `DailyRate(fraction, BaseDays)`, then multiplied by P. Food and non-harvestable biomass use the same rate, preserving the coupled pool equations and conservation. The 50% and 10% values are biomass accessibility fractions, not renewal percentages or permission to harvest Ocean. Ocean remains excluded from Paleolithic gathering, fishing and viable-start food, regardless of its larger biomass. Marine production never provides freshwater. Lakes keep their finite freshwater and distinct climate response.

## Upwelling proxy

For a Shallows tile, `u = min(1, adjacent Ocean count / 2)`. Hex adjacency wraps longitude. One ocean neighbor supplies half the bonus; two or more supply all of it. Lakes and Ocean itself receive no shallow-upwelling bonus. This is a coastal deep-water contact proxy, not simulated wind-driven currents, moving nutrients or depletion of an ocean reservoir.

Shallows food/biomass capacities are multiplied by `1 + 4u`; their daily renewal rate by `1 + u`. Thus full upwelling combines deep-ocean-scale biomass (900P) with 50% accessibility (450P) and twice normal shallow renewal. Its conservative recurring food potential is ten times that of the same ordinary shallow profile. Light and cold still limit both capacity and renewal. The constants are provisional gameplay tuning in AquaticEcology.cs, not physical calibration.

## Depletion, resilience and practice

Fishing and gathering debit finite G. Replenishment proceeds through the existing sparse seasonal integrator; unmet requests do not damage ecology. Realized extraction is compared with conservative renewable total biomass, as on land. The inaccessible reserve means fish extraction no longer treats every harvested unit as the entire ecosystem's biomass pool.

For generator 11 aquatic tiles, condition recovery per day is `0.1 * mean(seasonal BiomassRecoveryPerDay)`. It depends on stored biological turnover, independent of terrestrial vegetation. Ocean recovers more slowly; shallows and ocean-fed fisheries recover faster. Aquatic condition scales food/biomass capacities; current plankton tier follows that condition through existing vegetation hysteresis. The tier does not apply another productivity multiplier. Quiet projection remains pure, materialization commits it, and low/frozen seasonal turnover still slows recovery. There is no fishery immunity to depletion or a guaranteed harvest independent of demand.

The starting workforce and shared land/water labor ceiling still limit passive gathering. Viability consumes the final seasonal human-food profiles, excludes inaccessible biomass/Ocean, and checks freshwater every season. Increased aquatic potential can change eligible start sites and starting tribe cohorts even when seeds and terrain are unchanged. Presets must be revalidated after tuning.

Experience remains 50/50 Gathering/Fishing for passive aquatic food and 1.5x original successful-hunt experience. Active Fishing's practice weighting is unchanged. Changes to technology shares arise from ecology and opportunity, not player-ID quotas.

## Persistence and presentation

Generator version is 11. World/config version 7, observation schema 7, signature ANTHROPOCENE-8 and opening-v7.anthro are unchanged: the existing serialized capacities, rates, condition and vegetation tiers contain the complete continuation state. Upwelling forcing is baked into those profiles. Save/load never recalculates it. Generator 7–10 saves retain their exact stored profiles and original condition-recovery behavior; the new aquatic recovery path is gated by generation version. Future generator versions are rejected.

Filtered tile details label water vegetation as PLANKTON / ALGAE and the wider pool as AQUATIC BIOMASS, show the stored harvestable share, and omit soil moisture. Water never draws the land tree-count symbols. The explicit developer atlas labels aquatic tiers as plankton/algae; its food layer uses the stored seasonal profiles. No hidden ocean adjacency is exposed through player details.

## Verification

AquaticChecks covers 50%/10% shares, High/Low plankton, relative ocean/shallow turnover, graded ocean contact, longitude wrapping, lake exclusion, tenfold upwelling potential, sustained-demand resilience, food/biomass conservation, pure damaged-fishery recovery, aquatic hysteresis, saved profiles and legacy continuation. Existing start checks cover all-season food, shared labor, freshwater and Ocean exclusion. LayeredMapTests checks aquatic labels, accessibility and the absence of land trees at every detail zoom. See Validation.md for run results and measured balance limitations.
