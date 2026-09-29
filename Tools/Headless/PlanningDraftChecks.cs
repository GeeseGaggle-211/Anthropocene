using System;
using System.Collections.Generic;
using System.Linq;
using Anthropocene.Simulation;

// WP1: a planning draft computed on the committed world must never change an
// authoritative outcome. Owns its worlds and counters (lanes run concurrently).
internal static class PlanningDraftChecks
{
    private static int checks;
    private static void Check(bool ok, string message)
    { checks++; if (!ok) throw new Exception("Planning draft: " + message); }

    private static string Describe(PreparedTurn p)
        => string.Join("|", p.Orders.Select(o => o.ToString())) + "#" + string.Join("|", p.OrderReceipts) + "#" + string.Join("|", p.Rejections) +
           "#" + string.Join(",", p.AutomatedCamps.OrderBy(n => n)) + "#" + string.Join(",", p.AbandoningVillages.OrderBy(n => n));

    private static Dictionary<int, List<Order>> Merge(PlanningDraft draft, PlanningResult full)
    {
        var orders = draft.Ai.Orders.ToDictionary(p => p.Key, p => p.Value.Select(o => o.Copy()).ToList());
        orders[0] = full.Orders[0].Select(o => o.Copy()).ToList();
        return orders;
    }

    private static List<DecisionTrace> MergeTraces(PlanningDraft draft, PlanningResult full)
        => draft.Ai.Traces.Where(t => t.Player != 0).Concat(full.Traces.Where(t => t.Player == 0)).OrderBy(t => t.Player).ToList();

    private static Dictionary<int, List<Order>> Clone(Dictionary<int, List<Order>> orders)
        => orders.ToDictionary(p => p.Key, p => p.Value.Select(o => o.Copy()).ToList());

    // Replace a Move by a Wait, or a Wait by a legal adjacent Move, for one
    // camp so its effective tile changes relative to the draft's assumption.
    private static bool Perturb(WorldState w, Dictionary<int, List<Order>> orders, PlanningDraft draft, int mode, out int campId)
    {
        campId = -1;
        var candidates = w.Camps.Where(c => c.Population > 0).OrderBy(c => draft.Intents.ContainsKey(c.Id) ? 0 : 1).ThenBy(c => c.Player == 0 ? 1 : 0).ThenBy(c => c.Id).ToArray();
        var index = new PlanningIndex(w);
        foreach (var camp in candidates)
        {
            if (!orders.TryGetValue(camp.Player, out var list)) continue;
            var existing = list.FirstOrDefault(o => o.Entity == camp.Id && o.Kind != OrderKind.Economy);
            Order replacement;
            if (existing != null && existing.Kind == OrderKind.Move) replacement = new Order(OrderKind.NoOp, camp.Id);
            else
            {
                var observation = new AgentObservationBuilder().Build(w, camp.Player, index, includeCandidateForecasts: false);
                var mask = LegalActions.Build(observation).FirstOrDefault(m => m.Entity == camp.Id);
                if (mask == null) continue;
                var moves = mask.Actions.Where(a => a.Kind == OrderKind.Move && a.Destination != camp.Tile).ToArray();
                if (moves.Length == 0) continue;
                var near = moves.Where(a => w.Map.Distance(camp.Tile, a.Destination) == 1).ToArray();
                var pool = near.Length > 0 ? near : moves;
                replacement = pool[(mode * 7) % pool.Length].Copy();
            }
            // Keep the AI's automation request so the camp stays automated.
            replacement.RequestAutomaticEconomy = list.Any(o => o.Entity == camp.Id && o.RequestAutomaticEconomy);
            list.RemoveAll(o => o.Entity == camp.Id && o.Kind != OrderKind.Economy);
            list.Add(replacement);
            campId = camp.Id;
            return true;
        }
        return false;
    }

