using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Anthropocene.Simulation;

// Headless tools for native Elder contract 7 (model9): opening snapshots, the teacher-as-action fidelity probe,
// native throughput and NPY teacher-data shards. Shipped configuration (fission, immigration, trade, surplus births,
// kernel split) unless a tool says otherwise.
internal static class ElderModel9Tools
{
    private static string I(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);

    internal static byte[] Opening(int seed, int players, out int openingTurns) => ElderModel9Environment.GenerateOpening(seed, players, out openingTurns);

    // --model9-probe=<seed>,<turns>: lockstep teacher run (A) and teacher-labels-as-actions run (B) from one snapshot.
    public static int Probe(int seed, int turns, int players = 8)
    {
        var snapshot = Opening(seed, players, out int opening);
        var a = new ElderModel9Environment(); var b = new ElderModel9Environment();
        a.Reset(snapshot, true); b.Reset(snapshot, true);
        var controls = new SortedDictionary<string, int>(StringComparer.Ordinal);
        int exact = 0, steps = 0, unrepresentable = 0, moves = 0; bool diverged = false; double welfareA = 0, welfareB = 0, decisions = 0, msA = 0, msB = 0, repairs = 0, labelled = 0;
        for (int t = 0; t < turns; t++)
        {
            var stepA = a.Step(new Dictionary<int, ElderDirective>());
            // Economic fidelity: movement and fission stay native (contract-7 stages 1-3).
            var directives = stepA.Villages.Where(v => v.Teacher != null).ToDictionary(v => v.Camp, v => { var d = v.Teacher.Action.Copy(); d.MovementCandidate = ElderModel9Contract.NativeMovement; d.FissionPolicy = -1; return d; });
            var stepB = b.Step(directives);
            if (!diverged)
                foreach (var va in stepA.Villages)
                {
                    var vb = stepB.Villages.FirstOrDefault(v => v.Camp == va.Camp);
                    if (vb != null && va.Population == vb.Population && Math.Abs(va.Welfare - vb.Welfare) <= 1e-6 * Math.Max(1, va.Welfare)) continue;
                    diverged = true;
                    string L(ElderModel9Label l) => l == null ? "none" : "f" + I(l.Action.RetainedFieldOperation) + " h" + I(l.Action.RetainedHerdFraction) + " d" + I(l.Action.DevelopmentBudget)
                        + " s" + I(l.Action.FieldDevelopmentShare) + " f" + I(l.Action.FishingDevelopmentShare) + " lv" + string.Join(",", l.Action.ServiceLevels.Select(I)) + " r" + l.Action.ClothingRoute + l.Action.ShelterRoute;
                    Console.WriteLine("MODEL9_DIVERGE turn=" + stepA.Turn + " camp=" + va.Camp + " pop=" + va.Population + "/" + (vb?.Population ?? -1) + " welfare=" + I(va.Welfare) + "/" + I(vb?.Welfare ?? 0)
                        + " labelA=" + L(va.Teacher) + " realizedB=" + L(vb?.Teacher) + " staffA=" + va.Staffing + " staffB=" + vb?.Staffing
                        + " interventions=" + string.Join(";", (vb?.Interventions ?? Array.Empty<ElderIntervention>()).Select(i => i.Control + ":" + I(i.Requested) + ">" + I(i.Accepted))));
                    break;
                }
            steps++; if (stepA.Fingerprint == stepB.Fingerprint) exact++;
            welfareA += stepA.Villages.Sum(v => v.Welfare); welfareB += stepB.Villages.Sum(v => v.Welfare);
            decisions += stepA.Villages.Count(v => v.Teacher != null); msA += stepA.NativeMilliseconds; msB += stepB.NativeMilliseconds;
            repairs += stepB.Villages.Sum(v => v.Interventions.Length); labelled += directives.Count;
            foreach (var intervention in stepB.Villages.SelectMany(v => v.Interventions))
                controls[intervention.Control] = (controls.TryGetValue(intervention.Control, out int count) ? count : 0) + 1;
            unrepresentable += stepA.Villages.Count(v => v.Teacher != null && !v.Teacher.MovementMask); moves += stepA.Villages.Count(v => v.Teacher != null && (v.Teacher.Action.MovementCandidate >= 0 || !v.Teacher.MovementMask));
            Console.WriteLine("MODEL9_PROBE seed=" + seed + " turn=" + stepA.Turn + " villages=" + stepA.Villages.Count + " decisions=" + directives.Count
                + " exact=" + (stepA.Fingerprint == stepB.Fingerprint) + " welfareA=" + I(stepA.Villages.Sum(v => v.Welfare)) + " welfareB=" + I(stepB.Villages.Sum(v => v.Welfare))
                + " popA=" + a.World.Camps.Sum(a.World.LinkedPopulation) + " popB=" + b.World.Camps.Sum(b.World.LinkedPopulation)
                + " msA=" + I(stepA.NativeMilliseconds) + " msB=" + I(stepB.NativeMilliseconds));
        }
        ElderModel9Environment.VerifyReplay(b.Replay);
        Console.WriteLine("MODEL9_PROBE_SUMMARY seed=" + seed + " openingTurns=" + opening + " steps=" + steps + " exactSteps=" + exact
            + " welfareRatio=" + I(welfareA > 0 ? welfareB / welfareA : 0) + " decisions=" + decisions
            + " teacherDecisionsPerSecond=" + I(decisions / Math.Max(1e-9, msA / 1000)) + " directiveDecisionsPerSecond=" + I(labelled / Math.Max(1e-9, msB / 1000))
            + " heuristicMoves=" + moves + " unrepresentableMoves=" + unrepresentable + " interventionsPerDecision=" + I(labelled > 0 ? repairs / labelled : 0) + " replay=verified"
            + " interventionControls=" + string.Join(",", controls.Select(p => p.Key + ":" + p.Value)));
        return 0;
    }

    // Mean crop-moisture suitability (EcologyMath.CropSuitability) over a camp's land footprint and the four seasons:
    // the factor that scales cultivated food, 1 on well-watered land.
    internal static double FarmSuitability(WorldState w, Camp c)
    {
        var tiles = CultivationEconomy.Footprint(w, c).Where(id => w.Tiles[id].TraversableLand).ToList();
        return tiles.Count == 0 ? 0 : tiles.Average(id => Enumerable.Range(0, 4)
            .Average(s => EcologyMath.CropSuitability(w.Config, w.Tiles[id].Seasons[s].Moisture)));
    }

