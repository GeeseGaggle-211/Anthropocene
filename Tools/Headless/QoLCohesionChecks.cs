using System;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using Anthropocene.Simulation;

// WP2: QoL cohesion multiplier Q = F * W * R * S(N). Owns its worlds and counters.
internal static class QoLCohesionChecks
{
    private static void Check(ref int checks, bool ok, string message) { checks++; if (!ok) throw new Exception("QoL cohesion: " + message); }
    private static void Near(ref int checks, double a, double b, double tol, string message) => Check(ref checks, Math.Abs(a - b) <= tol, message + " (" + a + " vs " + b + ")");

    private static double FissionOnset(NeolithicConfig n, double scale, double br, double qStar)
        => br <= qStar ? 0 : scale * Math.Pow(Math.Log(br / qStar), 1 / n.SocialCohesionExponent);

    private static WorldState World(bool cultivation, Technology technology)
    {
        var w = NeolithicChecks.Fixture(cultivation);
        var c = w.Camps[0];
        if (!cultivation) c.Technology = technology;
        c.StationaryYears = 100;
        return w;
    }

    public static int Run()
    {
        int checks = 0;
        var n = new SimulationConfig().Neolithic;
        Check(ref checks, n.SocialCohesionExponent == 2 && n.CohesionScalePaleolithic == 220 && n.CohesionScaleNomadic == 550 && n.CohesionScaleAgricultural == 1780, "defaults");
        n.Validate();

        // Closed form, S(0)=1, strictly decreasing.
        var w0 = World(false, Technology.None); var camp0 = w0.Camps[0];
        Near(ref checks, NeolithicProduction.SocialCohesion(n, camp0, 0), 1, 0, "S(0)=1");
        double prior = 1;
        for (int pop = 10; pop <= 3000; pop += 10)
        {
            double s = NeolithicProduction.SocialCohesion(n, camp0, pop);
            Check(ref checks, s < prior && s > 0, "S strictly decreasing at N=" + pop);
            Near(ref checks, s, Math.Exp(-Math.Pow(pop / 220.0, 2)), 1e-15, "closed form N=" + pop);
            prior = s;
        }

        // Regime selection.
        var wp = World(false, Technology.None); var wn = World(false, Technology.Watercraft); var wa = World(true, Technology.Cultivation);
        Near(ref checks, NeolithicProduction.CohesionScale(n, wp.Camps[0]), 220, 0, "Paleolithic scale");
        Near(ref checks, NeolithicProduction.CohesionScale(n, wn.Camps[0]), 550, 0, "Nomadic scale");
        Near(ref checks, NeolithicProduction.CohesionScale(n, wa.Camps[0]), 1780, 0, "Agricultural scale");
        var wh = World(false, Technology.AnimalHusbandry);
        Near(ref checks, NeolithicProduction.CohesionScale(n, wh.Camps[0]), 550, 0, "Husbandry-only camp is Nomadic");

        // ResolveNeeds: Q = F*W*R*S on a fixture, using the cohort argument.
        foreach (var w in new[] { wp, wn, wa })
        {
            var c = w.Camps[0]; c.Population = 100; c.Inventory.Cloth = 40; c.FoodNeedsMetRatio = .8; c.WaterNeedsMetRatio = .9;
            foreach (int residents in new[] { -1, 60 })
            {
                NeolithicProduction.ResolveNeeds(w, c, 0, residents);
                var cfg = w.Config.Neolithic;
                double clothing = 1 - c.RudimentaryExpectation * (1 - c.ClothPerCapita), housing = 1 - c.RudimentaryExpectation * (1 - c.HousingPerCapita);
                double fwr = c.FoodNeedsMetRatio * c.WaterNeedsMetRatio * (cfg.RudimentaryWeightClothing * clothing + cfg.RudimentaryWeightHousing * housing);
                int cohort = residents < 0 ? c.Population : residents;
                Near(ref checks, c.QoL, fwr * NeolithicProduction.SocialCohesion(cfg, c, cohort), 1e-12, "ResolveNeeds QoL = F*W*R*S cohort " + cohort);
                Near(ref checks, c.QoL, fwr * Math.Exp(-Math.Pow(cohort / NeolithicProduction.CohesionScale(cfg, c), 2)), 1e-12, "closed form in ResolveNeeds");
            }
        }

        // Section 3.5 fission-onset table with Q* = 0.73.
        (double scale, double br, int expected)[] table =
        {
            (220, .90, 101), (220, 1.0, 123), (550, .90, 252), (550, 1.0, 309), (1780, .82, 607), (1780, 1.0, 999)
        };
        foreach (var row in table)
        {
            double onset = FissionOnset(n, row.scale, row.br, .73);
            Check(ref checks, Math.Abs(onset - row.expected) <= 1, "fission onset Ns=" + row.scale + " B*R=" + row.br + " gives " + onset + ", expected " + row.expected);
            // Q at the onset equals Q*: the same S function reproduces the threshold.
            var c = World(false, Technology.None).Camps[0];
            double s = Math.Exp(-Math.Pow(onset / row.scale, n.SocialCohesionExponent));
            Near(ref checks, row.br * s, .73, 1e-12, "B*R*S(N*)=Q* for Ns=" + row.scale);
        }
        Near(ref checks, FissionOnset(n, 220, .70, .73), 0, 0, "N*=0 when B*R <= Q*");

        // Regime switch applies only when capability changes at a boundary.
        var ws = World(false, Technology.Watercraft); var cs = ws.Camps[0];
        cs.Population = 400; cs.Inventory.Cloth = 400; cs.FoodNeedsMetRatio = cs.WaterNeedsMetRatio = 1;
        cs.StationaryYears = 100; NeolithicProduction.ResolveNeeds(ws, cs, 0);
        double nomadic = cs.QoL, fwrNomadic = nomadic / NeolithicProduction.SocialCohesion(ws.Config.Neolithic, cs, 400);
        Near(ref checks, nomadic, fwrNomadic * Math.Exp(-Math.Pow(400 / 550.0, 2)), 1e-12, "before Cultivation uses the Nomadic scale");
        NeolithicProduction.ResolveNeeds(ws, cs, 0);
        Near(ref checks, cs.QoL, nomadic, 1e-12, "no capability change leaves the scale unchanged");
        cs.Technologies[(int)Technology.Cultivation] = true;
        cs.StationaryYears = 100; NeolithicProduction.ResolveNeeds(ws, cs, 0);
        Near(ref checks, cs.QoL, fwrNomadic * Math.Exp(-Math.Pow(400 / 1780.0, 2)), 1e-12, "after Cultivation uses the Agricultural scale");
        Check(ref checks, cs.QoL > nomadic, "acquiring Cultivation raises cohesion for a large camp");

        // Save round trip with new fields.
        var saved = NeolithicChecks.Fixture(true); saved.Config.Neolithic.CohesionScaleNomadic = 611; saved.Config.Neolithic.SocialCohesionExponent = 3;
        var loaded = SaveCodec.Load(SaveCodec.Save(saved));
        Check(ref checks, loaded.Config.Neolithic.CohesionScaleNomadic == 611 && loaded.Config.Neolithic.SocialCohesionExponent == 3
            && loaded.Config.Neolithic.CohesionScalePaleolithic == 220 && loaded.Config.Neolithic.CohesionScaleAgricultural == 1780, "save round trip keeps configured cohesion values");
        Check(ref checks, SaveCodec.Fingerprint(saved) == SaveCodec.Fingerprint(loaded), "fingerprint stable across round trip");

        // Older payload lacking the fields loads the defaults.
        var serializer = new DataContractSerializer(typeof(NeolithicConfig));
        string xml;
        using (var ms = new MemoryStream()) { serializer.WriteObject(ms, new SimulationConfig().Neolithic); xml = Encoding.UTF8.GetString(ms.ToArray()); }
        string stripped = Regex.Replace(xml, "<(SocialCohesionExponent|CohesionScalePaleolithic|CohesionScaleNomadic|CohesionScaleAgricultural)>[^<]*</\\1>", "");
        Check(ref checks, stripped.Length < xml.Length && !stripped.Contains("CohesionScale"), "fixture removed the new fields");
        NeolithicConfig old;
        using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(stripped))) old = (NeolithicConfig)serializer.ReadObject(ms);
        Check(ref checks, old.SocialCohesionExponent == 2 && old.CohesionScalePaleolithic == 220 && old.CohesionScaleNomadic == 550 && old.CohesionScaleAgricultural == 1780, "legacy payload gets default cohesion values");
        old.Validate();

        // Validation rejects non-positive values.
        foreach (int which in new[] { 0, 1, 2, 3 })
        {
            var bad = new SimulationConfig().Neolithic;
            if (which == 0) bad.SocialCohesionExponent = 0; else if (which == 1) bad.CohesionScalePaleolithic = 0; else if (which == 2) bad.CohesionScaleNomadic = 0; else bad.CohesionScaleAgricultural = 0;
            bool rejected = false; try { bad.Validate(); } catch (ArgumentException) { rejected = true; }
            Check(ref checks, rejected, "non-positive cohesion value " + which + " rejected");
        }

        Console.WriteLine("PASS: " + checks + " QoL cohesion assertions");
        return checks;
    }
}
