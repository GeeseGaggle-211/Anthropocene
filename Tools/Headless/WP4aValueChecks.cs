using System;
using System.Collections.Generic;
using System.Linq;
using Anthropocene.Simulation;

// WP4a (redo): Farm Tools and Herding Tools priced at the farm-labour price w_farm0, Pots valued by
// food security. Owns its worlds and counters.
internal static class WP4aValueChecks
{
    private static void Check(ref int checks, bool ok, string message) { checks++; if (!ok) throw new Exception("WP4a value: " + message); }
    private static bool Info => Environment.GetEnvironmentVariable("WP4A_INFO") == "1";
    private static bool Near(double a, double b, double rel = 1e-9) => Math.Abs(a - b) <= rel * Math.Max(1, Math.Max(Math.Abs(a), Math.Abs(b)));
    private static string Sig(EconomicIntent e) => string.Join(",", e.Assignments.OrderBy(a => a.Role, StringComparer.Ordinal).Select(a => a.Role + a.Population)) + "|" + Math.Round(e.Labor?.MaterialGatherers ?? 0, 9) + "|" + Math.Round(e.Labor?.ExpansionFarmers ?? 0, 9);

    // Food-short Value-rule camp with an expansion ladder, optionally with husbandry enabled.
    private static WorldState Fixture(int population, double food = .2, bool husbandry = false)
    {
        var w = NeolithicChecks.Fixture();
        int count = w.Tiles.Length;
        w.Resources = new WorldResourceState { MaximumQuantity = 100000, CatalogHash = ResourceCatalog.Hash, ProfileHash = ResourceCatalog.Hash, ConfigurationHash = ResourceCatalog.Hash, Provinces = Array.Empty<GeologicalProvince>(), Province = new ushort[count], History = new ushort[count], Host = new byte[count], Marine = new byte[count], Stone = new ushort[count], Clay = new ushort[count], Wood = new ushort[count], WoodRecoveryPerYear = .05 };
        var c = w.Camps[0]; int tile = c.Tile;
        w.Resources.Stone[tile] = 1000; w.Resources.Clay[tile] = 1000; w.Resources.Wood[tile] = 1000;
        var t = w.Tiles[tile]; t.CultivationCapital = .1; t.Cultivation = (float)CultivationEconomy.Intensity(w.Config, .1); t.CultivationOperator = c.Id;
        c.Inventory.HandTools = 1000; c.Population = population; c.DemographicGrowthMultiplier = 1; w.Config.AnnualHumanGrowthRate = new SimulationConfig().AnnualHumanGrowthRate;
        foreach (string tech in NeolithicTech.FirstTier) c.People.NeolithicTechnologies.Add(tech);
        if (husbandry) c.Technologies[(int)Technology.AnimalHusbandry] = true;
        w.Config.Neolithic.PlannerFulfilment = "Value";
        c.FoodNeedsMetRatio = food; c.QoL = food == 1 ? 1 : .8;
        return w;
    }

    private static double Multiplier(CampProductionDemand d, ProductionClaim claim)
    {
        double def = NeolithicProduction.Priority(new Camp { CraftingPriorities = new CraftingPriorities() }, claim.Good);
        return def > 0 ? claim.Priority / def : (claim.Priority > 0 ? 1 : 0);
    }

    private static ProductionClaim Claim(CraftGood good, double quantity, double stock, double need, double labour, double v)
        => new ProductionClaim { Good = good, Quantity = quantity, Stock = stock, Need = need, LabourPerUnit = labour, FirstUnitValue = v, Value = v, Priority = 1, Valued = true };
    private static ProductionClaim Tool(CraftGood good, double labour, double v)
        => new ProductionClaim { Good = good, Quantity = 0, Stock = 0, Need = 0, LabourPerUnit = labour, FirstUnitValue = v, Value = v, Priority = 1, Valued = true, LabourSaving = true };

