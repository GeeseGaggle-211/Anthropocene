using System;
using System.Collections.Generic;
using System.Linq;
using Anthropocene.Simulation;

// WP4b: expansion labour valued on the allocator's step ladder with a bucketed food value.
// Owns its worlds and counters.
internal static class ExpansionClaimChecks
{
    private static void Check(ref int checks, bool ok, string message) { checks++; if (!ok) throw new Exception("Expansion claim: " + message); }
    private static bool Info => Environment.GetEnvironmentVariable("WP4B_INFO") == "1";

    // Value-rule Agricultural fixture; `richness` scales the tile stock so the same camp can be food-short.
    private static WorldState Agricultural(int population, double richness = 1)
    {
        var w = NeolithicChecks.Fixture();
        int count = w.Tiles.Length;
        w.Resources = new WorldResourceState { MaximumQuantity = 100000, CatalogHash = ResourceCatalog.Hash, ProfileHash = ResourceCatalog.Hash, ConfigurationHash = ResourceCatalog.Hash, Provinces = Array.Empty<GeologicalProvince>(), Province = new ushort[count], History = new ushort[count], Host = new byte[count], Marine = new byte[count], Stone = new ushort[count], Clay = new ushort[count], Wood = new ushort[count], WoodRecoveryPerYear = .05 };
        var c = w.Camps[0]; int tile = c.Tile;
        w.Resources.Stone[tile] = 1000; w.Resources.Clay[tile] = 1000; w.Resources.Wood[tile] = 1000;
        var t = w.Tiles[tile]; t.CultivationCapital = .1; t.Cultivation = (float)CultivationEconomy.Intensity(w.Config, .1); t.CultivationOperator = c.Id;
        c.Inventory.HandTools = 1000; c.Population = population; c.DemographicGrowthMultiplier = 1; w.Config.AnnualHumanGrowthRate = new SimulationConfig().AnnualHumanGrowthRate;
        foreach (string tech in NeolithicTech.FirstTier) c.People.NeolithicTechnologies.Add(tech);
        w.Config.Neolithic.PlannerFulfilment = "Value";
        // The planner fixtures pin the cohesion scale at 1,780 people whatever the housing (band 1,780, housing gain 0): they test the
        // ladder valuation at a lightly crowded village, independent of the derived-scale calibration (QoLCohesionChecks covers that).
        w.Config.Neolithic.CohesionBandScale = 1780; w.Config.Neolithic.CohesionHousingGain = 0;
        if (richness != 1) foreach (var tl in w.Tiles) { tl.Food *= (float)richness; tl.PotentialFood *= (float)richness; for (int s = 0; s < 4; s++) { tl.Seasons[s].MaxFood *= (float)richness; tl.Seasons[s].MaxPotentialFood *= (float)richness; } }
        return w;
    }

    private static string Sig(EconomicIntent e) => string.Join(",", e.Assignments.OrderBy(a => a.Role, StringComparer.Ordinal).Select(a => a.Role + a.Population)) + "|" + Math.Round(e.Labor?.MaterialGatherers ?? 0, 9) + "|" + Math.Round(e.Labor?.ExpansionFarmers ?? 0, 9);
    private static int Crafts(EconomicIntent e) => e.Assignments.Where(a => a.Role != WorkforceRoles.Farmer).Sum(a => a.Population);
    private static bool Near(double a, double b, double rel = 1e-9) => Math.Abs(a - b) <= rel * Math.Max(1, Math.Max(Math.Abs(a), Math.Abs(b)));

    // Independent W(N) = N Q(N): goods dilution, friction and cohesion, written out from the design.
    private static double W(WorldState w, Camp c, double n, double n0)
    {
        var cfg = w.Config; var x = cfg.Neolithic;
        double e = c.RudimentaryExpectation > 0 ? c.RudimentaryExpectation : x.RudimentaryExpectationFloor + (1 - x.RudimentaryExpectationFloor) * Math.Min(1, Math.Max(0, c.StationaryYears / x.RudimentaryExpectationRampYears));
        // Derived scale, written out: N_s = N_band (1 + k H), H = min(1, (fixed people + p_tent * tent people) / N).
        double fixedPeople = NeolithicProduction.FixedHousingAt(w, c.Id, c.Tile, w.Days) * x.HousingPeoplePerUnit, tentPeople = Math.Max(0, c.Inventory.LightHousingUnits) * x.LightHousingPeoplePerUnit;
        double ns = x.CohesionBandScale * (1 + x.CohesionHousingGain * Math.Min(1, (fixedPeople + x.LightHousingPermanence * tentPeople) / n));
        double sCoh = Math.Exp(-Math.Pow(n / ns, x.SocialCohesionExponent));
        double goods = x.RudimentaryWeightClothing * (1 - e * (1 - Math.Min(1, c.ClothPerCapita * n0 / n))) + x.RudimentaryWeightHousing * (1 - e * (1 - Math.Min(1, c.HousingPerCapita * n0 / n)));
        return n * c.FoodNeedsMetRatio * c.WaterNeedsMetRatio * sCoh * goods;
    }