    public static void Run()
    {
        checks = 0;
        var reference = new WorldGenerator().Generate(53, SimulationConfig.Small(4));
        var workers = new[] { new PhaseExecution(), new PhaseExecution(4, 3, true, 1) };
        var worlds = workers.Select(x => SaveCodec.Load(SaveCodec.Save(reference))).ToArray();
        int turns = 0, postOpeningTurns = 0, invalidationTurns = 0;
        long reused = 0, recomputed = 0, invalidationRecomputed = 0;
        PlanningDraft previous = null;
        while ((!reference.Finished && turns < 12) || postOpeningTurns < 25)
        {
            if (turns >= 12 && !reference.Finished) throw new Exception("Planning draft opening did not finish within 12 turns.");
            if (reference.Finished)
            {
                if (postOpeningTurns == 0)
                {
                    double offset = .37 * reference.Config.BaseDays;
                    reference.Days += offset;
                    foreach (var world in worlds) world.Days += offset;
                }
                postOpeningTurns++;
            }
            var plan = AiPlanning.Decide(reference); new TurnResolver().Resolve(reference, plan.Orders, plan.Traces);
            var drafts = new PlanningDraft[worlds.Length];
            for (int i = 0; i < worlds.Length; i++)
            {
                var w = worlds[i]; w.Camps.Reverse(); w.Hunters.Reverse(); w.Herds.Reverse();
                byte[] before = SaveCodec.Save(w);
                var draft = TurnPlanner.Draft(w, 0, workers[i]);
                Check(before.SequenceEqual(SaveCodec.Save(w)), "Draft changed saved world bytes on turn " + turns);
                Check(draft.Matches(w) && draft.Turn == w.Turn && draft.ExcludedPlayer == 0, "Draft stamp on turn " + turns);
                Check(!draft.Ai.Orders.ContainsKey(0), "Draft planned for the excluded player.");
                drafts[i] = draft;
                var full = AiPlanning.Decide(w, workers[i]);
                var orders = Merge(draft, full); var traces = MergeTraces(draft, full);

                // Stale drafts: previous turn (same world object) and another world.
                var stale = i == 0 ? previous : drafts[0];
                if (stale != null)
                {
                    var without = TurnPlanner.Prepare(w, Clone(orders), traces, workers[i]);
                    var withStale = TurnPlanner.Prepare(w, Clone(orders), traces, workers[i], stale);
                    var d = TurnDiagnostics.For(w);
                    Check(d.DraftIntentsReused == 0 && d.DraftIntentsRecomputed == 0, "A stale draft was consulted on turn " + turns);
                    Check(Describe(without) == Describe(withStale), "Stale draft changed prepared orders on turn " + turns);
                }

                if (i == 0 && postOpeningTurns > 0 && postOpeningTurns % 4 == 1)
                {
                    // Forced invalidation on independent clones so nothing is resolved twice.
                    var perturbed = Clone(orders);
                    if (Perturb(w, perturbed, draft, postOpeningTurns, out int campId))
                    {
                        var plain = SaveCodec.Load(SaveCodec.Save(w)); var cached = SaveCodec.Load(SaveCodec.Save(w));
                        var cachedDraft = TurnPlanner.Draft(cached, 0, workers[i]);
                        var a = TurnPlanner.Prepare(plain, Clone(perturbed), traces, workers[i]);
                        var b = TurnPlanner.Prepare(cached, Clone(perturbed), traces, workers[i], cachedDraft);
                        var diag = TurnDiagnostics.For(cached);
                        invalidationRecomputed += diag.DraftIntentsRecomputed;
                        Check(Describe(a) == Describe(b), "Invalidated draft changed prepared orders on turn " + turns + " camp " + campId);
                        new TurnResolver().Resolve(plain, a); new TurnResolver().Resolve(cached, b);
                        Check(SaveCodec.Fingerprint(plain) == SaveCodec.Fingerprint(cached), "Invalidated draft changed outcome on turn " + turns + " camp " + campId);
                        invalidationTurns++;
                    }
                }

                var prepared = TurnPlanner.Prepare(w, orders, traces, workers[i], draft);
                var diagnostics = TurnDiagnostics.For(w);
                if (postOpeningTurns > 0) { reused += diagnostics.DraftIntentsReused; recomputed += diagnostics.DraftIntentsRecomputed; }
                new TurnResolver(workers[i]).Resolve(w, prepared);
                Check(SaveCodec.Fingerprint(reference) == SaveCodec.Fingerprint(w), "Draft replay differs from reference for worker world " + i + " on turn " + turns);
            }
            previous = drafts[0];
            turns++;
        }
        Check(reused > 0, "No draft intent was ever reused (" + reused + ").");
        Check(invalidationTurns >= 2, "Invalidation exercised on only " + invalidationTurns + " turns.");
        Check(invalidationRecomputed > 0, "Forced invalidation never recomputed an intent.");
        Console.WriteLine("PlanningDraft counters: reused=" + reused + " recomputed=" + recomputed + " invalidationTurns=" + invalidationTurns + " invalidationRecomputed=" + invalidationRecomputed);
        Console.WriteLine("PASS: " + checks + " planning draft assertions");
    }
}