    public static int Run()
    {
        int checks = 0;
        var defaults = new SimulationConfig(); var dn = defaults.Neolithic;

        // ---- Closed forms.
        double eff = Math.Pow(dn.StoneFarmToolsSupportedIntensity / dn.PrimitiveAgricultureSupportedIntensity, 2);
        Check(ref checks, Near(eff, 1.265625, 1e-9) && Near(CultivationEconomy.FarmToolFieldWorkEfficiency(dn), eff, 1e-12), "Farm Tool field-work efficiency");
        double savedFT = dn.FarmToolFarmerYearsPerUnit * (1 - 1 / eff);
        Check(ref checks, Near(ClaimValuation.FarmToolSavedYears(dn), savedFT, 1e-12) && Near(savedFT, 20 * (1 - 1 / 1.265625), 1e-9), "saved farmer-years per Farm Tool = 20 (1 - 1/e) (" + savedFT.ToString("F4") + ")");
        double savedHT = dn.HerdingToolCoverageYearsPerUnit * (dn.StoneHerdingToolAnimalsPerHerder / defaults.AnimalsPerHerder - 1);
        Check(ref checks, Near(ClaimValuation.HerdingToolSavedYears(defaults), savedHT, 1e-12) && Near(savedHT, 20 * (2.69 / 2 - 1), 1e-9), "saved herder-years per Herding Tool = 20 (S / A - 1) (" + savedHT.ToString("F4") + ")");
        {
            // The saved years agree with the herder requirement the resolver uses.
            var hw = Fixture(1000, 1, true); var hc = hw.Camps[0];
            var herd = new Herd { Id = 901, Animals = 269, DomesticCamp = hc.Id, Tile = hc.Tile };
            hw.Herds.Add(herd);
            double years0 = hw.Config.TurnDays / hw.Config.DaysPerYear;
            hc.Inventory.HerdingTools = 0; int without = HerdingTooling.RequiredHerders(hw, hc, years0);
            hc.Inventory.HerdingTools = 1; int with = HerdingTooling.RequiredHerders(hw, hc, years0);
            double covered = hw.Config.Neolithic.HerdingToolCoverageYearsPerUnit / years0;
            double expectedWith = covered + Math.Max(0, 269 - covered * 2.69) / 2;
            Check(ref checks, without == (int)Math.Ceiling(269 / 2.0 - 1e-10) && with == (int)Math.Ceiling(expectedWith - 1e-10), "herder requirement relation: " + without + " herders uncovered, " + with + " with one tool");
            Check(ref checks, Near(269 / 2.0 - expectedWith, covered * (2.69 / 2 - 1), 1e-9) && Near((269 / 2.0 - expectedWith) * years0, savedHT, 1e-9), "one tool frees exactly savedHT herder-years over the horizon");
        }
        double k = -Math.Log(1 - dn.PotDecayPerReferenceTurn) / dn.ReferenceTurnYears;
        double lPot = 1 / (k + dn.DiscountRatePerYear);
        Check(ref checks, Near(ClaimValuation.PotLife(dn), lPot, 1e-12) && lPot > 0 && lPot < 1 / dn.DiscountRatePerYear, "L_pot = 1 / (k + r) (" + lPot.ToString("F3") + " years)");
        foreach (double vf in new[] { .05, .5, 3 })
            Check(ref checks, Near(ClaimValuation.PotValue(dn, vf), dn.FoodRiskWeight * dn.PotStorageFoodPerUnit * vf, 1e-12), "V_Pot closed form at v_food " + vf);
        {
            var zero = new NeolithicConfig(); zero.FoodRiskWeight = 0;
            Check(ref checks, ClaimValuation.PotValue(zero, 1) == 0, "rho = 0 gives V_Pot = 0");
        }

        // ---- Demand pass: w_farm0 and the tool and Pot claims.
        var w = Fixture(100, .2, true); var c = w.Camps[0]; var cfg = w.Config; var n = cfg.Neolithic;
        double years = cfg.TurnDays / cfg.DaysPerYear;
        string before = SaveCodec.Fingerprint(w);
        var d0 = CampProductionDemand.Build(w, c, EconomicIntent.From(c));
        Check(ref checks, before == SaveCodec.Fingerprint(w), "the demand pass does not mutate the world");
        Check(ref checks, d0.Expansion != null && d0.Expansion.Any, "the fixture has an expansion ladder");
        Check(ref checks, c.EconomicLabor == null && d0.FarmLabourPrice0 == d0.Expansion.ValueAfter(0) && d0.FarmLabourPrice0 == d0.Expansion.Value[0], "no last plan: w_farm0 is the ladder's first value");
        double priced = 0;
        foreach (double farmers in new[] { 0.0, 3, 25, 1e6 })
        {
            c.EconomicLabor = new EconomicLaborPlan { ExpansionFarmers = farmers };
            var d = CampProductionDemand.Build(w, c, EconomicIntent.From(c));
            double expected = d.Expansion.ValueAfter(farmers * years);
            Check(ref checks, d.FarmLabourPrice0 == expected, "w_farm0 is the ladder's value after last turn's expansion farmers (" + farmers + ")");
            var ft = d.Claims.First(x => x.Good == CraftGood.FarmTools); var ht = d.Claims.FirstOrDefault(x => x.Good == CraftGood.HerdingTools);
            if (expected > 0)
            {
                Check(ref checks, ft.Valued && ft.LabourSaving && ft.LabourPerUnit > 0 && Near(ft.FirstUnitValue, savedFT * expected * Multiplier(d, ft), 1e-12), "V_FT = savedFT w_farm0 P at " + farmers + " farmers (" + ft.FirstUnitValue.ToString("G4") + ")");
                Check(ref checks, ht != null && ht.Valued && ht.LabourSaving && Near(ht.FirstUnitValue, savedHT * expected * Multiplier(d, ht), 1e-12), "V_HT = savedHT w_farm0 P at " + farmers + " farmers (" + (ht?.FirstUnitValue ?? 0).ToString("G4") + ")");
                Check(ref checks, d.Order.Key(CraftGood.FarmTools) == ft.FirstUnitValue && d.Order.Key(CraftGood.HerdingTools) == ht.FirstUnitValue, "tool order keys are V per unit");
                priced++;
            }
            else
            {
                Check(ref checks, !ft.Valued && !ft.LabourSaving && (ht == null || !ht.Valued) && double.IsNaN(d.Order.Key(CraftGood.FarmTools)) && double.IsNaN(d.Order.Key(CraftGood.HerdingTools)), "an exhausted ladder leaves the tools unpriced");
            }
        }
        Check(ref checks, priced >= 2, "the fixture priced the tools at several expansion staffing levels (" + priced + ")");
        c.EconomicLabor = null;

        // Pots: the claim is a need claim on food security.
        {
            var pw = Fixture(100, .2, true); var pc = pw.Camps[0]; pc.Inventory.Pots = 0;
            pw.Config.GrowthReserveYears = Math.Max(pw.Config.GrowthReserveYears, 1);
            var pd = CampProductionDemand.Build(pw, pc, EconomicIntent.From(pc));
            var pot = pd.Claims.FirstOrDefault(x => x.Good == CraftGood.Pots);
            double need = NeolithicProduction.TargetPots(pw, pd.Preview, years);
            Check(ref checks, pot != null, "the fixture has a Pot claim");
            if (need > 0)
            {
                double vfood = ExpansionValuation.FoodValue(pw.Config, pc);
                Check(ref checks, pot.Valued && Near(pot.Need, need, 1e-12) && pot.Stock == 0 && Near(pot.FirstUnitValue, ClaimValuation.PotValue(pw.Config.Neolithic, vfood) * Multiplier(pd, pot), 1e-12), "Pot claim: Need = TargetPots, Stock = decayed pots, V = V_Pot P");
                Check(ref checks, Near(pot.Quantity, NeolithicProduction.PotDemand(pw, pd.Preview, years), 1e-12), "Pot Quantity is PotDemand's gap");
                Check(ref checks, ValueFulfilment.IsNeed(pot) == (pot.Quantity > 0), "a Pot claim with a gap is a need claim (in the path bounds)");
                pc.Inventory.Pots = 2;
                var pd2 = CampProductionDemand.Build(pw, pc, EconomicIntent.From(pc));
                var pot2 = pd2.Claims.First(x => x.Good == CraftGood.Pots);
                Check(ref checks, Near(pot2.Stock, 2 * Math.Pow(1 - n.PotDecayPerReferenceTurn, years / n.ReferenceTurnYears), 1e-12), "Pot stock is the current pots after this turn's decay");
                if (pot2.Quantity > 0 && pd2.Order != null) Check(ref checks, Near(pd2.Order.Key(CraftGood.Pots), pot2.FirstUnitValue * Math.Exp(-n.ValueCurveSteepness * pot2.Stock / pot2.Need), 1e-12), "Pot order key V_Pot exp(-lambda Stock / Need)");
                pc.Inventory.Pots = 0;
                // rho = 0 -> not valued
                pw.Config.Neolithic.FoodRiskWeight = 0;
                var rd = CampProductionDemand.Build(pw, pc, EconomicIntent.From(pc));
                var rpot = rd.Claims.First(x => x.Good == CraftGood.Pots);
                Check(ref checks, !rpot.Valued && double.IsNaN(rd.Order.Key(CraftGood.Pots)), "rho = 0: Pots are not valued");
                pw.Config.Neolithic.FoodRiskWeight = n.FoodRiskWeight;
            }
            else checks++;
            // Need <= 0: no reserve beyond natural storage -> not valued.
            pw.Config.GrowthReserveYears = 0;
            var nd = CampProductionDemand.Build(pw, pc, EconomicIntent.From(pc));
            var npot = nd.Claims.First(x => x.Good == CraftGood.Pots);
            Check(ref checks, NeolithicProduction.TargetPots(pw, nd.Preview, years) <= 0 && !npot.Valued, "Need <= 0: Pots are not valued");
            if (Info) Console.WriteLine("WP4A_INFO pot fixture need=" + need.ToString("F3") + " quantity=" + pot.Quantity.ToString("F3") + " V=" + pot.FirstUnitValue.ToString("G4"));
        }

        // ---- No ladder: tools stay unpriced and the plan equals the plan without the expansion claim.
        {
            var gw = Fixture(60, .2, true); gw.Config.AnnualHumanGrowthRate = 0; var gc = gw.Camps[0];
            var gd = CampProductionDemand.Build(gw, gc, EconomicIntent.From(gc));
            Check(ref checks, gd.Expansion == null && gd.FarmLabourPrice0 == 0, "no shortfall: no ladder and w_farm0 = 0");
            Check(ref checks, gd.Claims.Where(x => x.Good == CraftGood.FarmTools || x.Good == CraftGood.HerdingTools).All(x => !x.Valued && !x.LabourSaving && x.FirstUnitValue == 0), "no ladder: the tools are unpriced");
            Check(ref checks, double.IsNaN(gd.Order.Key(CraftGood.FarmTools)) && double.IsNaN(gd.Order.Key(CraftGood.HerdingTools)), "no ladder: no tool order keys");
            var a = EconomicPlanner.Plan(gw, gc, EconomicIntent.From(gc), EconomicPlanner.ConfiguredRule(gw.Config), out double pa, out var oa, out double fa, true);
            var b = EconomicPlanner.Plan(gw, gc, EconomicIntent.From(gc), EconomicPlanner.ConfiguredRule(gw.Config), out double pb, out var ob, out _, false);
            Check(ref checks, Sig(a) == Sig(b) && pa == pb && fa == 0 && oa.ToString() == ob.ToString(), "no ladder: plan, price and order equal the plan without the expansion claim");
            // Unpriced tools are pursued at the uniform share: a fully priced claim list with w_farm0 = 0 changes no share.
            var rule = new ValueFulfilment(n.ValueCurveSteepness); var sh = new double[gd.Claims.Count];
            rule.Shares(gd.Claims, .37, sh);
            int fi = gd.Claims.ToList().FindIndex(x => x.Good == CraftGood.FarmTools);
            Check(ref checks, Near(sh[fi], .37, 1e-12), "unpriced Farm Tools take the uniform share");
        }
        // Uniform never prices tools or Pots' order and builds no order.
        {
            var uw = Fixture(100, .2, true); uw.Config.Neolithic.PlannerFulfilment = "Uniform";
            var ud = CampProductionDemand.Build(uw, uw.Camps[0], EconomicIntent.From(uw.Camps[0]));
            Check(ref checks, ud.Order == null && ud.Expansion == null && ud.FarmLabourPrice0 == 0 && ud.Claims.All(x => !x.LabourSaving), "Uniform: no order, no ladder, no priced tools");
            var rule = new UniformFulfilment(); var sh = new double[ud.Claims.Count]; rule.Shares(ud.Claims, .4, sh);
            Check(ref checks, sh.All(x => x == .4), "Uniform shares ignore the new claims");
        }

        // ---- Tool shares: a price test that never changes the path's bounds.
        {
            var rule = new ValueFulfilment(n.ValueCurveSteepness);
            var need = new List<ProductionClaim> { Claim(CraftGood.Cloth, 40, 5, 45, 2, 9), Claim(CraftGood.Housing, 20, 10, 30, 4, 30) };
            var ladder = new ExpansionLadderClaim { WorkerYears = new[] { 40.0, 60, 120 }, Value = new[] { 6.0, 2, .5 }, TotalWorkerYears = 220 };
            foreach (double vPerWy in new[] { .01, .3, 1.5, 3, 4.4, 50 })
            {
                var claims = new List<ProductionClaim>(need) { Tool(CraftGood.FarmTools, 2, vPerWy * 2) };
                var bare = new List<ProductionClaim>(need);
                var sh = new double[claims.Count]; var shBare = new double[bare.Count];
                double last = 0; bool monotone = true, exact = true, bounds = true;
                for (int step = 0; step <= 128; step++)
                {
                    double s = step / 128.0;
                    rule.Shares(claims, ladder, s, sh, out double wy); rule.Shares(bare, ladder, s, shBare, out double wyBare);
                    double price = rule.LaborPrice(claims, ladder, s);
                    monotone &= sh[2] >= last - 1e-15; last = sh[2];
                    if (s > 0 && s < 1) exact &= sh[2] == (vPerWy >= price ? 1 : 0);
                    bounds &= price == rule.LaborPrice(bare, ladder, s) && sh[0] == shBare[0] && sh[1] == shBare[1] && wy == wyBare;
                    if (step == 0) Check(ref checks, sh[2] == 0, "tool share 0 at s = 0");
                    if (step == 128) Check(ref checks, sh[2] == 1, "tool share 1 at s = 1");
                }
                Check(ref checks, monotone, "tool share is nondecreasing in s (V/l " + vPerWy + ")");
                Check(ref checks, exact, "tool share is 1 exactly when V/l >= w(s) (V/l " + vPerWy + ")");
                Check(ref checks, bounds, "a tool claim changes neither the path bounds nor other claims' shares (V/l " + vPerWy + ")");
            }
            // A tool worth more than every need claim is pursued from the first interior position.
            var rich = new List<ProductionClaim>(need) { Tool(CraftGood.FarmTools, 1, 1e6) }; var shr = new double[3];
            rule.Shares(rich, null, .01, shr, out _);
            Check(ref checks, shr[2] == 1, "a tool more valuable than the price is pursued in full");
        }

        // ---- Pot claims on the exponential curve: share monotone in s and in rho.
        {
            var rule = new ValueFulfilment(n.ValueCurveSteepness);
            var baseNeed = new List<ProductionClaim> { Claim(CraftGood.Cloth, 40, 5, 45, 2, 9) };
            double vf = .8;
            var rng = new Random(11);
            for (int trial = 0; trial < 12; trial++)
            {
                double stock = rng.NextDouble() * 4, qty = 1 + rng.NextDouble() * 10, labour = .05 + rng.NextDouble() * 2;
                var low = new NeolithicConfig(); low.FoodRiskWeight = rng.NextDouble() * .2;
                var high = new NeolithicConfig(); high.FoodRiskWeight = low.FoodRiskWeight * (1 + rng.NextDouble() * 3) + 1e-6;
                double last = -1; bool ok = true, rhoOk = true;
                for (int step = 0; step <= 64; step++)
                {
                    double s = step / 64.0;
                    var cl = new List<ProductionClaim>(baseNeed) { Claim(CraftGood.Pots, qty, stock, stock + qty, labour, ClaimValuation.PotValue(low, vf)) };
                    var ch = new List<ProductionClaim>(baseNeed) { Claim(CraftGood.Pots, qty, stock, stock + qty, labour, ClaimValuation.PotValue(high, vf)) };
                    var sl = new double[2]; var sr = new double[2];
                    rule.Shares(cl, s, sl); rule.Shares(ch, s, sr);
                    ok &= sl[1] >= last - 1e-12; last = sl[1];
                    // The price path depends on the pots' bounds; compare at the same price instead of the same s.
                    double price = rule.LaborPrice(cl, s);
                    double cover = Math.Log(ClaimValuation.PotValue(high, vf) / (price * labour)) / n.ValueCurveSteepness;
                    double highShare = s <= 0 ? 0 : s >= 1 ? 1 : Math.Min(1, Math.Max(0, (cover * (stock + qty) - stock) / qty));
                    rhoOk &= highShare >= sl[1] - 1e-12;
                }
                Check(ref checks, ok, "Pot share is nondecreasing in s (trial " + trial + ")");
                Check(ref checks, rhoOk, "a higher rho never lowers the Pot share at a fixed price (trial " + trial + ")");
            }
        }

        // ---- Order: tools with small V rank below need goods; priced Pots are ordered by key.
        {
            var recipes = NeolithicProduction.Recipes(cfg);
            ProductionClaim C(CraftGood g, double v, double stock, double need, double qty, bool tool = false)
                => tool ? Tool(g, 1, v) : new ProductionClaim { Good = g, Quantity = qty, Stock = stock, Need = need, LabourPerUnit = 1, FirstUnitValue = v, Valued = true };
            double Priority(CraftGood g) => .5;
            var claims = new List<ProductionClaim> {
                C(CraftGood.Cloth, 10, 0, 100, 100), C(CraftGood.AnimalFabric, 10, 0, 100, 100), C(CraftGood.LightHousing, 100, 0, 10, 10), C(CraftGood.Housing, 50, 50, 100, 50),
                C(CraftGood.Pots, 30, 10, 20, 10), C(CraftGood.FarmTools, .4, 0, 0, 0, true), C(CraftGood.HerdingTools, 250, 0, 0, 0, true) };
            var order = ProductionOrder.Build(cfg, claims, recipes, Priority);
            Check(ref checks, Near(order.Key(CraftGood.Pots), 30 * Math.Exp(-n.ValueCurveSteepness * .5), 1e-12), "Pot key V_Pot exp(-lambda Stock / Need)");
            Check(ref checks, order.Key(CraftGood.FarmTools) == .4 && order.Key(CraftGood.HerdingTools) == 250, "tool keys are their per-unit values");
            Check(ref checks, order.Outranks(CraftGood.Cloth, CraftGood.FarmTools) && order.Outranks(CraftGood.Housing, CraftGood.FarmTools), "a Farm Tool of small V ranks below the need goods");
            Check(ref checks, order.Outranks(CraftGood.HerdingTools, CraftGood.Cloth), "a Herding Tool of large V ranks above the need goods");
            Check(ref checks, order.Outranks(CraftGood.Pots, CraftGood.FarmTools) == (order.Key(CraftGood.Pots) > order.Key(CraftGood.FarmTools)), "Pots and tools are ordered by key");
            var keyed = order.Goods.Where(g => !double.IsNaN(order.Key(g)) && g != CraftGood.HousingMaterial && g != CraftGood.AnimalFabric).Select(g => order.Key(g)).ToArray();
            Check(ref checks, keyed.Zip(keyed.Skip(1), (a, b) => a >= b - 1e-12).All(x => x), "priced goods are in nonincreasing key order (precedence moves aside)");
            // Pots must not become the "best equipment need good" that keys the tool-coverage goods.
            var withPot = ProductionOrder.Build(cfg, claims.Where(x => x.Good != CraftGood.Pots).ToList(), recipes, Priority);
            Check(ref checks, order.Key(CraftGood.Tools) == withPot.Key(CraftGood.Tools) && order.Key(CraftGood.HandTools) == withPot.Key(CraftGood.HandTools), "Pots do not change the coverage tools' derived keys");
            // Unpriced tools keep the tail.
            claims[5] = new ProductionClaim { Good = CraftGood.FarmTools, Quantity = 0, Need = 0, LabourPerUnit = 1, Valued = false };
            claims[6] = new ProductionClaim { Good = CraftGood.HerdingTools, Quantity = 0, Need = 0, LabourPerUnit = 1, Valued = false };
            var tail = ProductionOrder.Build(cfg, claims, recipes, Priority);
            int lastPriced = claims.Where(x => x.Valued).Select(x => tail.Rank(x.Good)).Concat(new[] { tail.Rank(CraftGood.Tools), tail.Rank(CraftGood.HandTools) }).Max();
            Check(ref checks, tail.Rank(CraftGood.FarmTools) > lastPriced && tail.Rank(CraftGood.HerdingTools) > lastPriced, "unpriced tools keep the tail in priority order");
        }

        // ---- Plans across labour regimes: valid, pure, deterministic and monotone where stated.
        foreach (double food in new[] { 1.0, .2 })
            foreach (int pop in new[] { 100, 160, 300, 1000 })
            {
                var sw = Fixture(pop, food, true); var sc = sw.Camps[0];
                string beforePlan = SaveCodec.Fingerprint(sw);
                var sd = CampProductionDemand.Build(sw, sc, EconomicIntent.From(sc));
                var plan = EconomicPlanner.Plan(sw, sc, EconomicIntent.From(sc), EconomicPlanner.ConfiguredRule(sw.Config), out double price, out var order, out double farm, true);
                Check(ref checks, beforePlan == SaveCodec.Fingerprint(sw), "planning does not mutate the world");
                Check(ref checks, plan.Assignments.Sum(a => a.Population) <= sc.Population, "plan (" + pop + ", F=" + food + ") stays within residents");
                Check(ref checks, order != null && order.Goods.Length == sd.Recipes.Length, "the plan carries a complete order");
                if (Info)
                {
                    var ft = sd.Claims.First(x => x.Good == CraftGood.FarmTools); var ht = sd.Claims.FirstOrDefault(x => x.Good == CraftGood.HerdingTools); var pt = sd.Claims.First(x => x.Good == CraftGood.Pots);
                    Console.WriteLine("WP4A_INFO plan pop=" + pop + " F=" + food + " w_farm0=" + sd.FarmLabourPrice0.ToString("G4") + " V_FT=" + (ft.Valued ? ft.FirstUnitValue.ToString("G4") : "-") + " V_HT=" + (ht != null && ht.Valued ? ht.FirstUnitValue.ToString("G4") : "-") + " V_Pot=" + (pt.Valued ? pt.FirstUnitValue.ToString("G4") : "-") + " w=" + price.ToString("G4") + " farm=" + farm.ToString("G4") + " order=" + order);
                }
            }

        // ---- Determinism: reversed collections after a save round trip give the same plans and orders.
        {
            var rw = Fixture(120, .2, true);
            rw.Camps[0].EconomicLabor = new EconomicLaborPlan { ExpansionFarmers = 4 };
            var copy = SaveCodec.Load(SaveCodec.Save(rw)); copy.Camps.Reverse(); copy.Hunters.Reverse(); copy.Herds.Reverse();
            var p1 = EconomicPlanner.Plan(rw, rw.Camps[0], EconomicIntent.From(rw.Camps[0]), EconomicPlanner.ConfiguredRule(rw.Config), out double w1, out var o1, out double f1, true);
            var p2 = EconomicPlanner.Plan(copy, copy.Camps[0], EconomicIntent.From(copy.Camps[0]), EconomicPlanner.ConfiguredRule(copy.Config), out double w2, out var o2, out double f2, true);
            var p3 = EconomicPlanner.Plan(rw, rw.Camps[0], EconomicIntent.From(rw.Camps[0]), EconomicPlanner.ConfiguredRule(rw.Config), out double w3, out var o3, out double f3, true);
            Check(ref checks, Sig(p1) == Sig(p2) && Sig(p1) == Sig(p3) && w1 == w2 && w1 == w3 && f1 == f2 && f1 == f3 && o1.ToString() == o2.ToString() && o1.ToString() == o3.ToString(), "planning is deterministic across repeats and a reversed save round trip");
        }
        // Scalar versus reversed 4-worker replay, draft reuse exact.
        {
            var reference = new WorldGenerator().Generate(53, SimulationConfig.Small(4));
            reference.Config.Neolithic.PlannerFulfilment = "Value";
            var workers = new[] { new PhaseExecution(), new PhaseExecution(4, 3, true, 1) };
            var worlds = workers.Select(x => SaveCodec.Load(SaveCodec.Save(reference))).ToArray();
            int turns = 0, post = 0; long reused = 0;
            string OrderSig(PreparedTurn p) => string.Join(";", p.ProductionOrders.OrderBy(x => x.Key).Select(x => x.Key + ":" + x.Value));
            while ((!reference.Finished && turns < 12) || post < 6)
            {
                if (turns >= 12 && !reference.Finished) throw new Exception("WP4a replay opening did not finish within 12 turns.");
                if (reference.Finished) post++;
                var plan = AiPlanning.Decide(reference);
                var refPrepared = TurnPlanner.Prepare(reference, plan.Orders.ToDictionary(p => p.Key, p => p.Value.Select(o => o.Copy()).ToList()), plan.Traces);
                string refSig = OrderSig(refPrepared);
                new TurnResolver().Resolve(reference, refPrepared);
                for (int i = 0; i < worlds.Length; i++)
                {
                    var wd = worlds[i]; wd.Camps.Reverse(); wd.Hunters.Reverse(); wd.Herds.Reverse();
                    var draft = TurnPlanner.Draft(wd, 0, workers[i]);
                    var full = AiPlanning.Decide(wd, workers[i]);
                    var merged = draft.Ai.Orders.ToDictionary(p => p.Key, p => p.Value.Select(o => o.Copy()).ToList());
                    merged[0] = full.Orders[0].Select(o => o.Copy()).ToList();
                    var traces = draft.Ai.Traces.Where(t => t.Player != 0).Concat(full.Traces.Where(t => t.Player == 0)).OrderBy(t => t.Player).ToList();
                    var plain = TurnPlanner.Prepare(wd, merged.ToDictionary(p => p.Key, p => p.Value.Select(o => o.Copy()).ToList()), traces, workers[i]);
                    var prepared = TurnPlanner.Prepare(wd, merged, traces, workers[i], draft);
                    reused += TurnDiagnostics.For(wd).DraftIntentsReused;
                    Check(ref checks, OrderSig(plain) == OrderSig(prepared) && OrderSig(prepared) == refSig, "the order is independent of workers, partitions, entity order and draft reuse at turn " + turns + " world " + i);
                    new TurnResolver(workers[i]).Resolve(wd, prepared);
                    Check(ref checks, SaveCodec.Fingerprint(reference) == SaveCodec.Fingerprint(wd), "replay matches the scalar reference at turn " + turns + " world " + i);
                }
                turns++;
            }
            Check(ref checks, reused > 0, "the replay reused draft intents (" + reused + ")");
        }

        // Optional measurement on a real checkpoint (WP4A_CHECKPOINT=<save>, WP4A_TURNS=<n>): per-camp magnitudes, never asserted.
        string checkpoint = Environment.GetEnvironmentVariable("WP4A_CHECKPOINT");
        if (!string.IsNullOrEmpty(checkpoint))
        {
            var cw = SaveCodec.Load(System.IO.File.ReadAllBytes(checkpoint)); cw.Config.Neolithic.PlannerFulfilment = "Value";
            int turnsToRun = int.TryParse(Environment.GetEnvironmentVariable("WP4A_TURNS"), out int t) ? t : 12;
            var exec = new PhaseExecution(4, 64, false, 1);
            for (int turn = 0; turn < turnsToRun; turn++)
            {
                if (turn == 0 || turn % 4 == 0 || turn == turnsToRun - 1)
                {
                    var rows = new List<string>(); int ladders = 0, agricultural = 0;
                    foreach (var camp in cw.Camps.Where(x => x.Population > 0 && CultivationEconomy.EconomyEnabled(x)))
                    {
                        agricultural++;
                        var d = CampProductionDemand.Build(cw, camp, EconomicIntent.From(camp));
                        if (d.Expansion != null) ladders++;
                        double Key(CraftGood g) => d.Order == null ? double.NaN : d.Order.Key(g);
                        var ft = d.Claims.FirstOrDefault(x => x.Good == CraftGood.FarmTools); var ht = d.Claims.FirstOrDefault(x => x.Good == CraftGood.HerdingTools); var pt = d.Claims.FirstOrDefault(x => x.Good == CraftGood.Pots);
                        rows.Add(string.Join(" ", new[] {
                            "pop=" + camp.Population, "F=" + camp.FoodNeedsMetRatio.ToString("F2"), "wFarm0=" + d.FarmLabourPrice0.ToString("G3"),
                            "V_FT=" + (ft != null && ft.Valued ? ft.FirstUnitValue.ToString("G3") : "-"), "V_HT=" + (ht != null && ht.Valued ? ht.FirstUnitValue.ToString("G3") : "-"),
                            "V_Pot=" + (pt != null && pt.Valued ? pt.FirstUnitValue.ToString("G3") + "(need=" + pt.Need.ToString("G3") + ",stock=" + pt.Stock.ToString("G3") + ",key=" + Key(CraftGood.Pots).ToString("G3") + ")" : "-"),
                            "keys Cloth=" + Key(CraftGood.Cloth).ToString("G3") + " Housing=" + Key(CraftGood.Housing).ToString("G3") + " Tools=" + Key(CraftGood.Tools).ToString("G3") + " FT=" + Key(CraftGood.FarmTools).ToString("G3") + " HT=" + Key(CraftGood.HerdingTools).ToString("G3"),
                            "order=" + (d.Order == null ? "-" : string.Join(">", d.Order.Goods.Take(6))) }));
                    }
                    Console.WriteLine("WP4A_CKPT turn=" + turn + " economy camps=" + agricultural + " with ladder=" + ladders + " meanQoL=" + cw.Camps.Where(x => x.Population > 0).Average(x => x.QoL).ToString("F4"));
                    foreach (string row in rows.Where(r => !r.Contains("wFarm0=0 ")).Take(6)) Console.WriteLine("  " + row);
                    foreach (string row in rows.Where(r => r.Contains("wFarm0=0 ")).Take(2)) Console.WriteLine("  [no farm price] " + row);
                }
                var plan = AiPlanning.Decide(cw, exec);
                var prepared = TurnPlanner.Prepare(cw, plan.Orders.ToDictionary(p => p.Key, p => p.Value.Select(o => o.Copy()).ToList()), plan.Traces, exec);
                new TurnResolver(exec).Resolve(cw, prepared);
            }
        }

        Console.WriteLine("PASS: " + checks + " WP4a value assertions");
        return checks;
    }
}
