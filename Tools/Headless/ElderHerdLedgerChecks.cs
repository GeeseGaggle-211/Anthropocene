using System;
using System.Collections.Generic;
using System.Linq;
using Anthropocene.Simulation;

// Section 18 herd ledger: every change in a managed herd's count is a typed event in the economic diagnostics.
// opening + births + acquisition (taming) + compatibility grant + ownership inflow - starvation deaths - slaughter
// - ownership outflow = closing, for every herd row of every camp in a multi-turn fixture; the closing count is the
// world's committed count and the next decision's opening count. Before the fix taming and the opening
// compatibility grant changed counts with no event (e.g. 2 -> 4 -> 5 animals with zero recorded newborns).
internal static class ElderHerdLedgerChecks
{
    private static int checks;
    private static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception("ElderHerdLedger: " + message); }
    private static double D(object value) => Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);

    public static int Run()
    {
        checks = 0;
        int herdingOnly = 1 << (int)Technology.Cultivation | 1 << (int)Technology.Watercraft;
        var totals = new Dictionary<string, double>();
        // Established Neolithic episodes (taming bootstrap and refill, births, deaths, slaughter, fission outflow).
        Episode(ElderModel9Environment.GenerateOpening(31, 8, out _, 40, herdingOnly), 15, totals);
        Episode(ElderModel9Environment.GenerateOpening(1103, 8, out _, 40, 0), 12, totals);
        // A short opening leaves Paleolithic camps whose Husbandry breakthrough commits the compatibility grant.
        Episode(ElderModel9Environment.GenerateOpening(17, 8, out _, 4, herdingOnly), 8, totals);
        foreach (string key in new[] { "acquiredAnimals", "grantedAnimals", "newbornAnimals", "ownershipOutflow" })
            Check(totals.TryGetValue(key, out double value) && value > 0, "the fixture exercises " + key);
        Console.WriteLine("ElderHerdLedger: " + string.Join(", ", totals.OrderBy(p => p.Key).Select(p => p.Key + " " + p.Value)));
        Console.WriteLine("PASS: " + checks + " elder herd ledger assertions");
        return checks;
    }

    private static void Episode(byte[] snapshot, int turns, Dictionary<string, double> totals)
    {
        var environment = new ElderModel9Environment { EconomicDiagnosticsEnabled = true };
        environment.Reset(snapshot, true);
        Dictionary<(int Camp, int Herd), double> previousClosing = null; var previousCamps = new HashSet<int>();
        for (int turn = 0; turn < turns; turn++)
        {
            var step = environment.Step(new Dictionary<int, ElderDirective>());
            var world = environment.World;
            var events = world.History.Last().Events;
            double tamed = events.Select(line => System.Text.RegularExpressions.Regex.Match(line, @"^Camp \d+ tamed (\d+) "))
                .Where(m => m.Success).Sum(m => double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
            double acquiredThisTurn = 0;
            var closing = new Dictionary<(int Camp, int Herd), double>();
            foreach (var d in step.EconomicDiagnostics.Values.OrderBy(x => x.CampId))
            {
                Check(d.SchemaVersion == ElderEconomicDiagnostic.CurrentSchemaVersion && d.SchemaVersion == 3, "economic diagnostics carry herd-ledger schema 3");
                var physical = d.PhysicalEvents;
                foreach (var row in d.Herds)
                {
                    int herdId = (int)row["herdId"];
                    double opening = D(row["openingAnimals"]), born = D(row["newbornAnimals"]), acquired = D(row["acquiredAnimals"]),
                        granted = D(row["grantedAnimals"]), inflow = D(row["ownershipInflow"]), deaths = D(row["starvationDeaths"]),
                        slaughtered = D(row["slaughtered"]), outflow = D(row["ownershipOutflow"]), ending = D(row["committedEndingAnimals"]);
                    Check(opening + born + acquired + granted + inflow - deaths - slaughtered - outflow == ending,
                        "herd " + herdId + " at camp " + d.CampId + " turn " + d.DecisionTurn + ": opening " + opening + " + births " + born +
                        " + acquired " + acquired + " + granted " + granted + " + inflow " + inflow + " - deaths " + deaths + " - slaughter " + slaughtered +
                        " - outflow " + outflow + " = closing " + ending);
                    Check(D(row["residual"]) == 0, "the emitted herd residual is zero");
                    var herd = world.Herds.FirstOrDefault(h => h.Id == herdId);
                    double committed = herd != null && herd.DomesticCamp == d.CampId ? herd.Animals : 0;
                    Check(ending == committed, "the closing count is the committed world count");
                    if (previousClosing != null && previousCamps.Contains(d.CampId) && opening > 0)
                        Check(previousClosing.TryGetValue((d.CampId, herdId), out double prior) && prior == opening,
                            "the opening count is the preceding decision's closing count");
                    closing[(d.CampId, herdId)] = ending;
                    double Event(string category) => physical.Where(e => (string)e["category"] == category && (string)e["item"] == "herd:" + herdId).Sum(e => D(e["quantity"]));
                    Check(Event("herd-acquisition") == acquired && Event("herd-compatibility-grant") == granted && Event("transfer-in") == inflow,
                        "each typed inflow is a typed physical event");
                    foreach (var p in new Dictionary<string, double> { ["acquiredAnimals"] = acquired, ["grantedAnimals"] = granted, ["newbornAnimals"] = born, ["ownershipOutflow"] = outflow })
                        totals[p.Key] = (totals.TryGetValue(p.Key, out double t) ? t : 0) + p.Value;
                    acquiredThisTurn += acquired;
                }
                Check(D(d.Conservation["herdResidual"]) == 0, "the camp herd residual is zero");
                foreach (var row in d.ConservationRows.Where(r => (string)r["domain"] == "herd"))
                    Check(D(row["residual"]) == 0, "the herd conservation row closes");
                foreach (var witness in d.Transfers.Where(t => (string)t["kind"] == "herd-acquisition"))
                    Check((int)witness["destinationCamp"] == d.CampId && (int)witness["sourceCamp"] == -1 && witness.ContainsKey("wildHerdId"),
                        "an acquisition witness names the receiving camp and the source wild herd");
            }
            Check(acquiredThisTurn == tamed, "typed acquisitions equal the animals the resolver tamed this turn");
            // Every managed herd of every opening camp is a row (no silent identity).
            foreach (var herd in world.Herds.Where(h => h.DomesticCamp >= 0 && step.EconomicDiagnostics.ContainsKey(h.DomesticCamp)))
                Check(closing.ContainsKey((herd.DomesticCamp, herd.Id)), "every committed managed herd has a ledger row");
            previousClosing = closing; previousCamps = new HashSet<int>(step.EconomicDiagnostics.Keys);
        }
    }
}