    // --model9-scan=<firstSeed>,<lastSeed>[,withheld]: openings only. Per seed: villages, husbandry villages on
    // grassland, and their mean farm suitability; used to build grassland-rich curriculum banks.
    public static int Scan(int firstSeed, int lastSeed, int withheld = 0)
    {
        for (int seed = firstSeed; seed <= lastSeed; seed++)
        {
            var world = SaveCodec.Load(ElderModel9Environment.GenerateOpening(seed, 8, out _, 40, withheld));
            var camps = world.Camps.Where(c => c.Population > 0 && CultivationEconomy.EconomyEnabled(world, c)).ToList();
            var grass = camps.Where(c => world.Tiles[c.Tile].Environment == EnvironmentKind.Grassland).ToList();
            var herding = grass.Where(c => CultivationEconomy.HusbandryEnabled(c)).ToList();
            Console.WriteLine("MODEL9_SCAN seed=" + seed + " villages=" + camps.Count + " grassland=" + grass.Count + " grasslandHerding=" + herding.Count
                + " suitability=" + I(herding.Count > 0 ? herding.Average(c => FarmSuitability(world, c)) : -1)
                + " allSuitability=" + I(camps.Count > 0 ? camps.Average(c => FarmSuitability(world, c)) : -1));
        }
        return 0;
    }

    // --model9-valley-scan=<firstSeed>,<lastSeed>[,turns]: contract-10 valley fixtures (Docs/ElderModel9Contract9MovementDesign.md).
    // A herding village on grassland is eligible when its own region observation shows a known foreign settlement
    // within four tiles (competition; camp spacing rules out nearer ones) and a region row away from it that is good farmland: visible with a farming
    // forecast of at least one meal per resident and 1.25 times the current tile's, or remembered on a river in
    // grassland, temperate or tropical land (static geography only). Openings are too sparse for competition, so the
    // heuristic plays on for up to <turns> turns (default 12); a fixture is (seed, first eligible turn): selected
    // states of unedited worlds, reached deterministically by playing the heuristic that many turns.
    public static int ValleyScan(int firstSeed, int lastSeed, int turns = 12)
    {
        var builder = new ElderObservationBuilder { ValueOrderedSites = true };
        for (int seed = firstSeed; seed <= lastSeed; seed++)
        {
            var env = new ElderModel9Environment { TeacherLabels = false };
            env.Reset(ElderModel9Environment.GenerateOpening(seed, 8, out _, 40, 0), true);
            // Strict: competition and a valley; relaxed: a valley alone (the first turn each occurs).
            int strict = -1, relaxed = -1, strictCamps = 0, relaxedCamps = 0, contested = 0, visible = 0, remembered = 0;
            for (int turn = 0; turn <= turns && strict < 0; turn++)
            {
                if (turn > 0) env.Step(new Dictionary<int, ElderDirective>());
                var world = env.World;
                int withCompetition = 0, valleys = 0;
                foreach (var camp in world.Camps.Where(c => c.Population > 0 && CultivationEconomy.EconomyEnabled(world, c)
                    && CultivationEconomy.HusbandryEnabled(c) && world.Tiles[c.Tile].Environment == EnvironmentKind.Grassland).OrderBy(c => c.Id))
                {
                    var o = builder.Build(world, camp.Id, true);
                    if (o.Regions.Length == 0) continue;
                    var here = o.Regions[0];
                    bool seen = o.Regions.Skip(1).Any(r => r.Visible && r.FarmingFood >= Math.Max(1, 1.25 * here.FarmingFood));
                    bool known = o.Regions.Skip(1).Any(r => !r.Visible && r.River && (r.EnvironmentGroup == 1 || r.EnvironmentGroup == 2 || r.EnvironmentGroup == 3));
                    bool competition = here.KnownForeign > 0;
                    if (competition) contested++;
                    if (!seen && !known) continue;
                    valleys++;
                    if (competition) { withCompetition++; if (seen) visible++; else remembered++; }
                }
                if (relaxed < 0 && valleys > 0) { relaxed = turn; relaxedCamps = valleys; }
                if (withCompetition > 0) { strict = turn; strictCamps = withCompetition; }
            }
            Console.WriteLine("MODEL9_VALLEY seed=" + seed + " strictTurn=" + strict + " strictCamps=" + strictCamps + " relaxedTurn=" + relaxed
                + " relaxedCamps=" + relaxedCamps + " contestedChecks=" + contested + " visibleValley=" + visible + " rememberedValley=" + remembered);
        }
        return 0;
    }

