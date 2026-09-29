# Handover: value-based fulfilment and trade-ready valuation

Written 29 September 2026 on `features/neolithic-agricultural` at the commit
that adds this file. Purpose: start a focused design session on how the
economic planner values goods, so that a value-based fulfilment rule can
replace the uniform rule and later carry trade and Elder/RL decisions. This
document is a starting point, not a contract. The design record is
[PlannerDirectFulfilmentPlan.md](PlannerDirectFulfilmentPlan.md); read its
"Decisions" section before proposing anything.

The value model draft is [ValueModelDesign.md](ValueModelDesign.md).
Its implementation plan is [ValueModelImplementationPlan.md](ValueModelImplementationPlan.md).

## Read first

1. `AGENTS.md` — engineering doctrine and repository contracts.
2. [PlannerDirectFulfilmentPlan.md](PlannerDirectFulfilmentPlan.md) —
   phases, measurements, the QoL value-fill analysis and the trade objection.
3. [EconomicAutomation.md](EconomicAutomation.md) — the automation contract
   the planner must keep (prior-cohort budgets, food first, tool rules).
4. [TurnResolutionPasses.md](TurnResolutionPasses.md) — the planner is an
   interturn pure step; the resolver never calls it.
5. Code: `Assets/_Anthropocene/Simulation/ProductionDemand.cs`
   (`CampProductionDemand`, `ProductionClaim`, `IFulfilmentRule`,
   `UniformFulfilment`) and `EconomicPlanner.cs` (`Plan`, `Evaluate`,
   `SearchPath`). QoL is computed in `NeolithicProduction.ResolveNeeds`.

## Where things stand

| Commit | What it did |
| --- | --- |
| `7120e11` | B3 phase 1: removed repeated planner forecasts (exact). |
| `8720984` | Phase 2: `CampProductionDemand` demand pass and `ProductionClaim` list (exact). |
| `6187fc7` | Phase 3: `IFulfilmentRule` with per-claim shares, `UniformFulfilment` default, `SearchPath` solve (unbiased vs a same-method control). |

- Each turn, `Plan` builds the demand pass once, then searches one monotone
  path `s ∈ [0, 1]`; the rule turns `s` into a share per claim and the exact
  staffing pass (`Evaluate`) returns the workers needed.
- `ProductionClaim.Value` exists and defaults to `Priority`; nothing uses it.
- Decision 2 (defer expansion Farm Tools) was measured, lowered QoL
  systematically and was dropped.
- Not done: unifying the planner, `ToolReplacementDemands` and
  `MaterialEconomy.Forecast` schedules with resolver production (changes
  authoritative results); Phase 4 contract documents.

## The design problem

Choose how a good gets its value so a rule can allocate scarce labor by value.
Requirements gathered so far:

- **Semi-dynamic scarcity (user).** A scarcer good is more valuable. Values
  are recomputed once per turn at the demand pass from the committed boundary,
  never mid-fulfilment.
- **QoL grounding (user proposal).** Value goods by how much they move quality
  of life, Amdahl-style. Current formula:
  `QoL = FoodMet × WaterMet × (½·clothing + ½·housing)`, with each need linear
  in coverage up to 1 and scaled by an expectation that ramps with settled
  years. Tools gain value when intensified agriculture makes labor scarce;
  amenities (luxury goods, not yet designed) follow later.
- **Trade (user objection).** Valuing goods only by local need coverage makes
  surplus worthless: a village would never build extra housing for a
  neighbor who needs it.

Current direction (amended in the plan doc): value **goods, not needs**, by
their best use — local consumption (marginal QoL of the need served, where the
scarcity curve applies), exchange (local value of what the good trades for,
net of transport, or a regional price later) and production input (derived
value through labor saved or food security). Local values double as
reservation prices. Trade demand enters the demand pass as export claims.

## Open questions

1. Scarcity curve for needs: form (the plan suggests
   `∂QoL/∂coverage × (1 − coverage)^γ`), bounds, and smoothing across turns.
2. Whether QoL itself should become concave (principled, needs
   recalibration) or stay linear with planner-only curves.
3. Shadow price of labor: expose it from the path solve and use it to value
   tools, Farm Tools and Herding Tools.
4. Pots: value through food security needs an expected-shortage estimate,
   which does not exist.
5. Durables: value housing and tools over their service life using existing
   decay rates.
