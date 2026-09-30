using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using Anthropocene.Simulation;

// WP4a: value claims for Farm Tools, Herding Tools and Pots under the Value planner rule. Owns its worlds and counters.
internal static class ToolPotValueChecks
{
    private static void Check(ref int checks, bool ok, string message) { checks++; if (!ok) throw new Exception("Tool and Pot value: " + message); }
    private static bool Info => Environment.GetEnvironmentVariable("WP4A_INFO") == "1";
    private static double Near(double a, double b) => Math.Abs(a - b) / Math.Max(1e-12, Math.Max(Math.Abs(a), Math.Abs(b)));

    // Expected first-unit values from the closed forms, recomputed from the committed camp and the claims.
    private static double W0(NeolithicConfig n, IEnumerable<ProductionClaim> claims)
        => claims.Where(x => ClaimValuation.IsNeedGood(x.Good) && x.Valued && x.FirstUnitValue > 0 && x.LabourPerUnit > 0 && x.Need > 0)
            .Select(x => x.FirstUnitValue * Math.Exp(-n.ValueCurveSteepness * x.Stock / x.Need) / x.LabourPerUnit).DefaultIfEmpty(0).Max();

    private static double PotV(WorldState w, Camp c, CampProductionDemand d)
    {
        var cfg = w.Config; var n = cfg.Neolithic;
        double years = cfg.TurnDays / cfg.DaysPerYear;
        double k = -Math.Log(1 - n.PotDecayPerReferenceTurn) / n.ReferenceTurnYears;
        double life = 1 / (k + n.DiscountRatePerYear);
        double vf = years * d.Preview.Population * c.QoL / (Math.Max(1e-6, c.FoodNeedsMetRatio) * d.Preview.Population * cfg.Consumption * cfg.GenerationIntervals);
        return n.PotRiskWeight * n.PotStorageFoodPerUnit * vf * life / n.ReferenceTurnYears;
    }