    // --model9-requests=<seed>,<turns>[,withheld]: per village, the heuristic's shelter and clothing requests with
    // grants and unmet causes at the last turn (diagnostic for capability fixtures).
    public static int Requests(int seed, int turns, int withheld = 0)
    {
        var env = new ElderModel9Environment { WithheldTechnologies = withheld };
        env.Reset(ElderModel9Environment.GenerateOpening(seed, 8, out _, 40, withheld), true);
        for (int t = 0; t < turns; t++)
        {
            var s = env.Step(new Dictionary<int, ElderDirective>());
            var managed = env.World.Herds.Where(h => h.DomesticCamp >= 0 && h.Animals > 0).ToList();
            Console.WriteLine("MODEL9_REQ_TURN turn=" + s.Turn + " villages=" + s.Villages.Count + " pop=" + env.World.Camps.Sum(c => c.Population)
                + " qol=" + I(s.Villages.Count > 0 ? s.Villages.Average(v => v.QoL) : 0)
                + " clothing=" + I(s.Villages.Count > 0 ? s.Villages.Average(v => v.Clothing) : 0)
                + " housing=" + I(s.Villages.Count > 0 ? s.Villages.Average(v => v.Housing) : 0)
                + " nutrition=" + I(s.Villages.Count > 0 ? s.Villages.Average(v => v.Nutrition) : 0) + " herds=" + managed.Count + " animals=" + managed.Sum(h => h.Animals)
                + " fabric=" + I(env.World.Camps.Sum(c => c.Inventory.AnimalFabric)) + " light=" + I(env.World.Camps.Sum(c => c.Inventory.LightHousingUnits)));
        }
        ResolutionMetrics.CaptureDemandAllocations = true;
        var draft = TurnPlanner.Prepare(env.World, AiPlanning.Decide(env.World).Orders.ToDictionary(p => p.Key, p => p.Value.ToList()), new List<DecisionTrace>());
        // Units requested and unmet by cause, per textile service, across all villages.
        var tally = new SortedDictionary<string, double>(StringComparer.Ordinal);
        foreach (var a in draft.DemandAllocations.Values)
            foreach (var q in a.Requests.Where(q => q.Service.Contains("shelter") || q.Service.Contains("cloth")))
            {
                string key = q.Service.Contains("cloth") ? "clothing" : "shelter";
                tally[key + " units"] = (tally.TryGetValue(key + " units", out double u) ? u : 0) + q.Units;
                double unmet = Math.Max(0, q.Units - q.Granted);
                if (unmet > 0)
                {
                    string cause = key + " unmet: " + (q.Unmet.Length > 0 ? q.Unmet : "?");
                    tally[cause] = (tally.TryGetValue(cause, out double v) ? v : 0) + unmet * (key == "shelter" ? 1 : 1);
                }
            }
        Console.WriteLine("MODEL9_REQ_TALLY " + string.Join("; ", tally.Select(p => p.Key + "=" + I(p.Value))));
        foreach (var pair in draft.DemandAllocations.OrderBy(p => p.Key))
        {
            var camp = env.World.Camps.First(c => c.Id == pair.Key); var a = pair.Value;
            var techs = string.Join("", new[] { NeolithicTech.Construction, NeolithicTech.Weaving, NeolithicTech.Toolmaking }.Select(x => camp.People.HasTech(x) ? x.ToString()[0] : '-'));
            Console.WriteLine("MODEL9_REQ camp=" + camp.Id + " pop=" + camp.Population + " techs=" + techs + " budget=" + I(a.ResidentGoodsBudget)
                + " herders=" + a.Herders + " animals=" + env.World.Herds.Where(h => h.DomesticCamp == camp.Id).Sum(h => h.Animals) + " fishers=" + a.Fishers + " gatherers=" + a.FoodGatherers + " compiled=" + I(a.FinalCompiledPersons)
                + " housing=" + I(camp.HousingPerCapita) + " light=" + I(camp.Inventory.LightHousingUnits) + " fabric=" + I(camp.Inventory.AnimalFabric)
                + " animalFood=" + I(camp.AnimalFood) + " food=" + I(camp.Food) + " plantFood=" + I(camp.PlantFood)
                + " supply=" + I(a.ExpectedSupply) + " forage=" + I(a.ExpectedForageAfterHerds) + " managed=" + I(a.ExpectedManagedAnimalFood)
                + " meals=" + I(a.Meals) + " reserve=" + I(a.OperatingReserve) + " animalEnv=" + I(a.AnimalEnvelope) + " animalEnvCloth=" + I(a.AnimalEnvelopeClothing)
                + " plantEnv=" + I(a.PlantEnvelope) + " | " + string.Join("; ", a.Requests.Where(q => q.Service.Contains("shelter") || q.Service.Contains("cloth") || q.Service.Contains("herd"))
                    .Select(q => q.Service + ":" + q.Class + " " + I(q.Granted) + "/" + I(q.Units) + (q.Unmet.Length > 0 ? " (" + q.Unmet + ")" : ""))));
        }
        return 0;
    }

    // --model9-pastoral=<firstSeed>,<lastSeed>,<turns>[,withheld]: does keeping herds pay in grassland? From one opening,
    // arm A runs the heuristic everywhere; arms B and C give every grassland village that can both farm and herd and
    // holds a herd a herd-first directive (B: no retained field operation, C: half), with the whole herd retained and
    // the Development budget on herds. Other villages, services, routes, movement and fission stay native. Reports
    // treated-village and whole-world welfare per arm, and treated villages by farm yield (HarvestProductivity).
    public static int Pastoral(int firstSeed, int lastSeed, int turns, int withheld = 0)
    {
        var rows = new List<(int Seed, int Camp, double Yield, double A, double B, double C)>();
        double worldA = 0, worldB = 0, worldC = 0;
        for (int seed = firstSeed; seed <= lastSeed; seed++)
        {
            var snapshot = ElderModel9Environment.GenerateOpening(seed, 8, out _, 40, withheld);
            var arms = new[] { new ElderModel9Environment { WithheldTechnologies = withheld }, new ElderModel9Environment { WithheldTechnologies = withheld },
                new ElderModel9Environment { WithheldTechnologies = withheld } };
            foreach (var arm in arms) arm.Reset(snapshot, true);
            var treated = new Dictionary<int, double>();   // camp -> farm yield when first treated (from arm B)
            var welfare = new Dictionary<int, double>[] { new Dictionary<int, double>(), new Dictionary<int, double>(), new Dictionary<int, double>() };
            for (int t = 0; t < turns; t++)
            {
                for (int a = 0; a < 3; a++)
                {
                    var env = arms[a]; var world = env.World;
                    var directives = new Dictionary<int, ElderDirective>();
                    if (a > 0)
                        foreach (var camp in world.Camps.Where(c => c.Population > 0 && CultivationEconomy.Enabled(c) && CultivationEconomy.HusbandryEnabled(c)
                            && world.Tiles[c.Tile].Environment == EnvironmentKind.Grassland && world.Herds.Any(h => h.DomesticCamp == c.Id && h.Animals > 0)))
                        {
                            if (a == 1 && !treated.ContainsKey(camp.Id)) treated[camp.Id] = FarmSuitability(world, camp);
                            directives[camp.Id] = new ElderDirective { SchemaVersion = ElderModel9Contract.ActionSchemaVersion,
                                RetainedFieldOperation = a == 1 ? 0 : .5, RetainedHerdFraction = 1, DevelopmentBudget = .15,
                                FieldDevelopmentShare = 0, MovementCandidate = ElderModel9Contract.NativeMovement };
                        }
                    var step = env.Step(directives);
                    foreach (var v in step.Villages) welfare[a][v.Camp] = (welfare[a].TryGetValue(v.Camp, out double w) ? w : 0) + v.Welfare;
                }
            }
            worldA += welfare[0].Values.Sum(); worldB += welfare[1].Values.Sum(); worldC += welfare[2].Values.Sum();
            foreach (var pair in treated)
                rows.Add((seed, pair.Key, pair.Value, welfare[0].TryGetValue(pair.Key, out double wa) ? wa : 0,
                    welfare[1].TryGetValue(pair.Key, out double wb) ? wb : 0, welfare[2].TryGetValue(pair.Key, out double wc) ? wc : 0));
            Console.WriteLine("MODEL9_PASTORAL seed=" + seed + " treated=" + treated.Count + " worldA=" + I(welfare[0].Values.Sum())
                + " worldB=" + I(welfare[1].Values.Sum()) + " worldC=" + I(welfare[2].Values.Sum()));
        }
        foreach (var row in rows)
            Console.WriteLine("MODEL9_PASTORAL_VILLAGE seed=" + row.Seed + " camp=" + row.Camp + " yield=" + I(row.Yield)
                + " A=" + I(row.A) + " B=" + I(row.B) + " C=" + I(row.C));
        string Band(IEnumerable<(int Seed, int Camp, double Yield, double A, double B, double C)> band)
        {
            var list = band.ToList(); double a = list.Sum(r => r.A);
            return "n=" + list.Count + " B/A=" + I(a > 0 ? list.Sum(r => r.B) / a : 0) + " C/A=" + I(a > 0 ? list.Sum(r => r.C) / a : 0)
                + " Bwins=" + list.Count(r => r.B > r.A) + " Cwins=" + list.Count(r => r.C > r.A);
        }
        var sorted = rows.OrderBy(r => r.Yield).ToList(); int third = Math.Max(1, sorted.Count / 3);
        Console.WriteLine("MODEL9_PASTORAL_SUMMARY seeds=" + firstSeed + "-" + lastSeed + " turns=" + turns + " withheld=" + withheld
            + " worldB/A=" + I(worldA > 0 ? worldB / worldA : 0) + " worldC/A=" + I(worldA > 0 ? worldC / worldA : 0)
            + " all{" + Band(rows) + "} lowYield{" + Band(sorted.Take(third)) + "} midYield{" + Band(sorted.Skip(third).Take(third))
            + "} highYield{" + Band(sorted.Skip(2 * third)) + "} yieldCuts=" + (sorted.Count > 0 ? I(sorted[Math.Min(third, sorted.Count - 1)].Yield) + "/" + I(sorted[Math.Min(2 * third, sorted.Count - 1)].Yield) : "-"));
        return 0;
    }