    private static CultivationEconomy.ExpansionStep St(double score, double gain, double wy)
        => new CultivationEconomy.ExpansionStep { Tile = 0, Score = score, AnnualGain = gain, WorkerYears = wy };

    public static int Run()
    {
        int checks = 0;
        var rule = new ValueFulfilment(new SimulationConfig().Neolithic.ValueCurveSteepness);
        Check(ref checks, new SimulationConfig().Neolithic.PersonLifeYears == 30 && new NeolithicConfig().PersonLifeYears == 30, "PersonLifeYears defaults to 30");
        var bad = new SimulationConfig(); bad.Neolithic.PersonLifeYears = 0;
        bool rejected = false; try { bad.Neolithic.Validate(); } catch (ArgumentException) { rejected = true; }
        Check(ref checks, rejected, "PersonLifeYears must be positive");

        // ---- The ladder is the resolver allocator's own step sequence.
        var w = Agricultural(100); var c = w.Camps[0]; c.FoodNeedsMetRatio = .2; c.QoL = .95; /* above Q*, so the growth ceiling is positive and a birth has a price */
        var cfg = w.Config; var n = cfg.Neolithic; double years = cfg.TurnDays / cfg.DaysPerYear;
        string before = SaveCodec.Fingerprint(w);
        var steps = CultivationEconomy.ExpansionLadder(w, c, years);
        Check(ref checks, before == SaveCodec.Fingerprint(w), "building the ladder does not mutate the world");
        Check(ref checks, steps.Length >= 4 && steps.All(x => x.Score > 0 && x.WorkerYears > 0 && x.AnnualGain > 0), "the ladder has positive steps (" + steps.Length + ")");
        double total = steps.Sum(x => x.WorkerYears);
        HashSet<int> Raised(HorizonCultivationAllocation a)
        {
            var set = new HashSet<int>();
            for (int i = 0; i < a.Tiles.Length; i++)
                if (a.EndCapital[i] > CultivationEconomy.Decay(cfg, a.StartCapital[i], a.StartOperatingFractions[i], years) + 1e-12) set.Add(a.Tiles[i]);
            return set;
        }
        var all = CultivationEconomy.PlanHorizonExpansion(w, c, years, total * 10, 0, null);
        Check(ref checks, Near(all.FarmerWorkerYearsUsed, total, 1e-9), "a large effort uses exactly the ladder's total worker-years (" + all.FarmerWorkerYearsUsed + " vs " + total + ")");
        Check(ref checks, Raised(all).SetEquals(steps.Select(x => x.Tile)), "the allocator raises exactly the ladder's fields");
        foreach (int k in new[] { 1, 2, steps.Length / 2, steps.Length - 1 })
        {
            double effort = steps.Take(k).Sum(x => x.WorkerYears);
            var part = CultivationEconomy.PlanHorizonExpansion(w, c, years, effort, 0, null);
            Check(ref checks, Near(part.FarmerWorkerYearsUsed, effort, 1e-9), "the first " + k + " ladder steps reproduce the allocator's used effort");
            Check(ref checks, Raised(part).SetEquals(steps.Take(k).Select(x => x.Tile)), "the allocator's first " + k + " steps raise the ladder's first " + k + " fields");
        }
        var half = CultivationEconomy.PlanHorizonExpansion(w, c, years, steps[0].WorkerYears * .5, 0, null);
        Check(ref checks, Near(half.FarmerWorkerYearsUsed, steps[0].WorkerYears * .5, 1e-9), "half the first step is used in full (partial step)");
        // A field another camp operates leaves the ladder exactly as it leaves the allocator.
        var foreign = Agricultural(100); foreign.Camps[0].FoodNeedsMetRatio = .2;
        int foreignTile = CultivationEconomy.ExpansionLadder(foreign, foreign.Camps[0], years)[0].Tile;
        foreign.Tiles[foreignTile].CultivationOperator = foreign.Camps[0].Id + 99;
        Check(ref checks, CultivationEconomy.ExpansionLadder(foreign, foreign.Camps[0], years).All(x => x.Tile != foreignTile), "a field operated by another camp is not on the ladder");

        // ---- Food per birth is the derivative of GrowthFood with respect to final population.
        double cTurn = cfg.Consumption * cfg.GenerationIntervals;
        foreach (double rate in new[] { .002, .01145, .03, .08 })
        {
            double n0 = 1000, h = rate * 1e-4;
            double Gf(double r) => TurnResolver.GrowthFood(n0 * cTurn, r, years);
            double Nf(double r) => n0 * Math.Exp(r * years);
            double numeric = (Gf(rate + h) - Gf(rate - h)) / (Nf(rate + h) - Nf(rate - h));
            double closed = ExpansionValuation.FoodPerBirth(cTurn, rate, years);
            Check(ref checks, Math.Abs(numeric - closed) <= 1e-5 * closed, "food per birth matches dGrowthFood/dN_final at rate " + rate + " (" + closed + " vs " + numeric + ")");
        }
        Check(ref checks, Math.Abs(ExpansionValuation.FoodPerBirth(cTurn, 1e-9, years) / cTurn - .5) < 1e-6 && Math.Abs(ExpansionValuation.FoodPerBirth(cTurn, 1e-5, years) / cTurn - .5) < 1e-3, "food per birth tends to half a turn's ration as the rate goes to zero");
        Check(ref checks, ExpansionValuation.FoodPerBirth(cTurn, 0, years) == 0 && ExpansionValuation.FoodPerBirth(cTurn, -1, years) == 0, "no growth rate: no food per birth");
        Check(ref checks, Math.Abs(ExpansionValuation.FoodPerBirth(cTurn, 1e-4 / years, years) - ExpansionValuation.FoodPerBirth(cTurn, 1.0001e-4 / years, years)) < 1e-4 * cTurn, "food per birth is continuous across the series switch");

        // ---- dW/dN closed form, goods dilution and friction included.
        foreach (int pop in new[] { 50, 100, 400 })
        {
            var mw = Agricultural(pop); var mc = mw.Camps[0]; mc.ClothPerCapita = .6; mc.HousingPerCapita = .8; mc.RudimentaryExpectation = .7; mc.FoodNeedsMetRatio = .5; mc.WaterNeedsMetRatio = .9;
            double expected = W(mw, mc, pop + 1, pop) - W(mw, mc, pop, pop);
            Check(ref checks, Near(ExpansionValuation.MarginalWelfare(mw, mc), expected, 1e-12), "dW/dN closed form at " + pop);
        }
        {
            // W(N0) is N0 times the camp's own QoL formula at the committed coverages.
            var qw = Agricultural(100); var qc = qw.Camps[0]; qc.ClothPerCapita = .6; qc.HousingPerCapita = .8; qc.RudimentaryExpectation = .7;
            double qol = qc.FoodNeedsMetRatio * qc.WaterNeedsMetRatio * (n.RudimentaryWeightClothing * (1 - .7 * (1 - .6)) + n.RudimentaryWeightHousing * (1 - .7 * (1 - .8))) * NeolithicProduction.SocialCohesion(qw, qc, 100);
            Check(ref checks, Near(ExpansionValuation.Welfare(qw, qc, 100, 100), 100 * qol, 1e-12), "W(N0) = N0 Q at the committed coverages");
            // One more person dilutes the goods and pays the friction: dW/dN is below Q while coverage is short.
            Check(ref checks, ExpansionValuation.MarginalWelfare(qw, qc) < qol, "dW/dN < Q when goods are short");
        }

        // ---- Demand pass: buckets, growth value and the gates.
        var d = CampProductionDemand.Build(w, c, EconomicIntent.From(c));
        Check(ref checks, before == SaveCodec.Fingerprint(w), "the demand pass does not mutate the world");
        TurnResolver.FoodBucketTargets(w, c, cfg.GenerationIntervals, years, d.Consumers * cfg.Consumption * cfg.GenerationIntervals, c.Food, out double cons, out _, out double fill, out double maxGrowth);
        double clothFeed = d.Goals[CraftGood.Cloth] * n.PlantFoodPerFabricUnit;
        Check(ref checks, Near(cons + fill + maxGrowth, d.Biological, 1e-12), "the buckets sum to the planning food demand");
        Check(ref checks, Near(d.GapSecurityPerYear, Math.Max(0, cons + fill - d.Supply) / years, 1e-12), "security gap per year");
        Check(ref checks, Near(d.GapConsumptionPerYear, Math.Max(0, cons - d.Supply) / years, 1e-12) && d.GapConsumptionPerYear <= d.GapSecurityPerYear, "consumption gap per year");
        Check(ref checks, Near(d.GapTotalPerYear, Math.Max(0, d.Biological + clothFeed - d.Supply) / years, 1e-12), "total gap per year equals the existing forecast gap");
        Check(ref checks, d.GapTotalPerYear >= d.GapSecurityPerYear && d.GapTotalPerYear > 0, "gaps are ordered");
        double dYear = c.Population * cfg.Consumption * cfg.DaysPerYear / cfg.BaseDays;
        Check(ref checks, Near(d.FoodValue, c.Population * c.QoL / (c.FoodNeedsMetRatio * dYear), 1e-12), "v_food = N Q / (F D_year)");
        Check(ref checks, Near(d.CapitalLife, 1 / (Math.Log(2) / n.DecayHalfLifeYears + n.DiscountRatePerYear), 1e-12) && Near(d.PersonLife, 1 / (1 / n.PersonLifeYears + n.DiscountRatePerYear), 1e-12), "L_cap and L_person");
        double maxRate = QoLMortality.MaximumRate(w, c);
        Check(ref checks, Near(d.FoodPerBirth, ExpansionValuation.FoodPerBirth(cTurn, maxRate, years), 1e-12), "food per birth from the camp's maximum rate");
        Check(ref checks, Near(d.GrowthFoodValue, Math.Max(0, d.MarginalWelfarePerBirth) * d.PersonLife / d.FoodPerBirth, 1e-12) && d.GrowthFoodValue > 0, "v_growth = max(0, dW/dN) L_person / food per birth");
        Check(ref checks, d.Expansion != null && d.Expansion.Any, "a forecast shortfall with a retained gain gives an expansion ladder");
        Check(ref checks, d.Expansion.TotalWorkerYears <= d.LadderWorkerYearsUntruncated + 1e-9, "truncation only shortens the ladder");
        {
            // Stopping the recording at the total gap changes nothing the valuation reads.
            var fullSteps = CultivationEconomy.ExpansionLadder(w, c, years, d.Footprint);
            var full = ExpansionValuation.Value(fullSteps, d.GapSecurityPerYear, d.GapTotalPerYear, d.FoodValue, d.GrowthFoodValue, d.CapitalLife);
            Check(ref checks, d.LadderSteps <= fullSteps.Length && full.Value.SequenceEqual(d.Expansion.Value) && full.WorkerYears.SequenceEqual(d.Expansion.WorkerYears), "capping the recording at the total gap gives the same valued ladder (" + d.LadderSteps + " of " + fullSteps.Length + " steps computed)");
        }
        Check(ref checks, d.Expansion.Value.Zip(d.Expansion.Value.Skip(1), (a, b) => a >= b).All(x => x), "u is nonincreasing");
        {
            var actual = new double[d.Claims.Count]; double last = 0; bool okShares = true;
            for (int step = 0; step <= 64; step++)
            {
                rule.Shares(d.Claims, d.Expansion, step / 64.0, actual, out double wy);
                if (step == 0) okShares &= wy == 0;
                okShares &= wy >= last - 1e-12 && wy <= d.Expansion.TotalWorkerYears + 1e-9; last = wy;
            }
            Check(ref checks, okShares, "the demand's own ladder buys nondecreasing worker-years from 0");
        }
        if (Info)
            Console.WriteLine("WP4B_INFO fixture pop=100 F=.2 Q=.8 gapSec/yr=" + d.GapSecurityPerYear.ToString("F1") + " gapTotal/yr=" + d.GapTotalPerYear.ToString("F1") + " v_food=" + d.FoodValue.ToString("F4") + " v_growth=" + d.GrowthFoodValue.ToString("F4") + " dW/dN=" + d.MarginalWelfarePerBirth.ToString("F4") + " foodPerBirth=" + d.FoodPerBirth.ToString("F1") + " L_cap=" + d.CapitalLife.ToString("F1") + " L_person=" + d.PersonLife.ToString("F1") + " ladderSteps=" + d.LadderSteps + " raw=" + d.LadderWorkerYearsUntruncated.ToString("F1") + "wy kept=" + d.Expansion.Value.Length + " total=" + d.Expansion.TotalWorkerYears.ToString("F1") + "wy u=[" + d.Expansion.Value.First().ToString("G4") + ".." + d.Expansion.Value.Last().ToString("G4") + "] residents*years=" + (c.Population * years).ToString("F0") + " | craft first-unit values per worker-year " + string.Join(" ", d.Claims.Where(ValueFulfilment.IsNeed).Select(x => x.Good + "=" + (x.FirstUnitValue / x.LabourPerUnit).ToString("F3"))));

        // Zero growth rate or a negative marginal welfare leaves only the security bucket.
        var nw = Agricultural(100); nw.Camps[0].FoodNeedsMetRatio = .2; nw.Camps[0].DeficitTurns = 1;
        var nd = CampProductionDemand.Build(nw, nw.Camps[0], EconomicIntent.From(nw.Camps[0]));
        Check(ref checks, nd.FoodPerBirth == 0 && nd.GrowthFoodValue == 0, "a camp with deficit turns has no growth bucket value (maxRate = 0)");
        var bw = Agricultural(3000); bw.Camps[0].FoodNeedsMetRatio = .2;
        var bd = CampProductionDemand.Build(bw, bw.Camps[0], EconomicIntent.From(bw.Camps[0]));
        Check(ref checks, bd.MarginalWelfarePerBirth <= 0 && bd.GrowthFoodValue == 0, "beyond the cohesion scale dW/dN <= 0 gives v_growth = 0 (" + bd.MarginalWelfarePerBirth + ")");

        // ---- Bucket piecewise valuation, truncation and the running minimum (pure).
        {
            var list = Enumerable.Range(0, 10).Select(i => St(1, 4, 3)).ToArray();
            var l = ExpansionValuation.Value(list, 10, 25, 8, 2, 50);
            double[] expected = { 400, 400, 250, 100, 100, 100, 25 };
            Check(ref checks, l.Value.Length == expected.Length && Enumerable.Range(0, expected.Length).All(i => Near(l.Value[i], expected[i], 1e-12)), "piecewise bucket values [" + string.Join(",", l.Value) + "]");
            Check(ref checks, l.WorkerYears.All(x => x == 3) && l.TotalWorkerYears == 21, "worker-years of the kept steps");
            Check(ref checks, ExpansionValuation.Value(list, 10, 24, 8, 2, 50).Value.Length == 6, "the ladder stops once cumulative food reaches the total gap");
            var noGrowth = ExpansionValuation.Value(list, 10, 25, 8, 0, 50);
            Check(ref checks, noGrowth.Value.Length == 3 && Near(noGrowth.Value[2], 50 * 8 * 2 / 4.0, 1e-12), "with v_growth = 0 the ladder ends inside the security bucket");
            // Consumption [0,4] at 8, reserve [4,10] at 0.8 (rho = .1), growth [10,25] at 2; 4 food per step.
            var split = ExpansionValuation.Value(list, 4, 10, 25, 8, .8, 2, 50);
            double[] expectedSplit = { 400, 40, 40, 40, 40, 40, 25 };
            Check(ref checks, split.Value.Length == expectedSplit.Length && Enumerable.Range(0, expectedSplit.Length).All(i => Near(split.Value[i], expectedSplit[i], 1e-12)), "three buckets: consumption, reserve at rho, growth [" + string.Join(",", split.Value) + "]");
            Check(ref checks, ExpansionValuation.Value(list, 10, 10, 25, 8, 8, 2, 50).Value.SequenceEqual(l.Value), "reserve at full v_food reproduces the two-bucket valuation");
            var none = ExpansionValuation.Value(list, 0, 0, 8, 2, 50);
            Check(ref checks, !none.Any && none.TotalWorkerYears == 0, "no gap: empty ladder");
            var zig = new[] { St(1, 4, 3), St(.5, 4, 3), St(3, 4, 3), St(.1, 4, 3), St(2, 4, 3) };
            var zl = ExpansionValuation.Value(zig, 100, 100, 8, 2, 50);
            Check(ref checks, zl.Value.Length == 5 && zl.Value.Zip(zl.Value.Skip(1), (a, b) => a >= b).All(x => x) && Near(zl.Value[2], zl.Value[1], 1e-12) && zl.Value[0] > zl.Value[1], "the running minimum keeps u nonincreasing");
            var rnd = new Random(77); bool mono = true;
            for (int t = 0; t < 50; t++)
            {
                var r = Enumerable.Range(0, 12).Select(i => St(rnd.NextDouble() * 2, .1 + rnd.NextDouble() * 5, .1 + rnd.NextDouble() * 4)).ToArray();
                double gs = rnd.NextDouble() * 20;
                var rl = ExpansionValuation.Value(r, gs, gs + rnd.NextDouble() * 20, 1 + rnd.NextDouble() * 9, rnd.NextDouble() * 3, 20 + rnd.NextDouble() * 40);
                for (int i = 1; i < rl.Value.Length; i++) mono &= rl.Value[i] <= rl.Value[i - 1];
                mono &= rl.Value.All(x => x > 0) && rl.WorkerYears.Length == rl.Value.Length;
            }
            Check(ref checks, mono, "random ladders are nonincreasing and positive");
            var vl = new ExpansionLadderClaim { WorkerYears = new[] { 2.0, 3, 5 }, Value = new[] { 9.0, 5, 1 }, TotalWorkerYears = 10 };
            Check(ref checks, vl.ValueAfter(0) == 9 && vl.ValueAfter(1.9) == 9 && vl.ValueAfter(2) == 5 && vl.ValueAfter(4.99) == 5 && vl.ValueAfter(5) == 1 && vl.ValueAfter(9.99) == 1 && vl.ValueAfter(10) == 0 && vl.ValueAfter(50) == 0, "the price after covered worker-years is the next step's value; 0 when exhausted");
        }

        // ---- The rule with a ladder: 0 at s = 0, nondecreasing, craft path unchanged, exact ends.
        var claims = new List<ProductionClaim>();
        double[] v = { 8, 4, 2 };
        for (int i = 0; i < 3; i++)
            claims.Add(new ProductionClaim { Good = (CraftGood)i, Quantity = 30 + 10 * i, Stock = 5 * i, Need = 35 + 10 * i, LabourPerUnit = 1 + i, FirstUnitValue = v[i] * (1 + i), Value = v[i] * (1 + i), Priority = 1, Valued = true });
        double hiCraftProbe = claims.Max(x => x.FirstUnitValue / x.LabourPerUnit);
        foreach (double top in new[] { .05, 1.5, 7, 40 })
        {
            var ladder = new ExpansionLadderClaim { WorkerYears = new[] { 40.0, 60, 120, 300 }, Value = new[] { top, top * .6, top * .3, top * .05 } };
            ladder.TotalWorkerYears = ladder.WorkerYears.Sum();
            var craftOnly = new double[claims.Count]; var withLadder = new double[claims.Count];
            double previous = 0; var prev = new double[claims.Count]; bool ok = true, none = true, unchanged = true, bounded = true;
            for (int step = 0; step <= 64; step++)
            {
                double s = step / 64.0;
                rule.Shares(claims, s, craftOnly);
                rule.Shares(claims, ladder, s, withLadder, out double wy);
                if (step == 0) none &= wy == 0 && withLadder.All(x => x == 0);
                ok &= wy >= previous - 1e-12 && wy >= 0;
                bounded &= wy <= ladder.TotalWorkerYears + 1e-9;
                for (int i = 0; i < claims.Count; i++) { ok &= withLadder[i] >= prev[i] - 1e-12; prev[i] = withLadder[i]; }
                if (top <= hiCraftProbe) for (int i = 0; i < claims.Count; i++) unchanged &= craftOnly[i] == withLadder[i];
                previous = wy;
            }
            Check(ref checks, ok && none && bounded, "ladder worker-years are 0 at s = 0, nondecreasing and within the total (top " + top + ")");
            Check(ref checks, unchanged, "a ladder no more valuable than the best craft leaves the craft path unchanged (top " + top + ")");
            rule.Shares(claims, ladder, .5, withLadder, out double wy50);
            double price = rule.LaborPrice(claims, ladder, .5);
            double expectedWy = 0; for (int i = 0; i < ladder.Value.Length && ladder.Value[i] >= price; i++) expectedWy += ladder.WorkerYears[i];
            Check(ref checks, Near(wy50, expectedWy, 1e-12), "worker-years at the price are the prefix with u >= w (top " + top + ")");
        }
        var emptyClaims = new List<ProductionClaim>(); var alone = new double[0];
        var lone = new ExpansionLadderClaim { WorkerYears = new[] { 5.0, 5 }, Value = new[] { 4.0, 1 }, TotalWorkerYears = 10 };
        rule.Shares(emptyClaims, lone, 0, alone, out double a0); rule.Shares(emptyClaims, lone, 1, alone, out double a1); rule.Shares(emptyClaims, lone, .5, alone, out double ah);
        Check(ref checks, a0 == 0 && a1 == 10 && ah >= 5 - 1e-9 && ah <= 10 && !double.IsNaN(rule.LaborPrice(emptyClaims, lone, .5)), "with no craft claim the ladder owns the path (0 at s=0, total at s=1)");

        // ---- Expansion is present only under Value, cultivation and a shortfall with a retained gain.
        var uw = Agricultural(100); uw.Config.Neolithic.PlannerFulfilment = "Uniform"; uw.Camps[0].FoodNeedsMetRatio = .2;
        var ud = CampProductionDemand.Build(uw, uw.Camps[0], EconomicIntent.From(uw.Camps[0]));
        Check(ref checks, ud.Expansion == null && ud.GapTotalPerYear == 0 && ud.LadderSteps == 0, "the Uniform config builds no expansion ladder");
        EconomicPlanner.Plan(uw, uw.Camps[0], EconomicIntent.From(uw.Camps[0]), EconomicPlanner.ConfiguredRule(uw.Config), out _, out _, out double uFarm);
        Check(ref checks, uFarm == 0, "Uniform reports no farm-labour price");
        var gw = Agricultural(60); gw.Config.AnnualHumanGrowthRate = 0; var gc = gw.Camps[0]; gc.FoodNeedsMetRatio = .2;
        var gd = CampProductionDemand.Build(gw, gc, EconomicIntent.From(gc));
        Check(ref checks, gd.Biological * gd.NextPopulation / gc.Population + gd.Goals[CraftGood.Cloth] * gw.Config.Neolithic.PlantFoodPerFabricUnit <= gd.Supply, "gate fixture has no forecast shortfall");
        Check(ref checks, gd.Expansion == null && gd.LadderSteps == 0, "no shortfall: no ladder");
        var gPlan = EconomicPlanner.Plan(gw, gc, EconomicIntent.From(gc), EconomicPlanner.ConfiguredRule(gw.Config), out double gPrice, out _, out double gFarm, true);
        var gRef = EconomicPlanner.Plan(gw, gc, EconomicIntent.From(gc), EconomicPlanner.ConfiguredRule(gw.Config), out double gRefPrice, out _, false);
        Check(ref checks, Sig(gPlan) == Sig(gRef) && gPrice == gRefPrice && gFarm == 0, "gate false: the plan equals the pre-WP4b Value plan and w_farm = 0");
        var noGain = Agricultural(100); noGain.Camps[0].FoodNeedsMetRatio = .2;
        foreach (var tl in noGain.Tiles) { tl.CultivationCapital = 1 / (noGain.Config.ReferenceIntensity * noGain.Config.ReferenceIntensity); tl.Cultivation = 1; tl.CultivationOperator = noGain.Camps[0].Id; }
        var ngd = CampProductionDemand.Build(noGain, noGain.Camps[0], EconomicIntent.From(noGain.Camps[0]));
        Check(ref checks, ngd.Expansion == null, "fully built fields have no retained gain and no ladder");

        // ---- Plans across labour regimes. The Info lines record magnitudes for the report.
        foreach (double food in new[] { 1.0, .2 })
            foreach (int pop in new[] { 100, 160, 300, 1000 })
            {
                var sw = Agricultural(pop); var sc = sw.Camps[0]; sc.FoodNeedsMetRatio = food; sc.QoL = food == 1 ? 1 : .8;
                string beforePlan = SaveCodec.Fingerprint(sw);
                var sd = CampProductionDemand.Build(sw, sc, EconomicIntent.From(sc));
                var with = EconomicPlanner.Plan(sw, sc, EconomicIntent.From(sc), EconomicPlanner.ConfiguredRule(sw.Config), out double pw, out _, out double farm, true);
                var without = EconomicPlanner.Plan(sw, sc, EconomicIntent.From(sc), EconomicPlanner.ConfiguredRule(sw.Config), out double po, out _, false);
                Check(ref checks, beforePlan == SaveCodec.Fingerprint(sw), "planning does not mutate the world");
                Check(ref checks, with.Assignments.Sum(a => a.Population) <= sc.Population, "plan (" + pop + ", F=" + food + ") stays within residents");
                Check(ref checks, with.Labor.ExpansionFarmers <= with.Assignments.Where(a => a.Role == WorkforceRoles.Farmer).Sum(a => a.Population) + 1e-9, "expansion farmers are Farmer assignments");
                double covered = (with.Labor?.ExpansionFarmers ?? 0) * years;
                Check(ref checks, farm == (sd.Expansion == null ? 0 : sd.Expansion.ValueAfter(covered)), "w_farm is the first uncovered ladder step's value");
                if (sd.Expansion != null) Check(ref checks, farm >= 0 && farm <= sd.Expansion.Value[0] + 1e-12, "w_farm lies on the ladder");
                if (food == 1)
                {
                    // Slack labour: leftover residents already exhaust the affordable ladder, so the plan is the pre-WP4b Value plan.
                    Check(ref checks, Sig(with) == Sig(without) && pw == po, "slack labour (" + pop + " residents): plan equals the pre-WP4b Value plan (" + Sig(with) + ")");
                    Check(ref checks, with.Labor.ExpansionFarmers > 0, "slack labour still expands with the leftover");
                }
                else
                {
                    // Scarce labour: affordable ladder steps enter the search and trade against crafts.
                    Check(ref checks, sd.Expansion != null && with.Labor.ExpansionFarmers >= without.Labor.ExpansionFarmers && Crafts(with) <= Crafts(without) && pw >= po - 1e-12, "scarce labour (" + pop + "): expansion never displaces nothing (" + Sig(with) + " vs " + Sig(without) + ")");
                }
                if (Info)
                    Console.WriteLine("WP4B_INFO plan pop=" + pop + " F=" + food + " claim=" + (sd.Expansion != null) + (sd.Expansion != null ? " kept=" + sd.Expansion.Value.Length + "/" + sd.LadderSteps + " B=" + sd.Expansion.TotalWorkerYears.ToString("F1") + "wy(res*y=" + (pop * years).ToString("F0") + ") u=[" + sd.Expansion.Value[0].ToString("G4") + ".." + sd.Expansion.Value[sd.Expansion.Value.Length - 1].ToString("G4") + "]" : "") + " gapSec=" + sd.GapSecurityPerYear.ToString("F1") + " gapTot=" + sd.GapTotalPerYear.ToString("F1") + " vF=" + sd.FoodValue.ToString("F3") + " vG=" + sd.GrowthFoodValue.ToString("F3") + " | w=" + pw.ToString("G4") + " vs " + po.ToString("G4") + " farm=" + farm.ToString("G4") + " exp=" + with.Labor.ExpansionFarmers + " vs " + without.Labor.ExpansionFarmers + " crafts " + Crafts(with) + " vs " + Crafts(without));
            }

        // ---- Determinism: reversed collections after a save round trip give the same plans.
        var rw = Agricultural(120); rw.Camps[0].FoodNeedsMetRatio = .2;
        var copy = SaveCodec.Load(SaveCodec.Save(rw)); copy.Camps.Reverse(); copy.Hunters.Reverse(); copy.Herds.Reverse();
        var p1 = EconomicPlanner.Plan(rw, rw.Camps[0], EconomicIntent.From(rw.Camps[0]));
        var p2 = EconomicPlanner.Plan(copy, copy.Camps[0], EconomicIntent.From(copy.Camps[0]));
        var p3 = EconomicPlanner.Plan(rw, rw.Camps[0], EconomicIntent.From(rw.Camps[0]));
        Check(ref checks, Sig(p1) == Sig(p2) && Sig(p1) == Sig(p3), "planning is deterministic across repeats and a reversed save round trip");

        Console.WriteLine("PASS: " + checks + " expansion claim assertions");
        return checks;
    }
}
