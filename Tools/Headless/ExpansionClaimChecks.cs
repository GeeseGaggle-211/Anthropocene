using System;
using System.Collections.Generic;
using System.Linq;
using Anthropocene.Simulation;

// WP4b: expansion labour as a value claim. Owns its worlds and counters.
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
        c.Inventory.HandTools = 1000; c.Population = population;
        foreach (string tech in NeolithicTech.FirstTier) c.People.NeolithicTechnologies.Add(tech);
        w.Config.Neolithic.PlannerFulfilment = "Value";
        if (richness != 1) foreach (var tl in w.Tiles) { tl.Food *= (float)richness; tl.PotentialFood *= (float)richness; for (int s = 0; s < 4; s++) { tl.Seasons[s].MaxFood *= (float)richness; tl.Seasons[s].MaxPotentialFood *= (float)richness; } }
        return w;
    }

    // Independent recomputation of g for the fixture: the allocator's first farmer step per operated field.
    private static double ManualG(WorldState w, Camp c, CampProductionDemand d, double years)
    {
        var cfg = w.Config; var n = cfg.Neolithic; double best = 0;
        double ceiling = Math.Pow(1 / cfg.ReferenceIntensity, 2), bridgeCeiling = Math.Pow(cfg.GathererBridgeCeiling / cfg.ReferenceIntensity, 2);
        foreach (int id in d.Operated)
        {
            var tile = w.Tiles[id];
            var view = LazyEcology.ProjectEcology(w, id, Math.Max(w.Days, tile.LastEcologyUpdateDays));
            double e = CultivationEconomy.Decay(cfg, Math.Max(0, view.CultivationCapital), tile.CultivationOperatingFraction, years);
            double q = CultivationEconomy.Intensity(cfg, e);
            double nextE = Math.Pow(Math.Min(1, (Math.Floor(q * 32 + 1e-10) + 1) / 32) / cfg.ReferenceIntensity, 2);
            double de = Math.Min(ceiling - e, Math.Max(0, nextE - e));
            if (e < bridgeCeiling - 1e-12) de = Math.Min(de, bridgeCeiling - e);
            CultivationEconomy.Factors(cfg, tile, out double build, out double maintenance);
            double cost = n.BuildWorkerYearsPerTileE * (q < cfg.GathererBridgeCeiling ? n.OpenBuild : build);
            double score = CultivationEconomy.HorizonMarginalScore(w, tile, view, e, de, years, cost, maintenance);
            if (score > best) best = score;
        }
        return best;
    }

    private static int Crafts(EconomicIntent e) => e.Assignments.Where(a => a.Role != WorkforceRoles.Farmer).Sum(a => a.Population);

    public static int Run()
    {
        int checks = 0;
        var defaults = new SimulationConfig(); var dn = defaults.Neolithic;

        // ---- Closed forms on a food-short fixture (F = 0.2 so the gate holds and the value is visible).
        var w = Agricultural(100); var c = w.Camps[0]; c.FoodNeedsMetRatio = .2; c.QoL = .8;
        var cfg = w.Config; var n = cfg.Neolithic; double years = cfg.TurnDays / cfg.DaysPerYear;
        string before = SaveCodec.Fingerprint(w);
        var d = CampProductionDemand.Build(w, c, EconomicIntent.From(c));
        Check(ref checks, before == SaveCodec.Fingerprint(w), "the demand pass does not mutate the world");
        Check(ref checks, d.Expansion != null && d.ExpansionProductivity > 0, "a forecast shortfall with a retained gain gives an expansion claim");
        Check(ref checks, Math.Abs(d.ExpansionProductivity - ManualG(w, c, d, years)) <= 1e-12 * d.ExpansionProductivity, "g equals the allocator's first-step marginal score (" + d.ExpansionProductivity + " vs " + ManualG(w, c, d, years) + ")");
        double dYear = c.Population * cfg.Consumption * cfg.DaysPerYear / cfg.BaseDays;
        Check(ref checks, Math.Abs(d.FoodValue - c.Population * c.QoL / (c.FoodNeedsMetRatio * dYear)) < 1e-12 && Math.Abs(d.FoodValue - c.QoL / (c.FoodNeedsMetRatio * cfg.Consumption * cfg.DaysPerYear / cfg.BaseDays)) < 1e-12, "v_food = N Q / (F D_year), N cancelling");
        Check(ref checks, Math.Abs(d.CapitalLife - 1 / (Math.Log(2) / n.DecayHalfLifeYears + n.DiscountRatePerYear)) < 1e-12 && d.CapitalLife > 0, "L_cap = 1 / (ln 2 / half-life + r)");
        Check(ref checks, Math.Abs(d.ExpansionValue - d.ExpansionProductivity * d.FoodValue * d.CapitalLife) < 1e-12 * d.ExpansionValue, "V_exp = g v_food L_cap");
        double forecast = d.Biological * d.NextPopulation / c.Population + d.Goals[CraftGood.Cloth] * n.PlantFoodPerFabricUnit;
        Check(ref checks, forecast > d.Supply && Math.Abs(d.ExpansionShortfallPerYear - (forecast - d.Supply) / years) < 1e-9, "shortfall per year is the per-turn gap divided by the turn's years");
        Check(ref checks, Math.Abs(d.ExpansionWorkerYears - d.ExpansionShortfallPerYear / d.ExpansionProductivity) < 1e-9 * d.ExpansionWorkerYears && Math.Abs(d.ExpansionWorkers - d.ExpansionWorkerYears / years) < 1e-9 * d.ExpansionWorkers, "D_exp = shortfall / g worker-years, and D_exp / years workers for the turn");
        var claim = d.Expansion;
        Check(ref checks, claim.Stock == 0 && claim.Quantity == d.ExpansionWorkers && claim.Need == d.ExpansionWorkers && Math.Abs(claim.LabourPerUnit - years) < 1e-12 && Math.Abs(claim.FirstUnitValue - d.ExpansionValue * years) < 1e-9 * claim.FirstUnitValue, "the claim counts workers for the turn: l = years, V = V_exp * years");
        Check(ref checks, Math.Abs(claim.FirstUnitValue / claim.LabourPerUnit - d.ExpansionValue) < 1e-12 * d.ExpansionValue, "V / l is V_exp per worker-year, the craft labour-price unit");
        var rule = new ValueFulfilment(n.ValueCurveSteepness);
        Check(ref checks, Math.Abs(d.FarmLabourPrice - d.ExpansionValue) < 1e-12 * d.ExpansionValue && Math.Abs(d.FarmLabourPrice - rule.MarginalValue(claim, 0) / claim.LabourPerUnit) < 1e-12 * d.ExpansionValue, "w_farm = V_exp e^0 at zero expansion coverage");
        Check(ref checks, !d.Claims.Contains(claim) && d.Claims.All(x => x.Valued ? ClaimValuation.IsNeedGood(x.Good) : true), "the expansion claim is not a craft claim");
        if (Info)
        {
            var cloth = d.Claims.Single(x => x.Good == CraftGood.Cloth); var housing = d.Claims.Single(x => x.Good == CraftGood.Housing);
            Console.WriteLine("WP4B_INFO fixture pop=100 F=.2 Q=.8 g=" + d.ExpansionProductivity.ToString("G4") + " v_food=" + d.FoodValue.ToString("F4") + " L_cap=" + d.CapitalLife.ToString("F2") + " V_exp=" + d.ExpansionValue.ToString("G4") + " shortfall/yr=" + d.ExpansionShortfallPerYear.ToString("F1") + " D_exp=" + d.ExpansionWorkerYears.ToString("F0") + " workers=" + d.ExpansionWorkers.ToString("F0") + " l=" + claim.LabourPerUnit + " Vpw=" + claim.FirstUnitValue.ToString("G4") + " w_farm=" + d.FarmLabourPrice.ToString("G4") + " | Cloth V/l=" + (cloth.FirstUnitValue / cloth.LabourPerUnit).ToString("F2") + " Housing V/l=" + (housing.FirstUnitValue / housing.LabourPerUnit).ToString("F2") + " Cloth V=" + cloth.FirstUnitValue.ToString("F2") + " Housing V=" + housing.FirstUnitValue.ToString("F2"));
        }

        // ---- Expansion is present only under Value, cultivation, and a shortfall with a retained gain.
        var uw = Agricultural(100); uw.Config.Neolithic.PlannerFulfilment = "Uniform"; uw.Camps[0].FoodNeedsMetRatio = .2;
        var ud = CampProductionDemand.Build(uw, uw.Camps[0], EconomicIntent.From(uw.Camps[0]));
        Check(ref checks, ud.Expansion == null && ud.FarmLabourPrice == 0 && ud.ExpansionWorkers == 0 && ud.ExpansionProductivity == 0, "the Uniform config builds no expansion claim");
        var gw = Agricultural(60); var gc = gw.Camps[0]; gc.FoodNeedsMetRatio = .2;
        var gd = CampProductionDemand.Build(gw, gc, EconomicIntent.From(gc));
        Check(ref checks, gd.Biological * gd.NextPopulation / gc.Population + gd.Goals[CraftGood.Cloth] * gw.Config.Neolithic.PlantFoodPerFabricUnit <= gd.Supply, "gate fixture has no forecast shortfall");
        Check(ref checks, gd.Expansion == null && gd.FarmLabourPrice == 0 && gd.ExpansionWorkers == 0 && gd.ExpansionValue == 0, "no shortfall: no claim and w_farm = 0");
        var gPlan = EconomicPlanner.Plan(gw, gc, EconomicIntent.From(gc), EconomicPlanner.ConfiguredRule(gw.Config), out double gPrice, out _, true);
        var gRef = EconomicPlanner.Plan(gw, gc, EconomicIntent.From(gc), EconomicPlanner.ConfiguredRule(gw.Config), out double gRefPrice, out _, false);
        Check(ref checks, Sig(gPlan) == Sig(gRef) && gPrice == gRefPrice, "gate false: the plan equals the pre-WP4b Value plan");
        var noGain = Agricultural(100); noGain.Camps[0].FoodNeedsMetRatio = .2;
        foreach (var tl in noGain.Tiles) { tl.CultivationCapital = 1 / (noGain.Config.ReferenceIntensity * noGain.Config.ReferenceIntensity); tl.Cultivation = 1; tl.CultivationOperator = noGain.Camps[0].Id; }
        var ngd = CampProductionDemand.Build(noGain, noGain.Camps[0], EconomicIntent.From(noGain.Camps[0]));
        Check(ref checks, ngd.Expansion == null && ngd.FarmLabourPrice == 0, "fully built fields have no retained gain and no expansion claim");

        // ---- Slack labour: the plan is the pre-WP4b Value plan, leftover residents expand as before.
        foreach (int pop in new[] { 100, 160, 300 })
        {
            var sw = Agricultural(pop); var sc = sw.Camps[0];
            var sd = CampProductionDemand.Build(sw, sc, EconomicIntent.From(sc));
            var with = EconomicPlanner.Plan(sw, sc, EconomicIntent.From(sc), EconomicPlanner.ConfiguredRule(sw.Config), out double pw, out _, true);
            var without = EconomicPlanner.Plan(sw, sc, EconomicIntent.From(sc), EconomicPlanner.ConfiguredRule(sw.Config), out double po, out _, false);
            Check(ref checks, sd.Expansion != null && sd.ExpansionValue <= pw, "slack fixture " + pop + " has an expansion claim worth no more than the last craft unit");
            Check(ref checks, Sig(with) == Sig(without) && pw == po, "slack labour (" + pop + " residents): plan equals the pre-WP4b Value plan (" + Sig(with) + ")");
            Check(ref checks, with.Labor.ExpansionFarmers > 0, "slack labour still expands with the leftover");
        }

        // ---- Scarce labour: expansion workers enter the search and trade against crafts.
        foreach (int pop in new[] { 100, 120, 160, 300 })
        {
            var sw = Agricultural(pop); var sc = sw.Camps[0]; sc.FoodNeedsMetRatio = .2; sc.QoL = .8;
            string beforeScarce = SaveCodec.Fingerprint(sw);
            var with = EconomicPlanner.Plan(sw, sc, EconomicIntent.From(sc), EconomicPlanner.ConfiguredRule(sw.Config), out double pw, out _, true);
            var without = EconomicPlanner.Plan(sw, sc, EconomicIntent.From(sc), EconomicPlanner.ConfiguredRule(sw.Config), out double po, out _, false);
            Check(ref checks, beforeScarce == SaveCodec.Fingerprint(sw), "scarce planning does not mutate the world");
            Check(ref checks, with.Assignments.Sum(a => a.Population) <= sc.Population, "scarce plan (" + pop + ") stays within residents");
            Check(ref checks, with.Labor.ExpansionFarmers <= with.Assignments.Where(a => a.Role == WorkforceRoles.Farmer).Sum(a => a.Population) + 1e-9, "expansion farmers are Farmer assignments");
            Check(ref checks, with.Labor.ExpansionFarmers > without.Labor.ExpansionFarmers && Crafts(with) < Crafts(without), "expansion displaces low-value craft labour (" + Sig(with) + " vs " + Sig(without) + ")");
            Check(ref checks, pw > po, "expansion raises the shadow price of labour (" + pw + " vs " + po + ")");
            Check(ref checks, with.Assignments.Any(a => a.Role == WorkforceRoles.Weaver || a.Role == WorkforceRoles.Builder), "some higher-value crafts survive the trade at " + pop);
        }

        // ---- The rule with an extra claim: 0 at s = 0, nondecreasing, craft path unchanged, exact ends.
        var rng = new Random(4242);
        var probe = ValueFulfilmentProbe(rng, out var claims);
        foreach (var extra in probe)
        {
            var craftOnly = new double[claims.Count]; var withExtra = new double[claims.Count];
            double previous = 0; var prev = new double[claims.Count]; bool ok = true, none = true, unchanged = true;
            for (int step = 0; step <= 64; step++)
            {
                double s = step / 64.0;
                rule.Shares(claims, s, craftOnly);
                rule.Shares(claims, extra, s, withExtra, out double share);
                if (step == 0) none &= share == 0 && withExtra.All(x => x == 0);
                ok &= share >= previous - 1e-12 && share >= 0 && share <= 1;
                for (int i = 0; i < claims.Count; i++) { ok &= withExtra[i] >= prev[i] - 1e-12; prev[i] = withExtra[i]; }
                // Only a claim more valuable than every craft moves the path's top; otherwise crafts are untouched.
                double hiCraft = claims.Where(ValueFulfilment.IsNeed).Max(x => x.FirstUnitValue / x.LabourPerUnit);
                if (extra.FirstUnitValue / extra.LabourPerUnit <= hiCraft) for (int i = 0; i < claims.Count; i++) unchanged &= craftOnly[i] == withExtra[i];
                previous = share;
            }
            Check(ref checks, ok && none, "extra claim shares are 0 at s = 0 and nondecreasing with crafts");
            Check(ref checks, unchanged, "an extra claim no more valuable than the best craft leaves the craft path unchanged");
            // Higher shortfall (more workers needed) never lowers the target at fixed s.
            foreach (double s in new[] { .2, .5, .8, 1 })
            {
                rule.Shares(claims, extra, s, withExtra, out double a);
                var larger = new ProductionClaim { Quantity = extra.Quantity * 3, Need = extra.Need * 3, Stock = 0, LabourPerUnit = extra.LabourPerUnit, FirstUnitValue = extra.FirstUnitValue, Valued = true };
                rule.Shares(claims, larger, s, withExtra, out double b);
                Check(ref checks, b * larger.Quantity >= a * extra.Quantity - 1e-9, "a larger shortfall never lowers the expansion target at s=" + s);
            }
        }
        var alone = new double[0]; var lone = new ProductionClaim { Quantity = 10, Need = 10, LabourPerUnit = 1, FirstUnitValue = 5, Valued = true };
        rule.Shares(new List<ProductionClaim>(), lone, 0, alone, out double a0); rule.Shares(new List<ProductionClaim>(), lone, 1, alone, out double a1);
        Check(ref checks, a0 == 0 && a1 == 1 && !double.IsNaN(rule.LaborPrice(new List<ProductionClaim>(), lone, .5)), "with no craft claim the expansion claim owns the path (0 at s=0, 1 at s=1)");

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

    // Craft claims plus expansion-like extras of small, comparable and dominant value.
    private static List<ProductionClaim> ValueFulfilmentProbe(Random rng, out List<ProductionClaim> claims)
    {
        claims = new List<ProductionClaim>();
        double[] v = { 8, 4, 2 };
        for (int i = 0; i < 3; i++)
            claims.Add(new ProductionClaim { Good = (CraftGood)i, Quantity = 30 + 10 * i, Stock = 5 * i, Need = 35 + 10 * i, LabourPerUnit = 1 + i, FirstUnitValue = v[i] * (1 + i), Value = v[i] * (1 + i), Priority = 1, Valued = true });
        var extras = new List<ProductionClaim>();
        foreach (double perYear in new[] { .01, .5, 3, 9 })
        {
            double workers = 5 + rng.NextDouble() * 500;
            extras.Add(new ProductionClaim { Quantity = workers, Need = workers, LabourPerUnit = 20, FirstUnitValue = perYear * 20, Value = perYear * 20, Priority = 1, Valued = true });
        }
        return extras;
    }

    private static string Sig(EconomicIntent e) => string.Join(",", e.Assignments.OrderBy(a => a.Role, StringComparer.Ordinal).Select(a => a.Role + a.Population)) + "|" + Math.Round(e.Labor?.MaterialGatherers ?? 0, 9) + "|" + Math.Round(e.Labor?.ExpansionFarmers ?? 0, 9);
}