    // --model9-ab=<firstSeed>,<lastSeed>,<turns>[,withheld[,jobs]] with --arm-a=Field=Value;... and --arm-b=...: a paired
    // A/B of configuration overrides. Each seed generates one opening with the base configuration; each arm loads it,
    // applies its overrides to the loaded world's configuration (dotted paths reach nested settings) and runs the
    // heuristic for every village. Reports per seed and the paired B/A distribution of welfare (summed village
    // welfare), final population and population-weighted QoL as mean, standard error, median, a 95% t interval and
    // sign counts, so a change is judged against seed-to-seed divergence rather than a few worlds.
    public static int AB(int firstSeed, int lastSeed, int turns, int withheld, int jobs, string armA, string armB)
    {
        var seeds = Enumerable.Range(firstSeed, lastSeed - firstSeed + 1).ToArray();
        var results = new (double Welfare, double Population, double QoL, int Settlements)[seeds.Length, 2];
        System.Threading.Tasks.Parallel.For(0, seeds.Length, new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, jobs) }, i =>
        {
            var snapshot = ElderModel9Environment.GenerateOpening(seeds[i], 8, out _, 40, withheld);
            for (int arm = 0; arm < 2; arm++)
            {
                var env = new ElderModel9Environment { WithheldTechnologies = withheld };
                env.Reset(snapshot, true);
                ApplyOverrides(env.World.Config, arm == 0 ? armA : armB);
                double welfare = 0;
                for (int t = 0; t < turns; t++) welfare += env.Step(new Dictionary<int, ElderDirective>()).Villages.Sum(v => v.Welfare);
                var live = env.World.Camps.Where(c => c.Population > 0).ToList();
                double pop = live.Sum(c => c.Population);
                results[i, arm] = (welfare, pop, pop > 0 ? live.Sum(c => c.QoL * c.Population) / pop : 0, live.Count);
            }
        });
        for (int i = 0; i < seeds.Length; i++)
            Console.WriteLine("MODEL9_AB seed=" + seeds[i] + " welfare=" + I(results[i, 0].Welfare) + "/" + I(results[i, 1].Welfare)
                + " pop=" + results[i, 0].Population + "/" + results[i, 1].Population + " qol=" + I(results[i, 0].QoL) + "/" + I(results[i, 1].QoL)
                + " settlements=" + results[i, 0].Settlements + "/" + results[i, 1].Settlements);
        string Summary(string name, Func<int, int, double> value)
        {
            var r = Enumerable.Range(0, seeds.Length).Select(i => value(i, 0) > 0 ? value(i, 1) / value(i, 0) : 1).ToArray();
            double mean = r.Average(), sd = r.Length > 1 ? Math.Sqrt(r.Sum(x => (x - mean) * (x - mean)) / (r.Length - 1)) : 0;
            double se = sd / Math.Sqrt(r.Length), t = TCritical95(r.Length - 1);
            var sorted = r.OrderBy(x => x).ToArray();
            return name + "{mean=" + I(mean) + " se=" + I(se) + " ci95=" + I(mean - t * se) + ".." + I(mean + t * se) + " median=" + I(sorted[sorted.Length / 2])
                + " sd=" + I(sd) + " better=" + r.Count(x => x > 1.01) + " worse=" + r.Count(x => x < .99) + " identical=" + r.Count(x => Math.Abs(x - 1) < 1e-9) + "}";
        }
        Console.WriteLine("MODEL9_AB_SUMMARY seeds=" + firstSeed + "-" + lastSeed + " turns=" + turns + " withheld=" + withheld
            + " a=[" + armA + "] b=[" + armB + "] " + Summary("welfare", (i, a) => results[i, a].Welfare) + " "
            + Summary("population", (i, a) => results[i, a].Population) + " " + Summary("qol", (i, a) => results[i, a].QoL));
        return 0;
    }

    // --model9-divergence=<firstSeed>,<lastSeed>,<turns>,<perturbTurn>[,epsilon[,withheld]]: how fast do two copies of
    // a world separate after a tiny perturbation? Both copies run the heuristic from one opening; at perturbTurn the
    // copy B multiplies the food store of its lowest-ID living camp by (1 + epsilon). A diagnostic state edit, not a
    // game rule. Per turn it reports whether the fingerprints still match, the relative population and settlement
    // gaps, and each copy's fission and migration counts, so divergence can be attributed to discrete social events.
    public static int Divergence(int firstSeed, int lastSeed, int turns, int perturbTurn, double epsilon, int withheld)
    {
        for (int seed = firstSeed; seed <= lastSeed; seed++)
        {
            var snapshot = ElderModel9Environment.GenerateOpening(seed, 8, out _, 40, withheld);
            var a = new ElderModel9Environment { WithheldTechnologies = withheld }; a.Reset(snapshot, true);
            var b = new ElderModel9Environment { WithheldTechnologies = withheld }; b.Reset(snapshot, true);
            int firstDiff = -1, fissionsA = 0, fissionsB = 0, movesA = 0, movesB = 0;
            for (int t = 0; t < turns; t++)
            {
                if (t == perturbTurn)
                {
                    // epsilon <= -1: a discrete perturbation, removing that many animals from the largest managed herd.
                    if (epsilon <= -1)
                    {
                        var herd = b.World.Herds.Where(h => h.DomesticCamp >= 0 && h.Animals > 1).OrderByDescending(h => h.Animals).ThenBy(h => h.Id).FirstOrDefault();
                        if (herd != null) herd.Animals = Math.Max(1, herd.Animals - (int)Math.Round(-epsilon));
                    }
                    else
                    {
                        var camp = b.World.Camps.Where(c => c.Population > 0).OrderBy(c => c.Id).FirstOrDefault();
                        if (camp != null) camp.Food *= 1 + epsilon;
                    }
                }
                var sa = a.Step(new Dictionary<int, ElderDirective>()); var sb = b.Step(new Dictionary<int, ElderDirective>());
                var ra = a.World.History.Last(); var rb = b.World.History.Last();
                fissionsA += ra.Fissions.Count; fissionsB += rb.Fissions.Count; movesA += ra.Migrations.Count; movesB += rb.Migrations.Count;
                bool same = sa.Fingerprint == sb.Fingerprint;
                if (!same && firstDiff < 0) firstDiff = t;
                double popA = a.World.Camps.Sum(c => c.Population), popB = b.World.Camps.Sum(c => c.Population);
                int setA = a.World.Camps.Count(c => c.Population > 0), setB = b.World.Camps.Count(c => c.Population > 0);
                Console.WriteLine("MODEL9_DIVERGENCE seed=" + seed + " turn=" + t + " same=" + same + " popGap=" + I(popA > 0 ? Math.Abs(popB - popA) / popA : 0)
                    + " pop=" + popA + "/" + popB + " settlements=" + setA + "/" + setB + " fissions=" + fissionsA + "/" + fissionsB
                    + " migrations=" + movesA + "/" + movesB);
            }
            Console.WriteLine("MODEL9_DIVERGENCE_SUMMARY seed=" + seed + " perturbTurn=" + perturbTurn + " epsilon=" + epsilon + " firstDifferentTurn=" + firstDiff);
        }
        return 0;
    }

    static void ApplyOverrides(SimulationConfig cfg, string overrides)
    {
        foreach (var item in (overrides ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = item.Split('=');
            object target = cfg; var path = pair[0].Trim().Split('.');
            for (int i = 0; i < path.Length - 1; i++)
                target = (target.GetType().GetField(path[i]) ?? throw new ArgumentException("Unknown config field " + pair[0])).GetValue(target);
            var field = target.GetType().GetField(path[path.Length - 1]) ?? throw new ArgumentException("Unknown config field " + pair[0]);
            field.SetValue(target, Convert.ChangeType(pair[1].Trim(), field.FieldType, System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    // Two-sided 95% Student t critical values (df 1-30, then the normal value).
    static double TCritical95(int df)
    {
        double[] table = { 12.706, 4.303, 3.182, 2.776, 2.571, 2.447, 2.365, 2.306, 2.262, 2.228, 2.201, 2.179, 2.160, 2.145, 2.131,
            2.120, 2.110, 2.101, 2.093, 2.086, 2.080, 2.074, 2.069, 2.064, 2.060, 2.056, 2.052, 2.048, 2.045, 2.042 };
        return df < 1 ? double.NaN : df <= table.Length ? table[df - 1] : 1.96;
    }

    // --model9-oracle=<firstSeed>,<lastSeed>,<turns>: is there a better mixed-use allocation than the heuristic's? From
    // one opening, arm 0 runs the heuristic everywhere; each other arm gives every village that can both farm and
    // herd one fixed directive for the whole horizon (routes, services, movement and fission native). Reports, per
    // arm and seed, the welfare of those mixed villages (summed over every village ever treated in that arm, or for
    // arm 0, ever mixed) and of the whole world, as ratios to the heuristic.
    static readonly (string Name, double Field, double Herd, double Budget, double FieldShare)[] OracleArms =
    {
        ("farm-lean", 1, .5, .1, 1), ("herd-lean", .5, 1, .1, 0), ("balanced", 1, 1, .1, .5),
        ("pure-farm", 1, 0, .15, 1), ("pure-herd", 0, 1, .15, 0), ("balanced-nodev", 1, 1, 0, .5),
        ("balanced-highdev", 1, 1, .3, .5), ("keep-all-lowdev", 1, 1, .05, .5)
    };
    public static int Oracle(int firstSeed, int lastSeed, int turns)
    {
        int arms = OracleArms.Length + 1;
        var mixedTotal = new double[arms]; var worldTotal = new double[arms]; var wins = new int[arms];
        for (int seed = firstSeed; seed <= lastSeed; seed++)
        {
            var snapshot = ElderModel9Environment.GenerateOpening(seed, 8, out _, 40);
            var mixed = new double[arms]; var world = new double[arms]; var qol = new double[arms]; var pop = new double[arms];
            for (int a = 0; a < arms; a++)
            {
                var env = new ElderModel9Environment(); env.Reset(snapshot, true);
                var ever = new HashSet<int>(); var welfare = new Dictionary<int, double>();
                for (int t = 0; t < turns; t++)
                {
                    var directives = new Dictionary<int, ElderDirective>();
                    foreach (var camp in env.World.Camps.Where(c => c.Population > 0 && CultivationEconomy.Enabled(c) && CultivationEconomy.HusbandryEnabled(c)))
                    {
                        ever.Add(camp.Id);
                        if (a == 0) continue;
                        var arm = OracleArms[a - 1];
                        directives[camp.Id] = new ElderDirective { SchemaVersion = ElderModel9Contract.ActionSchemaVersion,
                            RetainedFieldOperation = arm.Field, RetainedHerdFraction = arm.Herd, DevelopmentBudget = arm.Budget,
                            FieldDevelopmentShare = arm.FieldShare, FishingDevelopmentShare = 0, MovementCandidate = ElderModel9Contract.NativeMovement };
                    }
                    var step = env.Step(directives);
                    foreach (var v in step.Villages) welfare[v.Camp] = (welfare.TryGetValue(v.Camp, out double w) ? w : 0) + v.Welfare;
                }
                mixed[a] = welfare.Where(p => ever.Contains(p.Key)).Sum(p => p.Value); world[a] = welfare.Values.Sum();
                var live = env.World.Camps.Where(c => c.Population > 0).ToList();
                pop[a] = live.Sum(c => c.Population); qol[a] = pop[a] > 0 ? live.Sum(c => c.QoL * c.Population) / pop[a] : 0;
                mixedTotal[a] += mixed[a]; worldTotal[a] += world[a];
                if (a > 0 && world[a] > world[0]) wins[a]++;
            }
            for (int a = 0; a < arms; a++)
                Console.WriteLine("MODEL9_ORACLE seed=" + seed + " arm=" + (a == 0 ? "heuristic" : OracleArms[a - 1].Name)
                    + " mixed/h=" + I(mixed[0] > 0 ? mixed[a] / mixed[0] : 0) + " world/h=" + I(world[0] > 0 ? world[a] / world[0] : 0)
                    + " pop=" + pop[a] + " qol=" + I(qol[a]));
        }
        for (int a = 1; a < arms; a++)
            Console.WriteLine("MODEL9_ORACLE_SUMMARY seeds=" + firstSeed + "-" + lastSeed + " arm=" + OracleArms[a - 1].Name
                + " mixed/h=" + I(mixedTotal[0] > 0 ? mixedTotal[a] / mixedTotal[0] : 0) + " world/h=" + I(worldTotal[0] > 0 ? worldTotal[a] / worldTotal[0] : 0)
                + " worldWins=" + wins[a]);
        return 0;
    }

    // --model9-liquidation=<firstSeed>,<lastSeed>,<turns>[,withheld]: do herding villages slaughter their herds when
    // they take up farming? Every village runs the heuristic. A liquidation is a turn in which a village holding at
    // least 5 animals slaughters at least half of them; it is a farming switch when the village's Farmers rose that
    // turn. Reports counts, the farm yield of switching villages and slaughter against births.
    public static int Liquidation(int firstSeed, int lastSeed, int turns, int withheld = 0)
    {
        int herdTurns = 0, events = 0, farmingSwitches = 0, toZero = 0; long slaughtered = 0, born = 0, starved = 0;
        var switchYields = new List<double>(); var herdYields = new List<double>();
        for (int seed = firstSeed; seed <= lastSeed; seed++)
        {
            var env = new ElderModel9Environment { WithheldTechnologies = withheld };
            env.Reset(ElderModel9Environment.GenerateOpening(seed, 8, out _, 40, withheld), true);
            int Farmers(Camp c) => c.Workforce?.Assignments?.Where(a => a.Role == WorkforceRoles.Farmer).Sum(a => a.Population) ?? 0;
            int seedEvents = 0;
            for (int t = 0; t < turns; t++)
            {
                var world = env.World;
                var before = world.Camps.Where(c => c.Population > 0).ToDictionary(c => c.Id,
                    c => (Animals: world.Herds.Where(h => h.DomesticCamp == c.Id).Sum(h => h.Animals), Farmers: Farmers(c)));
                env.Step(new Dictionary<int, ElderDirective>());
                world = env.World; var record = world.History.Last();
                foreach (var report in record.ManagedHerds) { slaughtered += report.Slaughtered; born += report.NewbornAnimals; starved += report.StarvationDeaths; }
                foreach (var pair in before.Where(p => p.Value.Animals >= 5))
                {
                    var camp = world.Camps.FirstOrDefault(c => c.Id == pair.Key); if (camp == null) continue;
                    herdTurns++; herdYields.Add(FarmSuitability(world, camp));
                    int killed = record.ManagedHerds.Where(m => m.Camp == pair.Key).Sum(m => m.Slaughtered);
                    if (killed * 2 < pair.Value.Animals) continue;
                    events++; seedEvents++;
                    if (world.Herds.Where(h => h.DomesticCamp == pair.Key).Sum(h => h.Animals) == 0) toZero++;
                    if (Farmers(camp) > pair.Value.Farmers) { farmingSwitches++; switchYields.Add(FarmSuitability(world, camp)); }
                }
            }
            Console.WriteLine("MODEL9_LIQUIDATION seed=" + seed + " events=" + seedEvents);
        }
        double Median(List<double> x) { if (x.Count == 0) return -1; var s = x.OrderBy(v => v).ToList(); return s[s.Count / 2]; }
        Console.WriteLine("MODEL9_LIQUIDATION_SUMMARY seeds=" + firstSeed + "-" + lastSeed + " turns=" + turns + " withheld=" + withheld
            + " herdVillageTurns=" + herdTurns + " liquidations=" + events + " toZero=" + toZero + " withFarmingSwitch=" + farmingSwitches
            + " switchYieldMedian=" + I(Median(switchYields)) + " herdVillageYieldMedian=" + I(Median(herdYields))
            + " slaughtered=" + slaughtered + " born=" + born + " starved=" + starved);
        return 0;
    }

    // --model9-profile=<seed>,<turns>[,players]: phase timing of teacher steps (heuristic for every village).
    public static int Profile(int seed, int turns, int players = 8)
    {
        var env = new ElderModel9Environment();
        env.Reset(Opening(seed, players, out _), true);
        PhaseProfile.Reset(); PhaseProfile.Enabled = true;
        var sum = new double[9]; long decisions = 0; double villagesSum = 0;
        for (int t = 0; t < turns; t++)
        {
            var s = env.Step(new Dictionary<int, ElderDirective>());
            var parts = new[] { s.DecideMs, s.PrepareMs, s.LabelMs, s.ResolveMs, s.FingerprintMs, s.ObserveMs, s.EncodeMs, s.PrivilegedMs, s.NativeMilliseconds };
            for (int i = 0; i < parts.Length; i++) sum[i] += parts[i];
            decisions += s.Villages.Count(v => v.Teacher != null); villagesSum += s.Villages.Count;
            Console.WriteLine("MODEL9_FINGERPRINT turn=" + s.Turn + " villages=" + s.Villages.Count + " " + s.Fingerprint);
            // Managed herds after the turn: count, animals, newborns and full-nutrition herds (breeding requires it).
            var managed = env.World.Herds.Where(h => h.DomesticCamp >= 0 && h.StartingAnimals > 0).ToList();
            Console.WriteLine("MODEL9_HERDS turn=" + s.Turn + " herds=" + managed.Count + " animals=" + managed.Sum(h => h.Animals)
                + " newborn=" + managed.Sum(h => h.NewbornAnimals) + " breedingHerds=" + managed.Count(h => h.NewbornAnimals > 0)
                + " fullNutrition=" + managed.Count(h => h.LastNutrition >= 1 - 1e-12) + " fullWater=" + managed.Count(h => h.LastWaterNutrition >= 1 - 1e-12)
                + " emptied=" + env.World.Herds.Count(h => h.DomesticCamp >= 0 && h.StartingAnimals > 0 && h.Animals == 0));
        }
        PhaseProfile.Enabled = false;
        foreach (var counter in PhaseProfile.Counters())
            Console.WriteLine("MODEL9_COUNT " + counter.Key + "/turn=" + I(counter.Value / (double)turns));
        Console.WriteLine("MODEL9_BOUND evaluations=" + SocialRelocationForecastCache.BoundEvaluations
            + " pruned=" + SocialRelocationForecastCache.BoundPruned + " audit=" + SocialRelocationForecastCache.AuditBounds);
        foreach (var row in PhaseProfile.Rows().OrderByDescending(r => r.ExclusiveMs).Take(24))
            Console.WriteLine("MODEL9_PHASE exclusiveMsPerTurn=" + I(row.ExclusiveMs / turns) + " share=" + I(100 * row.ExclusiveMs / Math.Max(1e-9, sum[8])) + "% calls/turn=" + I(row.Calls / (double)turns) + " " + row.Path);
        string[] names = { "decide", "prepare", "labels", "resolve", "fingerprint", "observe", "encode", "privileged", "total" };
        Console.WriteLine("MODEL9_PROFILE seed=" + seed + " players=" + players + " turns=" + turns + " meanVillages=" + I(villagesSum / turns) + " decisions=" + decisions
            + " decisionsPerSecond=" + I(decisions / Math.Max(1e-9, sum[8] / 1000)) + " " + string.Join(" ", names.Select((n, i) => n + "Ms=" + I(sum[i] / turns) + "(" + I(100 * sum[i] / Math.Max(1e-9, sum[8])) + "%)")));
        return 0;
    }

    // --model9-teacher=<outDir>,<firstSeed>,<lastSeed>,<turns>[,players[,withheld]]: heuristic rollouts written as NPY
    // shards, one per world. withheld is SimulationConfig.TrainingWithheldTechnologies (capability fixtures).
    public static int Teacher(string outDir, int firstSeed, int lastSeed, int turns, int players = 8, int withheld = 0)
    {
        Directory.CreateDirectory(outDir);
        long totalDecisions = 0; double nativeMs = 0;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        for (int seed = firstSeed; seed <= lastSeed; seed++)
        {
            var snapshot = ElderModel9Environment.GenerateOpening(seed, players, out _, 40, withheld);
            var env = new ElderModel9Environment { WithheldTechnologies = withheld };
            var reset = env.Reset(snapshot, true);
            var rows = new List<(int Turn, ElderModel9Village Decision, ElderModel9Tensor Obs, float[] Privileged)>();
            var pending = reset.Villages.Where(v => v.Observation != null).ToDictionary(v => v.Camp, v => (v.Observation, v.Privileged));
            int turn0 = env.World.Turn;
            var outcomes = new List<(int Turn, ElderModel9Village V)>();
            for (int t = 0; t < turns; t++)
            {
                int turn = env.World.Turn;
                var step = env.Step(new Dictionary<int, ElderDirective>());
                nativeMs += step.NativeMilliseconds;
                foreach (var v in step.Villages)
                {
                    if (pending.TryGetValue(v.Camp, out var obs) && v.Teacher != null) rows.Add((turn, v, obs.Observation, obs.Privileged));
                }
                totalDecisions += step.Villages.Count(v => v.Teacher != null);
                pending = step.Villages.Where(v => v.Observation != null && !v.Terminated).ToDictionary(v => v.Camp, v => (v.Observation, v.Privileged));
            }
            Write(Path.Combine(outDir, "world-" + seed.ToString("D5")), seed, rows, withheld);
            Console.WriteLine("MODEL9_TEACHER seed=" + seed + " rows=" + rows.Count + " elapsed=" + I(clock.Elapsed.TotalSeconds));
        }
        Console.WriteLine("MODEL9_TEACHER_SUMMARY worlds=" + (lastSeed - firstSeed + 1) + " decisions=" + totalDecisions
            + " nativeDecisionsPerSecond=" + I(totalDecisions / Math.Max(1e-9, nativeMs / 1000))
            + " wallDecisionsPerSecond=" + I(totalDecisions / Math.Max(1e-9, clock.Elapsed.TotalSeconds)));
        return 0;
    }

    private static void Write(string dir, int seed, List<(int Turn, ElderModel9Village V, ElderModel9Tensor Obs, float[] Privileged)> rows, int withheld = 0)
    {
        Directory.CreateDirectory(dir);
        int n = rows.Count, core = ElderModel9Contract.CoreWidth, services = ElderModel9Contract.GoodsServices.Length;
        NpyWriter.Write(Path.Combine(dir, "core.npy"), rows.SelectMany(r => r.Obs.Core).ToArray(), n, core);
        NpyWriter.Write(Path.Combine(dir, "core_present.npy"), rows.SelectMany(r => r.Obs.CorePresent).ToArray(), n, core);
        NpyWriter.Write(Path.Combine(dir, "privileged.npy"), rows.SelectMany(r => r.Privileged).ToArray(), n, ElderModel9Contract.PrivilegedFeatures.Length);
        for (int s = 0; s < ElderModel9Contract.Sets.Length; s++)
        {
            var set = ElderModel9Contract.Sets[s];
            NpyWriter.Write(Path.Combine(dir, set.Name + ".npy"), rows.SelectMany(r => r.Obs.Sets[s]).ToArray(), n, set.Rows, set.Width);
            NpyWriter.Write(Path.Combine(dir, set.Name + "_present.npy"), rows.SelectMany(r => r.Obs.RowPresent[s]).ToArray(), n, set.Rows);
        }
        NpyWriter.Write(Path.Combine(dir, "movement_mask.npy"), rows.SelectMany(r => r.Obs.MovementMask).ToArray(), n, ElderModel9Contract.MovementSlots);
        NpyWriter.Write(Path.Combine(dir, "clothing_route_mask.npy"), rows.SelectMany(r => r.Obs.ClothingRouteMask).ToArray(), n, 2);
        NpyWriter.Write(Path.Combine(dir, "shelter_route_mask.npy"), rows.SelectMany(r => r.Obs.ShelterRouteMask).ToArray(), n, 2);
        NpyWriter.Write(Path.Combine(dir, "economy_mix_mask.npy"), rows.SelectMany(r => r.Obs.EconomyMixMask).ToArray(), n, 3);
        var labels = rows.Select(r => r.V.Teacher).ToArray();
        NpyWriter.Write(Path.Combine(dir, "strategic.npy"), labels.SelectMany(l => new[] { (float)l.Action.RetainedFieldOperation, (float)l.Action.RetainedHerdFraction,
            (float)l.Action.DevelopmentBudget, (float)l.Action.FieldDevelopmentShare, (float)l.Action.FishingDevelopmentShare }).ToArray(), n, 5);
        NpyWriter.Write(Path.Combine(dir, "strategic_mask.npy"), labels.SelectMany(l => new[] { l.StrategicFieldMask, l.StrategicHerdMask, l.DevelopmentMask, l.DevelopmentShareMask, l.FishingShareMask }).ToArray(), n, 5);
        NpyWriter.Write(Path.Combine(dir, "service_levels.npy"), labels.SelectMany(l => l.Action.ServiceLevels.Select(x => (float)x)).ToArray(), n, services);
        NpyWriter.Write(Path.Combine(dir, "service_mask.npy"), labels.SelectMany(l => l.ServiceMask).ToArray(), n, services);
        NpyWriter.Write(Path.Combine(dir, "clothing_route.npy"), labels.Select(l => l.Action.ClothingRoute).ToArray(), n);
        NpyWriter.Write(Path.Combine(dir, "clothing_route_label_mask.npy"), labels.Select(l => l.ClothingRouteMask).ToArray(), n);
        NpyWriter.Write(Path.Combine(dir, "shelter_route.npy"), labels.Select(l => l.Action.ShelterRoute).ToArray(), n);
        NpyWriter.Write(Path.Combine(dir, "shelter_route_label_mask.npy"), labels.Select(l => l.ShelterRouteMask).ToArray(), n);
        NpyWriter.Write(Path.Combine(dir, "movement.npy"), labels.Select(l => l.Action.MovementCandidate + 1).ToArray(), n);
        NpyWriter.Write(Path.Combine(dir, "movement_label_mask.npy"), labels.Select(l => l.MovementMask).ToArray(), n);
        NpyWriter.Write(Path.Combine(dir, "fission_policy.npy"), labels.Select(l => l.Action.FissionPolicy).ToArray(), n);
        NpyWriter.Write(Path.Combine(dir, "repair.npy"), labels.Select(l => (float)l.RepairSize).ToArray(), n);
        NpyWriter.Write(Path.Combine(dir, "camp.npy"), rows.Select(r => r.V.Camp).ToArray(), n);
        NpyWriter.Write(Path.Combine(dir, "turn.npy"), rows.Select(r => r.Turn).ToArray(), n);
        NpyWriter.Write(Path.Combine(dir, "society.npy"), rows.Select(r => r.V.Society).ToArray(), n);
        NpyWriter.Write(Path.Combine(dir, "parent_society.npy"), rows.Select(r => r.V.ParentSociety).ToArray(), n);
        NpyWriter.Write(Path.Combine(dir, "prior_population.npy"), rows.Select(r => r.V.PriorPopulation).ToArray(), n);
        NpyWriter.Write(Path.Combine(dir, "population.npy"), rows.Select(r => r.V.Population).ToArray(), n);
        NpyWriter.Write(Path.Combine(dir, "qol.npy"), rows.Select(r => (float)r.V.QoL).ToArray(), n);
        NpyWriter.Write(Path.Combine(dir, "nutrition.npy"), rows.Select(r => (float)r.V.Nutrition).ToArray(), n);
        NpyWriter.Write(Path.Combine(dir, "years.npy"), rows.Select(r => (float)r.V.Years).ToArray(), n);
        NpyWriter.Write(Path.Combine(dir, "welfare.npy"), rows.Select(r => (float)r.V.Welfare).ToArray(), n);
        NpyWriter.Write(Path.Combine(dir, "terminated.npy"), rows.Select(r => r.V.Terminated).ToArray(), n);
        File.WriteAllText(Path.Combine(dir, "meta.json"), "{\"seed\": " + seed + ", \"rows\": " + n + ", \"contract\": " + ElderModel9Contract.NativeContractVersion
            + ", \"withheldTechnologies\": " + withheld + ", \"coreWidth\": " + core + ", \"buildHash\": \"" + ElderModel9Environment.BuildHash + "\"}\n");
    }
}
