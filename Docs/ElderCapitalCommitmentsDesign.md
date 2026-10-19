# Elder capital commitments: multi-turn capital decisions (design for review)

Status: proposal, 18 October 2026. Approved in principle: saving a commitment and extending saves is acceptable, and
save compatibility is a convenience, not a requirement. Nothing is implemented yet.

## Problem

The learned Elder re-chooses every control every 20-year turn, including the slow capital decisions:
- field retention and field development;
- herd retention and herd development;
- clothing and shelter routes.

Their payoff arrives several turns later: a herd breeds, field capital builds, shelter changes the housing stock.
Under PPO each turn's choice is credited only through noisy per-turn advantages.

Evidence:
- The mixed-use oracle (`--model9-oracle`) shows fixed allocations beating the heuristic by 1-11% in 5 of 6 worlds.
  The learned policy captures about 3.6%.
- Whole-world welfare spreads about 25% across allocation choices, and per-seed outcomes diverge by about 9%, so
  the per-turn signal is weak against the noise.

## Idea

Let the Elder commit a capital decision for a span of turns, then hold it. Holding is an option in the
reinforcement-learning sense:
- One decision covers several turns of consequences.
- The policy learns from the value of the whole span.
- The world sees steady capital policy instead of turn-to-turn churn.

Short-cycle controls are not committed and stay per-turn: service levels, the food and growth buckets, fishing
staffing.

## Authoritative state (saved)

`ElderInstitutionMemory` moves to schema 2 and gains one commitment record per village:
- the committed values: retained field operation, retained herd fraction, development budget, field development
  share, and the clothing and shelter routes;
- the turn it was set and its span in turns (1-6);
- the remaining turns.

Schema-1 memories load with no commitment, which means per-turn behaviour.

## Temporal abstraction

At each turn boundary a village either holds its commitment, with no new strategic or route draw, or makes a new
one. A commitment ends when its span runs out, when the Elder breaks it (an explicit action, at a cost), or on a
forced break. Forced breaks happen at:
- fission, for both parent and daughter;
- relocation;
- a capability change (a new technology);
- a survival shortfall, where the existing distress rules already override.

No sub-turn state exists and no new clock is added. The span is counted in turns, so a shorter turn length means a
proportionally shorter commitment in years. If the turn length becomes configurable, revisit the span in years.

## Action and observation

- **Action.** A categorical span head (hold, or commit for 1, 2, 3, 4 or 6 turns) is added to the directive.
  Holding re-emits the stored values, so the compiler and planner see an ordinary directive and stay unchanged.
- **Observation.** The commitment's values, its age and remaining turns, whether it was forced to break, and the
  realized-versus-committed gap (the existing `ElderPreviousDecisionObservation` covers part of this).
- **Teacher labels.** The heuristic is represented as "commit for 1 turn", which reproduces it exactly, so teacher
  fidelity holds and stage 1 can clone it.

## Learner

- **Semi-Markov PPO.**
  - A held turn draws no new action, so it adds no log-probability and no entropy.
  - The decision's advantage is the discounted reward over its span plus the bootstrapped value at the next
    decision. GAE runs over decision points, not turns, and the discount already follows elapsed years.
- **Breaking cost.** A small cost on voluntary breaks makes commitment the default without forbidding change. It is
  the same "slight penalty" principle as the mix-change cost, but on a decision the policy can actually see.
- **Critic.** It predicts value at decision points. The privileged inputs are unchanged.

## Scaling

- The cost is O(villages) per turn for storage and observation. Planning is unchanged, because a hold re-emits a
  directive and the planner compiles it as before.
- Decisions per world fall, which saves learner compute. Simulator cost is unchanged.
- With the median commitment near 3 turns, policy samples drop about threefold while the same simulated years cover
  more consequences per sample. That should help sample efficiency at RL-training scale.

## Validation

1. **Teacher fidelity:** commit-for-1 everywhere reproduces the heuristic exactly (fingerprints).
2. **Save/load:**
   - schema-2 memories round-trip;
   - schema-1 loads with no commitment;
   - continuation fingerprints match.
3. **Forced breaks:** fission, relocation, new technology and survival shortfall each end a commitment, with a logged
   reason.
4. **Learning test:** stage 2 with commitments against stage 2 without, on 3 seeds each, gated per regime. A
   commitment policy that beats the shared-head baseline beyond seed noise is the result we want. Then rerun stage 3
   with free routes, now committed: if committed routes no longer cost default worlds, this was the cause.