    public static int Run()
    {
        int checks = 0;
        var defaults = new SimulationConfig(); var dn = defaults.Neolithic;

        // ---- Config: PotRiskWeight default, validation, hash, legacy payloads and save round trip.
        Check(ref checks, dn.PotRiskWeight == .1, "PotRiskWeight defaults to 0.1");
        var negative = new SimulationConfig().Neolithic; negative.PotRiskWeight = -.01;
        bool rejected = false; try { negative.Validate(); } catch (ArgumentException) { rejected = true; }
        Check(ref checks, rejected, "negative PotRiskWeight rejected");
        var zeroRho = new SimulationConfig().Neolithic; zeroRho.PotRiskWeight = 0; zeroRho.Validate();
        Check(ref checks, true, "PotRiskWeight 0 is allowed");
        var rhoConfig = new SimulationConfig(); rhoConfig.Neolithic.PotRiskWeight = .3;
        Check(ref checks, SaveCodec.ConfigurationHash(rhoConfig) != SaveCodec.ConfigurationHash(new SimulationConfig()), "PotRiskWeight enters the configuration hash");
        var serializer = new DataContractSerializer(typeof(NeolithicConfig));
        string xml;
        using (var ms = new MemoryStream()) { serializer.WriteObject(ms, new SimulationConfig().Neolithic); xml = Encoding.UTF8.GetString(ms.ToArray()); }
        string stripped = Regex.Replace(xml, "<PotRiskWeight>[^<]*</PotRiskWeight>", "");
        Check(ref checks, stripped.Length < xml.Length, "fixture removed PotRiskWeight");
        NeolithicConfig old;
        using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(stripped))) old = (NeolithicConfig)serializer.ReadObject(ms);
        Check(ref checks, old.PotRiskWeight == .1, "legacy payload loads PotRiskWeight 0.1");
        var saved = ValueFulfilmentChecks.Agricultural(true); saved.Config.Neolithic.PotRiskWeight = .35;
        var loaded = SaveCodec.Load(SaveCodec.Save(saved));
        Check(ref checks, loaded.Config.Neolithic.PotRiskWeight == .35 && SaveCodec.Fingerprint(saved) == SaveCodec.Fingerprint(loaded), "save round trip keeps PotRiskWeight");

        // ---- Farm Tools and Pots closed forms on an Agricultural fixture camp.
        var w = ValueFulfilmentChecks.Agricultural(true); var c = w.Camps[0]; var n = w.Config.Neolithic;
        c.Inventory.HandTools = 1000;
        string before = SaveCodec.Fingerprint(w);
        var d = CampProductionDemand.Build(w, c, EconomicIntent.From(c));
        Check(ref checks, before == SaveCodec.Fingerprint(w), "demand pass stays pure with tool and Pot claims");
        double years = w.Config.TurnDays / w.Config.DaysPerYear;
        double efficiency = Math.Pow(n.StoneFarmToolsSupportedIntensity / n.PrimitiveAgricultureSupportedIntensity, 2);
        double savedFT = n.FarmToolFarmerYearsPerUnit * (1 - 1 / efficiency);
        Check(ref checks, Near(efficiency, 1.265625) < 1e-12 && Near(savedFT, 20 * (1 - 1 / 1.265625)) < 1e-12 && Near(ClaimValuation.FarmToolSavedYears(n), savedFT) < 1e-12, "savedFT = 20 * (1 - 1 / 1.265625) = 4.1975 worker-years");
        double w0 = W0(n, d.Claims);
        Check(ref checks, w0 > 0 && Near(d.LabourPrice0, w0) < 1e-12, "w0 is the best need claim's marginal value per worker-year (" + w0 + ")");
        var farm = d.Claims.Single(x => x.Good == CraftGood.FarmTools);
        var pots = d.Claims.Single(x => x.Good == CraftGood.Pots);
        var cloth = d.Claims.Single(x => x.Good == CraftGood.Cloth); var housing = d.Claims.Single(x => x.Good == CraftGood.Housing);
        double priorityMultiplier = 1; // default priorities on the fixture camp
        Check(ref checks, Near(farm.Priority, NeolithicProduction.Priority(new Camp { CraftingPriorities = new CraftingPriorities() }, CraftGood.FarmTools)) < 1e-12, "fixture camp keeps the default Farm Tools priority");
        Check(ref checks, farm.Valued && farm.LabourSaving && ValueFulfilment.IsTool(farm) && farm.Quantity == 0 && Near(farm.FirstUnitValue, savedFT * w0 * priorityMultiplier) < 1e-12 && farm.Value == farm.FirstUnitValue, "V_FT = savedFT * w0 * P and the claim is still sized later (Quantity 0)");
        double perYear = w.Config.DaysPerYear / w.Config.BaseDays;
        double farmL = 1 / (farm.Rate * perYear) + n.WoodPerFarmTool / (n.SpecialistMaterialExtractionRate * (c.People.HasTech(EconomicIntents.RequiredTechnology(MaterialEconomy.Specialist(MaterialId.Wood))) ? 1 : n.GeneralistTaskEfficiency) * perYear);
        Check(ref checks, Near(farm.LabourPerUnit, farmL) < 1e-12, "Farm Tools l = craft time + Wood extraction");
        double vf = years * c.QoL / (Math.Max(1e-6, c.FoodNeedsMetRatio) * w.Config.Consumption * w.Config.GenerationIntervals);
        double potV = PotV(w, c, d);
        Check(ref checks, pots.Valued && !pots.LabourSaving && Near(pots.FirstUnitValue, potV) < 1e-12 && potV > 0, "V_Pot = rho * PotStorageFoodPerUnit * vf * L / ReferenceTurnYears (" + potV + ", vf " + vf + ")");
        Check(ref checks, Near(pots.Need, NeolithicProduction.TargetPots(w, d.Preview, years)) < 1e-12 && Near(pots.Quantity, NeolithicProduction.PotDemand(w, d.Preview, years)) < 1e-12 && Near(pots.Stock + pots.Quantity, pots.Need) < 1e-9, "Pot need is the reserve target, stock the decayed pots, quantity PotDemand's gap");
        Check(ref checks, pots.Quantity > 0 && ValueFulfilment.IsNeed(pots), "the fixture has a Pot gap so Pots act as a need claim");
        if (Info)
        {
            double kk = -Math.Log(1 - n.HousingDecayPerTurn) / n.ReferenceTurnYears;
            Console.WriteLine("WP4A_INFO w0=" + w0.ToString("F4") + " savedFT=" + savedFT.ToString("F4") + " vf=" + vf.ToString("F5") + " L_housing=" + (1 / (kk + n.DiscountRatePerYear)).ToString("F2"));
            foreach (var claim in d.Claims.Where(x => x.Valued))
                Console.WriteLine("WP4A_INFO " + claim.Good + " V=" + claim.FirstUnitValue.ToString("F4") + " l=" + claim.LabourPerUnit.ToString("F4") + " V/l=" + (claim.FirstUnitValue / claim.LabourPerUnit).ToString("F3") + " Q=" + claim.Quantity.ToString("F2") + " stock=" + claim.Stock.ToString("F2") + " need=" + claim.Need.ToString("F2"));
            Console.WriteLine("WP4A_INFO order " + d.Order);
        }

        // ---- Tool shares: 0 at s = 0, 1 at s = 1, nondecreasing, and 1 whenever V / l >= w(s).
        var rule = new ValueFulfilment(n.ValueCurveSteepness);
        var claims = d.Claims;
        int farmIndex = claims.ToList().IndexOf(farm);
        var shares = new double[claims.Count]; double previous = 0; bool sawOne = false, sawZeroInterior = false;
        for (int step = 0; step <= 400; step++)
        {
            double s = step / 400.0;
            rule.Shares(claims, s, shares);
            double price = rule.LaborPrice(claims, s);
            Check(ref checks, shares[farmIndex] >= previous, "Farm Tools share is nondecreasing in s (s " + s + ")");
            previous = shares[farmIndex];
            if (s > 0 && s < 1)
            {
                bool worth = farm.FirstUnitValue / farm.LabourPerUnit >= price;
                Check(ref checks, worth ? shares[farmIndex] == 1 : shares[farmIndex] == 0, "Farm Tools share is 1 exactly when V/l >= w(s) (s " + s + ")");
                sawOne |= shares[farmIndex] == 1; sawZeroInterior |= shares[farmIndex] == 0;
            }
            if (step == 0) Check(ref checks, shares[farmIndex] == 0, "Farm Tools share is 0 at s = 0");
            if (step == 400) Check(ref checks, shares[farmIndex] == 1, "Farm Tools share is 1 at s = 1");
        }
        Check(ref checks, sawOne, "Farm Tools are pursued in full at interior prices");
        // A tool claim priced below the labour price is not pursued, and one priced above it is.
        var probe = new List<ProductionClaim>
        {
            new ProductionClaim { Good = CraftGood.Cloth, Quantity = 10, Stock = 0, Need = 10, LabourPerUnit = 1, FirstUnitValue = 10, Priority = 1, Valued = true },
            new ProductionClaim { Good = CraftGood.FarmTools, Quantity = 0, LabourPerUnit = 1, FirstUnitValue = 6, Priority = 1, Valued = true, LabourSaving = true },
            new ProductionClaim { Good = CraftGood.HerdingTools, Quantity = 5, LabourPerUnit = 1, FirstUnitValue = 2, Priority = 1, Valued = true, LabourSaving = true },
        };
        var ps = new double[3]; rule.Shares(probe, .25, ps); double pw = rule.LaborPrice(probe, .25);
        Check(ref checks, pw > 2 && pw < 6 && ps[1] == 1 && ps[2] == 0, "a tool is pursued only while its value per worker-year covers the price (w " + pw + ")");
        probe[1].LabourPerUnit = 0; rule.Shares(probe, .25, ps);
        Check(ref checks, ps[1] == .25, "a tool with no usable labour estimate falls back to the uniform share");
        probe[1].LabourPerUnit = 1; probe[1].FirstUnitValue = 0; rule.Shares(probe, .25, ps);
        Check(ref checks, ps[1] == 0, "a tool with zero value (priority 0) gets share 0");

        // ---- Pots: rho monotone; rho = 0 pursues no Pots under scarcity; priority multiplies V.
        double lastShare = -1; var potShares = new List<double>();
        foreach (double rho in new[] { 0, .02, .05, .1, .2, .5, 1, 3 })
        {
            var rw = ValueFulfilmentChecks.Agricultural(true); rw.Config.Neolithic.PotRiskWeight = rho; var rc = rw.Camps[0]; rc.Inventory.HandTools = 1000;
            var rd = CampProductionDemand.Build(rw, rc, EconomicIntent.From(rc));
            var rp = rd.Claims.Single(x => x.Good == CraftGood.Pots); int pi = rd.Claims.ToList().IndexOf(rp);
            Check(ref checks, Near(rp.FirstUnitValue, PotV(rw, rc, rd)) < 1e-12, "V_Pot closed form at rho " + rho);
            var rs = new double[rd.Claims.Count]; double acc = 0;
            foreach (double s in new[] { .1, .3, .5, .7, .9 }) { new ValueFulfilment(3).Shares(rd.Claims, s, rs); acc += rs[pi]; if (rho == 0) Check(ref checks, rs[pi] == 0, "rho 0 pursues no Pots at s " + s); }
            Check(ref checks, acc >= lastShare - 1e-12, "a higher rho never lowers the Pot share (rho " + rho + ")");
            lastShare = acc; potShares.Add(acc);
            if (rho == 0) Check(ref checks, rp.FirstUnitValue == 0 && rp.Valued, "rho 0 gives Pots zero value");
        }
        Check(ref checks, potShares.Last() > potShares.First(), "rho raises the Pot share somewhere along the path");
        var rw0 = ValueFulfilmentChecks.Agricultural(true, 14); rw0.Config.Neolithic.PotRiskWeight = 0;
        var scarce0 = EconomicPlanner.Plan(rw0, rw0.Camps[0], EconomicIntent.From(rw0.Camps[0]));
        Check(ref checks, !scarce0.Assignments.Any(a => a.Role == WorkforceRoles.Potter), "rho 0 stages no Potters under scarce labour");
        c.CraftingPriorities.Pots = 0;
        var noPot = CampProductionDemand.Build(w, c, EconomicIntent.From(c)).Claims.Single(x => x.Good == CraftGood.Pots);
        Check(ref checks, noPot.FirstUnitValue == 0, "priority 0 removes the Pot value");
        c.CraftingPriorities.Pots = new CraftingPriorities().Pots;

        // ---- Production order: tools by V, Pots by V_Pot * exp(-lambda c0), coverage tools precede them.
        var order = d.Order;
        Check(ref checks, order != null && Near(order.Key(CraftGood.FarmTools), farm.FirstUnitValue) < 1e-12, "Farm Tools key is V per unit");
        Check(ref checks, Near(order.Key(CraftGood.Pots), pots.FirstUnitValue * Math.Exp(-n.ValueCurveSteepness * pots.Stock / pots.Need)) < 1e-12, "Pot key is V_Pot * exp(-lambda * stock / need)");
        Check(ref checks, order.Outranks(CraftGood.HandTools, CraftGood.FarmTools) && order.Outranks(CraftGood.HandTools, CraftGood.Pots) && order.Outranks(CraftGood.Tools, CraftGood.HandTools), "coverage tools precede the tools and Pots they cover (" + order + ")");
        Check(ref checks, order.Goods.Length == d.Recipes.Length && order.Goods.Distinct().Count() == order.Goods.Length, "order is a permutation of the recipes");
        Check(ref checks, order.Outranks(CraftGood.HousingMaterial, CraftGood.Housing), "Housing Material still precedes Housing");

        // ---- Husbandry fixture: Herding Tools.
        var hw = PastoralCraftChecks.PlannerWorld(); var hc = hw.Camps.Single();
        foreach (var tech in new[] { NeolithicTech.Toolmaking, NeolithicTech.Carpentry, NeolithicTech.Weaving, NeolithicTech.Construction }) hc.People.NeolithicTechnologies.Add(tech);
        hc.Inventory.Stone = hc.Inventory.Wood = 1000; hc.Inventory.HandTools = 1000;
        hw.Config.Neolithic.PlannerFulfilment = "Value";
        var hn = hw.Config.Neolithic;
        var hd = CampProductionDemand.Build(hw, hc, EconomicIntent.From(hc));
        var herding = hd.Claims.Single(x => x.Good == CraftGood.HerdingTools);
        double savedHT = hn.HerdingToolCoverageYearsPerUnit * (1 - hw.Config.AnimalsPerHerder / hn.StoneHerdingToolAnimalsPerHerder);
        Check(ref checks, Near(savedHT, 20 * (1 - 2 / 2.69)) < 1e-12 && Near(ClaimValuation.HerdingToolSavedYears(hw.Config), savedHT) < 1e-12, "savedHT = 20 * (1 - 2 / 2.69) = 5.1301 worker-years");
        double hw0 = W0(hn, hd.Claims);
        Check(ref checks, hw0 > 0 && Near(hd.LabourPrice0, hw0) < 1e-12 && herding.LabourSaving && Near(herding.FirstUnitValue, savedHT * hw0) < 1e-12, "V_HT = savedHT * w0 (" + herding.FirstUnitValue + ")");
        Check(ref checks, Near(hd.Order.Key(CraftGood.HerdingTools), herding.FirstUnitValue) < 1e-12 && hd.Order.Outranks(CraftGood.HandTools, CraftGood.HerdingTools), "Herding Tools are keyed by V and preceded by coverage tools");
        var hp = new double[hd.Claims.Count]; int hi = hd.Claims.ToList().IndexOf(herding); var hrule = new ValueFulfilment(3);
        hrule.Shares(hd.Claims, 0, hp); bool zero = hp[hi] == 0; hrule.Shares(hd.Claims, 1, hp); bool one = hp[hi] == 1;
        Check(ref checks, zero && one, "Herding Tools share is 0 at s = 0 and 1 at s = 1");
        string beforeH = SaveCodec.Fingerprint(hw);
        var hplan = EconomicPlanner.Plan(hw, hc, EconomicIntent.From(hc), hrule, out double hprice, out var horder);
        Check(ref checks, beforeH == SaveCodec.Fingerprint(hw) && !double.IsNaN(hprice) && horder != null && hplan.Assignments.Sum(a => a.Population) <= hc.Population, "husbandry plan with tool claims is pure, priced and resident-bounded");
        if (Info) Console.WriteLine("WP4A_INFO herding V=" + herding.FirstUnitValue.ToString("F4") + " l=" + herding.LabourPerUnit.ToString("F4") + " w0=" + hw0.ToString("F4") + " savedHT=" + savedHT.ToString("F4"));

        // ---- Ample labour: Value plan equals full fulfilment with tool and Pot claims present.
        var aw = ValueFulfilmentChecks.Agricultural(true, 100); var au = ValueFulfilmentChecks.Agricultural(false, 100);
        var ap = EconomicPlanner.Plan(aw, aw.Camps[0], EconomicIntent.From(aw.Camps[0]));
        var up = EconomicPlanner.Plan(au, au.Camps[0], EconomicIntent.From(au.Camps[0]));
        string Sig(EconomicIntent e) => string.Join(",", e.Assignments.OrderBy(a => a.Role, StringComparer.Ordinal).Select(a => a.Role + a.Population)) + "|" + Math.Round(e.Labor?.MaterialGatherers ?? 0, 9) + "|" + Math.Round(e.Labor?.ExpansionFarmers ?? 0, 9);
        Check(ref checks, Sig(ap) == Sig(up), "ample labour: the Value plan equals full fulfilment (" + Sig(ap) + ")");
        // Scarce labour: bounded, pure, repeatable.
        var sw = ValueFulfilmentChecks.Agricultural(true, 14); var sc = sw.Camps[0];
        string beforeS = SaveCodec.Fingerprint(sw);
        var s1 = EconomicPlanner.Plan(sw, sc, EconomicIntent.From(sc)); var s2 = EconomicPlanner.Plan(sw, sc, EconomicIntent.From(sc));
        Check(ref checks, beforeS == SaveCodec.Fingerprint(sw) && Sig(s1) == Sig(s2) && s1.Assignments.Sum(a => a.Population) <= sc.Population, "scarce plan is pure, repeatable and bounded (" + Sig(s1) + ")");

        // ---- Uniform stays untouched: no order, no tool value, Value = Priority.
        var uw = ValueFulfilmentChecks.Agricultural(false); var uc = uw.Camps[0];
        var ud = CampProductionDemand.Build(uw, uc, EconomicIntent.From(uc));
        Check(ref checks, ud.Order == null && ud.Claims.All(x => x.Value == x.Priority), "Uniform keeps Value = Priority and builds no order");
        Check(ref checks, UniformFulfilment.Instance.Name == "Uniform", "uniform rule unchanged");
        var us = new double[ud.Claims.Count]; UniformFulfilment.Instance.Shares(ud.Claims, .3, us);
        Check(ref checks, us.All(x => x == .3), "Uniform shares every claim at s including tools and Pots");

        // ---- Determinism: scalar versus reversed 4-worker replay over an Agricultural opening with tool claims live.
        var reference = new WorldGenerator().Generate(53, SimulationConfig.Small(4));
        reference.Config.Neolithic.PlannerFulfilment = "Value";
        var workers = new[] { new PhaseExecution(2, 7, false, 1), new PhaseExecution(4, 3, true, 1) };
        var worlds = workers.Select(x => SaveCodec.Load(SaveCodec.Save(reference))).ToArray();
        int turns = 0, post = 0;
        while ((!reference.Finished && turns < 12) || post < 4)
        {
            if (turns >= 12 && !reference.Finished) throw new Exception("Tool and Pot value replay opening did not finish within 12 turns.");
            if (reference.Finished) post++;
            var plan = AiPlanning.Decide(reference); new TurnResolver().Resolve(reference, plan.Orders, plan.Traces);
            for (int i = 0; i < worlds.Length; i++)
            {
                var rev = worlds[i]; rev.Camps.Reverse(); rev.Hunters.Reverse(); rev.Herds.Reverse();
                var parallel = AiPlanning.Decide(rev, workers[i]); new TurnResolver(workers[i]).Resolve(rev, parallel.Orders, parallel.Traces);
                Check(ref checks, SaveCodec.Fingerprint(reference) == SaveCodec.Fingerprint(rev), "reversed worker layout " + i + " matches the scalar reference at turn " + (turns + 1));
            }
            turns++;
        }
        Console.WriteLine("PASS: " + checks + " tool and Pot value assertions");
        return checks;
    }
}
