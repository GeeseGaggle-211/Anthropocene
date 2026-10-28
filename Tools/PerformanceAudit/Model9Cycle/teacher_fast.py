"""Slim converted-teacher loop: labor_bc._teacher_transition without actor tensors (labels/execution identical)."""
import json, sys, time
from pathlib import Path
import torch
from anthropocene_rl.autonomous_v7.labor_bc import LaborResetOptions, shadow_teacher_labels, _retained_action, _idle_retry
from anthropocene_rl.autonomous_v7.labor_bridge import LaborModel9Bridge, labor_observation_fields
from anthropocene_rl.autonomous_v7.labor_ppo import execute_labor_decision
from anthropocene_rl.autonomous_v7.bridge import Model9Bridge

world, turns, out = int(sys.argv[1]), int(sys.argv[2]), Path(sys.argv[3])
options = LaborResetOptions(players=16, movement=True, withheld=0, width=128, height=64, separation=9)
t0 = time.time()
with LaborModel9Bridge() as lb, Model9Bridge(economic_diagnostics=True) as hb, out.open("w") as f:
    step = lb.reset(world, **options.reset_kwargs())
    for i in range(turns):
        live = [v for v in step["villages"] if v.get("observation") is not None and not v.get("terminated")]
        if not live: break
        catalogs = {}
        contexts = []
        for v in live:
            cat, _ = labor_observation_fields(v["observation"]); catalogs[cat.camp_id] = cat; contexts.append((cat,))
        turn = int(step["turn"])
        h_step, labels = shadow_teacher_labels(lb, hb, world, options, live, contexts)
        retained = {int(v["camp"]): _retained_action(l, c[0], torch.zeros((1, 1), dtype=torch.bool)) for v, l, c in zip(live, labels, contexts)}
        nxt = execute_labor_decision(lb, catalogs, retained, _idle_retry).step
        lab = {int(v["camp"]): l for v, l in zip(live, labels)}
        hv = {int(h["camp"]): h for h in h_step["villages"]}
        for v in nxt["villages"]:
            c = int(v["camp"]); l = lab.get(c); h = hv.get(c, {})
            f.write(json.dumps({"turn": turn, "camp": c, "p0": v["priorPopulation"], "pop": v["population"], "cloth": v["clothing"],
                "qol": v["qol"], "years": v["years"], "terminated": v["terminated"], "obs": v.get("observation") is not None,
                "nutrition": v["nutrition"], "spec": l.action["laborSpecialtyAllocation"] if l is not None else None,
                "hcloth": h.get("clothing"), "hpop": h.get("population")}) + "\n")
        f.flush(); step = nxt
        print(f"turn {turn} live {len(live)} t={time.time()-t0:.0f}s", flush=True)