## Part 2: activity tokens (the scalable allocation action)

Contract 8's economy mix has exactly three outputs (cultivation, herding, fishing), so every new activity needs a
new head and a contract change. As mining, smelting, crafts and trade arrive, the allocation must instead scale with
the number of activities, not the network's shape.

### Tokens

Each economic activity a village can staff is a row in a new `activities` set, padded to at most K = 16 rows with
presence masks. A row carries:
- **kind:** a stable activity identifier, used as a learned type embedding, such as cultivation, herding, fishing,
  and later mine, smelter or trade route;
- **marginal return:** forecast output per added worker-year, from the planner's existing development bands
  (field, herd and fishing bands today);
- **capacity:** the workers the activity can absorb, and the current staffing;
- **capital at stake:** retained fields, herd, tools or sites that would be lost if the activity were abandoned;
- **input readiness:** whether its inputs (feedstock, tools, materials) are available;
- **service fed:** a small vector of which services it supplies (food, textiles, tools, materials, trade value) and
  the current gap in each;
- **legality:** a technology and capability mask.

All of it comes from the filtered observation and the planner's own forecasts; no hidden state is exposed.

### Allocation head

The village query (from the trunk) attends over the activity rows. The attention logits, masked to legal rows plus
one "unassigned" row, are the allocation:
- the softmax gives the shares of discretionary labour (the Development budget today);
- PPO samples it as an additive logistic-normal over the present rows, the same family as the contract-8 mix head;
- log-probabilities stay exact.

The planner turns the shares into whole-worker staffing through the request and bundle machinery it already uses.

### Relationship to what exists

- **Contract 8 is the special case** with rows for cultivation, herding and fishing. A warm start initialises the
  query so the three-row case reproduces the trained mix head; stage-2 policies carry over.
- **Commitments (Part 1):** a commitment stores the allocation vector over activity kinds, plus retention for
  activities with capital. Kinds are stable identifiers, so a held commitment survives rows appearing or
  disappearing. A new legal activity forces no break; the policy may re-commit to include it.
- **Expert mixture** (optional, earlier proposal): if tested, it would condition the query, not replace the
  tokens.

### Supply chains

An intermediate activity (smelting, cloth weaving) is valuable only through what it feeds. Its row's "service fed"
and gap features expose that demand, and the planner's bundles already chain inputs (feedstock to fabric to
tents). The policy chooses where labour goes; the planner keeps ensuring each chain is complete and feasible. Long
chains therefore need no new learner structure, only rows.

### Trade-dependent activities (requirement, raised in review 19 October 2026)

A mining or service economy feeds itself through trade. If trade is not valued, the policy sees only workers taken
off food, and the specialisation starves. Four requirements before such activities are added:

1. **Rows carry trade-adjusted value.** An activity's marginal return includes the expected food and goods its
   output buys from known partners, at the existing `Trade.Value` curves and the remembered contact prices, not
   only its direct output. Without a partner, the trade value is zero and the row says so.
2. **The planner counts contracted imports as supply.** Food arriving through agreed trade must count toward
   Survival and the reserve in planning. Otherwise the Survival class refuses the labour shift and no learned
   policy can specialise, however it is rewarded. Today trade is AI-to-AI only, with no Elder order kinds or
   observation fields, so this needs simulator work.
3. **Reward credits both sides of an exchange.** Gains from trade show up in the partner's welfare as well as the
   specialist's. A reward that sees only the specialist's lineage under-values exchange. The vector critic
   (Part 4) gets a trade or production component, and lineage credit should include realized gains for the
   partner (`RealizedTradeUtility` is already tracked in Elder memory).
4. **The curriculum has the regime.** Add scanned fixtures of resource-rich, food-poor sites with a reachable food
   surplus partner, gated per regime like the herding, farming and fishing fixtures. Also add the failure case:
   the same site with no partner, where specialisation should not happen.

Validation: on those fixtures, a trade-valued policy specialises and improves combined specialist-plus-partner
welfare against a self-sufficient baseline, without starvation. Without a partner, it stays self-sufficient.

### Scaling

- **Learner:** attention is O(K) per village; K = 16 is negligible next to the trunk.
- **Simulator:** one marginal-return forecast per present activity. Today's three bands are already computed;
  each new activity adds its own band forecast, which should be measured as it is added.
- **Contract:** adding an activity kind is a new kind identifier plus its features, with no change to the network's
  shape. The contract version bumps only when the row's feature layout changes.