6. Trade mechanics (owned by trade, not the planner): who sets exchange terms
   and when; speculative versus committed production; transport and supply
   range (#13); what a village may know of a neighbor under filtered
   observations; Elder policy (#23/#24) as the actor that sets priorities or
   values.
7. Rule shape: value-ordered fill over claims, how it maps to the monotone
   path `s`, and tie-breaking that stays deterministic.

## Related GitHub issues

- **#23 (Elder planning contract)** asks for a unified Elder utility
  contract. Value-based fulfilment is effectively its economic half; design
  the two together rather than as competing value systems. **#24** (pastoral
  candidates) consumes the same contract.
- **#33 findings, carried in #38**: under the normal policy, productive
  specialist work appeared in Toolmaking 21/21 natural windows, Weaving 17/21,
  Construction 4/21, Pottery 0/21 and Carpentry 0/21; Housing remains a major
  normal-policy failure despite high food satisfaction. Recipes are physically
  feasible (controlled witnesses), so this is an allocation/valuation
  question. A value rule should be judged against these numbers by rerunning
  the closed #33 harness, as #38 already directs for the Elder.
- **#37** (performance baselines) stays open until after the first Elder
  integration; the B3 measurements are pre-Elder evidence, not its
  completion.
- **#13** (logistics) and **#34** (contested-resource entitlement) bound the
  trade half: transport cost/range and who gets a shared source.

## Constraints to keep

- The engineering doctrine checklist in `AGENTS.md` for any new mechanic:
  authoritative state, temporal abstraction, scaling law, mature-game scale,
  RL implications and validation, stated before coding.
- The planner stays a pure interturn step with canonical ordering; no save
  fields unless deliberately designed; filtered observations only.
- Uniform fill remains the default until a value rule is chosen and
  calibrated.

## How to validate a planner change

- **Exact refactors:** per-turn `SaveCodec.Fingerprint` equality against the
  prior commit from `TestResults/Issues26-28-33-35/start-80-11.anthro`
  (scalar, four workers, and four workers with reversed collections and
  partitions), plus `start-80-1729` and `start-320-1729`; then
  `./Tools/Test-Headless.ps1 -Seeds 0` and
  `-Seeds 30 -Standard -Geography` with unchanged output.
- **Behaviour changes:** build the audit harness against baseline and
  candidate sources, run 25 post-opening turns from the four checkpoints, and
  compare with `Tools/PerformanceAudit/Compare-Drift.py`, together with a
  same-method control (for example the baseline with one more search
  iteration). Judge bias by the mean signed deviation; late per-turn QoL
  maxima reach 3.5–9.5% in the control alone because trajectories diverge.

```powershell
$sdk = 'C:/Program Files/Unity/Hub/Editor/6000.5.10f1/Editor/Data/DotNetSdk/dotnet.exe'
# Archive the baseline simulation into an ignored directory first (git archive), then:
& $sdk build Tools/PerformanceAudit/Anthropocene.PerformanceAudit.csproj -c Release -t:Rebuild -p:UseSharedCompilation=false "-p:SimulationSource=<baseline Simulation dir>" -o TestResults/Drift/audit-base
& $sdk build Tools/PerformanceAudit/Anthropocene.PerformanceAudit.csproj -c Release -t:Rebuild -p:UseSharedCompilation=false -o TestResults/Drift/audit-cand
foreach ($w in '80-11','80-23','80-1729','320-1729') { foreach ($v in 'base','cand') {
  & $sdk "TestResults/Drift/audit-$v/Anthropocene.PerformanceAudit.dll" "--load=TestResults/Issues26-28-33-35/start-$w.anthro" --production-config=1 --post-opening-turns=25 --warmup=1 --metrics=0 --details=0 --fingerprints=0 "--output=TestResults/Drift/run/$v-$w.json" } }
python Tools/PerformanceAudit/Compare-Drift.py TestResults/Drift/run TestResults/Drift/control
```

The checkpoints are ignored local files; regenerate them as described in
`Tools/PerformanceAudit/ForecastUnitsREADME.md` if absent.

## Housekeeping

- GitHub CLI authentication was repaired at handover (file-backed
  `hosts.yml`). Issue review found nothing to close: #37 and #38 stay open by
  their own terms, and the remaining issues are untouched feature work.
- Unity re-serializes whitespace in 29 MapArt `.meta` files and creates
  `ProjectSettings/PackageManagerSettings.asset` during EditMode runs; these
  were deliberately left uncommitted.