### Validation

1. The three-row case reproduces contract-8 behaviour: teacher labels map exactly, and fingerprints match with the
   heuristic as actions.
2. A masked dummy row changes nothing.
3. A benchmark at K = 16 rows covers observation build time and learner step time.
4. Warm start: a stage-2 policy loaded into the token head gates within noise of the original.

### Phasing

Both parts are contract changes, so they ship together with the contract-9 movement design
(`ElderModel9Contract9MovementDesign.md`) as one contract-9 schema:
1. activity rows and the token allocation head, with three kinds;
2. commitments over token allocations;
3. destination intents.

The next supply chain is then added as rows, not as a contract redesign.

## Part 3: expert mixture with an attention router (scalability option, kept for later)

Proposed by the user on 18 October 2026, and kept deliberately as a scalability path rather than adopted now.

### Architecture

- **Encoder:** the shared encoder (set encoders and trunk) produces the village context h.
- **Experts:** N expert FFNs each map h to a plan embedding z_i. Experts are not hard-coded to economies;
  specialisation is learned.
- **Router:**
  - the query is q = W_q h and the keys are k_i = W_k z_i;
  - the weights are alpha_i = softmax(q . k_i / sqrt(d) / tau), with a learned temperature tau;
  - the combined plan is the sum of alpha_i z_i.

  Mixing happens in embedding space, not between finished plans, so hybrid settlements are natural: sharp weights
  pick an expert, soft weights blend them.
- **Decoders:** the existing Gaussian and categorical action decoders read the combined plan, so PPO
  log-probabilities stay exact and sampling happens after mixing.
- **Stability:** a load-balancing loss and a small router-entropy bonus prevent collapse onto one expert.
- **Warm start:** experts begin as zero-change copies with a uniform router, which reproduces the shared-head policy
  exactly (as the pilot's adapters did).
- **With activity tokens (Part 2):** the expert mixture conditions the village query that attends over activity
  rows. Tokens scale the action to more activities; experts scale the policy's capacity to more regimes.

### Evidence so far

- The pilot's capability-gated adapters (`--experts`, a fixed router given by the economy mask) beat shared heads in
  stage 3 by 1.5-3%. That is one seed, against evaluation noise of about ±3%, so it is suggestive, not established.
- Stage 2 with shared heads and regime balancing already passes every regime gate, so the expert mixture is not
  needed for today's three economies.

### When to adopt

Adopt it when shared heads stop covering the regimes. The signals are:
- a regime regressing beyond seed noise despite balancing;
- many activity kinds with distinct operating styles (mining, crafts, trade);
- a measured capacity limit, where a larger trunk helps and balancing does not.

Before adopting, test a learned router (4-6 experts) against capability-gated adapters and against shared heads,
over at least 3 seeds each, gated per regime.

### Costs and risks

- **Compute:** negligible next to the simulator.
- **Data:** each expert sees only part of the data. At about 75 local decisions per second this is the binding
  cost; at cluster scale it matters less.
- **Router collapse and dead experts:** handled with load balancing, and monitored through per-regime router weights,
  which also aid interpretability.

## Part 4: vector-valued critic (an extensible objective)

Lineage welfare (population x QoL x years) is an interim reward. The objective is endogenous scale-up of a
civilization with complexity and interdependence. Later components are expected to include production, resource
output, trade and military strength.

- **Critic:** one value head per objective component (population-years, QoL-weighted population-years, and later
  production, specialisation and trade measures). Each is trained on its own return with the same discount.
- **Advantage:** a configurable weighted combination of the component advantages. Today's weights reproduce welfare
  exactly, so training is unchanged until the weights change.
- **Scaling:** a new objective component is a new head, a new reward column and a weight. There is no
  re-architecture and no retraining from scratch: existing heads keep their fit and only the new head starts cold.
- **Validation:**
  - with today's weights, advantages match the scalar critic within tolerance;
  - each head's explained variance is reported per regime;
  - weights are recorded in the checkpoint and the gate report.

## Rejected

- **A fixed head per activity:** it does not scale, and every new activity is a contract and network change.
- **Fixed decision cadence for everyone** (for example, every third turn): it ignores state and forces stale choices
  in a crisis.
- **Longer turns:** that changes game semantics.
- **Value-target tricks without commitment** (n-step returns, a larger lambda): cheaper, but they don't reduce the
  number of noisy decisions. Worth trying as a control, not as the fix.
