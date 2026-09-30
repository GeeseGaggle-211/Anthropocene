using System;
using System.IO;
using System.Linq;
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
    // Housing structure set directly from people capacities: fixed people in permanent housing, tent people in portable Light Housing.
    private static void House(WorldState w, Camp c, double fixedPeople, double tentPeople)
    {
        var n = w.Config.Neolithic;
        w.FixedHousing.RemoveAll(x => x.CampId == c.Id);
        if (fixedPeople > 0) w.FixedHousing.Add(new FixedHousingStock { CampId = c.Id, Tile = c.Tile, Units = fixedPeople / n.HousingPeoplePerUnit, LastUpdatedDays = w.Days });
        c.Inventory.LightHousingUnits = tentPeople / n.LightHousingPeoplePerUnit;
    }
    // Closed forms written out from the design, independent of the production helpers.
    private static double H(NeolithicConfig n, double fixedPeople, double tentPeople, double people) => people <= 0 ? 0 : Math.Min(1, (fixedPeople + n.LightHousingPermanence * tentPeople) / people);
    private static double Ns(NeolithicConfig n, double h) => n.CohesionBandScale * (1 + n.CohesionHousingGain * h);
    private static double S(NeolithicConfig n, double h, double people) => Math.Exp(-Math.Pow(people / Ns(n, h), n.SocialCohesionExponent));

    public static int Run()
    {
        int checks = 0;
        var n = new SimulationConfig().Neolithic;
        Check(ref checks, n.SocialCohesionExponent == 2 && n.CohesionBandScale == 220 && n.CohesionHousingGain == 7.1 && n.LightHousingPermanence == .21 && n.LightHousingPeoplePerUnit == 3 && n.HousingPeoplePerUnit == 4, "defaults");
        n.Validate();
        var fields = typeof(NeolithicConfig).GetFields().Select(f => f.Name).ToArray();
        Check(ref checks, !fields.Any(f => f.StartsWith("CohesionScale")), "the technology-labelled scale fields are gone");

        // Closed form of H and N_s: no housing, all tents, all fixed, mixes, over-capacity capped at 1.
        var wh = World(false, Technology.None); var ch = wh.Camps[0]; ch.Population = 100;
        var cases = new (double fixedP, double tentP, double expectedH, string name)[]
        {
            (0, 0, 0, "no housing"), (0, 100, n.LightHousingPermanence, "all tents"), (100, 0, 1, "all permanent"),
            (40, 30, .4 + n.LightHousingPermanence * .3, "mix"), (20, 20, .2 + n.LightHousingPermanence * .2, "small mix"),
            (250, 0, 1, "over-capacity fixed is capped at 1"), (0, 1000, 1, "over-capacity tents are capped at 1"), (60, 200, 1, "mixed over-capacity is capped at 1")
        };
        foreach (var k in cases)
        {
            House(wh, ch, k.fixedP, k.tentP);
            var shelter = NeolithicProduction.HousingCapacity(wh, ch, wh.Days);
            Near(ref checks, shelter.Fixed, k.fixedP, 1e-9, k.name + ": fixed people capacity");
            Near(ref checks, shelter.Light, k.tentP, 1e-9, k.name + ": tent people capacity");
            double h = NeolithicProduction.HousingShare(n, shelter, 100);
            Near(ref checks, h, k.expectedH, 1e-12, k.name + ": H");
            Near(ref checks, NeolithicProduction.CohesionScale(n, h), n.CohesionBandScale * (1 + n.CohesionHousingGain * k.expectedH), 1e-9, k.name + ": N_s = N_band (1 + k H)");
            Near(ref checks, NeolithicProduction.SocialCohesion(wh, ch, 100), Math.Exp(-Math.Pow(100 / (n.CohesionBandScale * (1 + n.CohesionHousingGain * k.expectedH)), 2)), 1e-15, k.name + ": S closed form");
        }
        Near(ref checks, NeolithicProduction.HousingShare(n, new NeolithicProduction.ShelterCapacity(0, 0), 0), 0, 0, "empty cohort without housing has H = 0");
        Near(ref checks, NeolithicProduction.HousingShare(n, new NeolithicProduction.ShelterCapacity(10, 0), 0), 1, 0, "empty cohort with housing has H = 1");
        // Paleolithic camps with no housing get N_band; S(0) = 1 and S strictly decreases in N.
        House(wh, ch, 0, 0);
        Near(ref checks, NeolithicProduction.CohesionScale(wh, ch, 100), n.CohesionBandScale, 0, "no housing gives N_band");
        Near(ref checks, NeolithicProduction.SocialCohesion(wh, ch, 0), 1, 0, "S(0)=1");
        double prior = 1;
        for (int pop = 10; pop <= 3000; pop += 10)
        {
            double s = NeolithicProduction.SocialCohesion(wh, ch, pop);
            Check(ref checks, s < prior && s > 0, "S strictly decreasing at N=" + pop);
            Near(ref checks, s, Math.Exp(-Math.Pow(pop / n.CohesionBandScale, 2)), 1e-15, "closed form N=" + pop);
            prior = s;
        }
        // Housing raises S at a fixed headcount, and the tent share never exceeds the fixed share for equal capacity.
        House(wh, ch, 0, 100); double sTent = NeolithicProduction.SocialCohesion(wh, ch, 100);
        House(wh, ch, 100, 0); double sFixed = NeolithicProduction.SocialCohesion(wh, ch, 100);
        Check(ref checks, NeolithicProduction.SocialCohesion(n, 0, 100) < sTent && sTent < sFixed, "S rises with housing: none < tents < permanent at equal capacity");

        // Capacity-based coverage: 3 people per tent, 4 per house.
        var wc = World(true, Technology.Cultivation); wc.Config.Neolithic.LightHousingPeoplePerUnit = 3; var cc = wc.Camps[0]; cc.Population = 100; cc.Inventory.Cloth = 100; cc.StationaryYears = 100;
        cc.Inventory.LightHousingUnits = 10; NeolithicProduction.ResolveNeeds(wc, cc, 0);
        Near(ref checks, cc.HousingPerCapita, .30, 1e-12, "10 tents shelter 30 of 100 people");
        cc.Inventory.LightHousingUnits = 0; wc.FixedHousing.Add(new FixedHousingStock { CampId = cc.Id, Tile = cc.Tile, Units = 10, LastUpdatedDays = wc.Days });
        NeolithicProduction.ResolveNeeds(wc, cc, 0);
        Near(ref checks, cc.HousingPerCapita, .40, 1e-12, "10 houses shelter 40 of 100 people");
        cc.Inventory.LightHousingUnits = 10; NeolithicProduction.ResolveNeeds(wc, cc, 0);
        Near(ref checks, cc.HousingPerCapita, .70, 1e-12, "10 houses and 10 tents shelter 70 of 100 people");
        Near(ref checks, NeolithicProduction.HousingGapPeople(wc, cc, 100, wc.Days), 30, 1e-12, "the housing gap is counted in people");
        // The shipped tent capacity is lower than the specified three: the same rule at LightHousingPeoplePerUnit.
        cc.Inventory.LightHousingUnits = 10; wc.Config.Neolithic.LightHousingPeoplePerUnit = n.LightHousingPeoplePerUnit; NeolithicProduction.ResolveNeeds(wc, cc, 0);
        Near(ref checks, cc.HousingPerCapita, (40 + 10 * n.LightHousingPeoplePerUnit) / 100, 1e-12, "10 houses and 10 tents at the shipped tent capacity");
        wc.Config.Neolithic.LightHousingPeoplePerUnit = 3; NeolithicProduction.ResolveNeeds(wc, cc, 0);
        // Demand targets people: the gap is 30 people, so 7.5 houses or 10 tents.
        Near(ref checks, NeolithicProduction.Demand(wc, cc, CraftGood.Housing, 0, 100, true, true), 7.5, 1e-9, "Housing demand is gap people / 4");
        Near(ref checks, NeolithicProduction.Demand(wc, cc, CraftGood.LightHousing, 0, 100, true, true), 10, 1e-9, "Light Housing demand is gap people / 3");

        // ResolveNeeds: Q = F*W*R*S with S at the camp's current housing and the cohort.
        foreach (var variant in new[] { (0.0, 0.0), (0.0, 50.0), (40.0, 0.0), (30.0, 30.0) })
            foreach (var technology in new[] { Technology.None, Technology.AnimalHusbandry, Technology.Cultivation })
            {
                var w = World(false, Technology.None); var c = w.Camps[0]; c.Technology = technology; if (technology != Technology.None) c.Technologies[(int)technology] = true;
                c.Population = 100; c.Inventory.Cloth = 40; c.FoodNeedsMetRatio = .8; c.WaterNeedsMetRatio = .9; House(w, c, variant.Item1, variant.Item2);
                foreach (int residents in new[] { -1, 60 })
                {
                    NeolithicProduction.ResolveNeeds(w, c, 0, residents);
                    var cfg = w.Config.Neolithic;
                    double clothing = 1 - c.RudimentaryExpectation * (1 - c.ClothPerCapita), housing = 1 - c.RudimentaryExpectation * (1 - c.HousingPerCapita);
                    double fwr = c.FoodNeedsMetRatio * c.WaterNeedsMetRatio * (cfg.RudimentaryWeightClothing * clothing + cfg.RudimentaryWeightHousing * housing);
                    int cohort = residents < 0 ? c.Population : residents;
                    Near(ref checks, c.QoL, fwr * NeolithicProduction.SocialCohesion(w, c, cohort), 1e-12, "ResolveNeeds QoL = F*W*R*S cohort " + cohort);
                    Near(ref checks, c.QoL, fwr * S(cfg, H(cfg, variant.Item1, variant.Item2, cohort), cohort), 1e-12, "closed form in ResolveNeeds " + technology + " " + variant + " cohort " + cohort);
                }
            }

        // Continuity: acquiring a technology with identical housing changes neither N_s nor S (no regime jump).
        foreach (var housing in new[] { (0.0, 0.0), (0.0, 400.0), (400.0, 0.0), (150.0, 150.0) })
        {
            var ws = World(false, Technology.Watercraft); var cs = ws.Camps[0];
            cs.Population = 400; cs.Inventory.Cloth = 400; cs.FoodNeedsMetRatio = cs.WaterNeedsMetRatio = 1; House(ws, cs, housing.Item1, housing.Item2);
            cs.StationaryYears = 100; NeolithicProduction.ResolveNeeds(ws, cs, 0);
            double before = cs.QoL, sBefore = NeolithicProduction.SocialCohesion(ws, cs, 400), scaleBefore = NeolithicProduction.CohesionScale(ws, cs, 400);
            Near(ref checks, sBefore, S(ws.Config.Neolithic, H(ws.Config.Neolithic, housing.Item1, housing.Item2, 400), 400), 1e-15, "S before acquisition " + housing);
            cs.Technologies[(int)Technology.AnimalHusbandry] = true; cs.Technologies[(int)Technology.Cultivation] = true; cs.Technology = Technology.Cultivation;
            cs.StationaryYears = 100; NeolithicProduction.ResolveNeeds(ws, cs, 0);
            Near(ref checks, NeolithicProduction.SocialCohesion(ws, cs, 400), sBefore, 0, "S unchanged across a Cultivation acquisition with identical housing " + housing);
            Near(ref checks, NeolithicProduction.CohesionScale(ws, cs, 400), scaleBefore, 0, "N_s unchanged across a Cultivation acquisition " + housing);
            Near(ref checks, cs.QoL, before, 1e-12, "QoL unchanged across a Cultivation acquisition with identical housing " + housing);
        }
        // Continuity in housing: S moves smoothly as capacity is added (no jump at any threshold).
        {
            var wk = World(true, Technology.Cultivation); var ck = wk.Camps[0]; double lastS = NeolithicProduction.SocialCohesion(wk, ck, 300), maxStep = 0;
            for (int people = 1; people <= 400; people++)
            {
                House(wk, ck, people, 0); double s = NeolithicProduction.SocialCohesion(wk, ck, 300);
                Check(ref checks, s >= lastS - 1e-15, "S is non-decreasing as permanent capacity grows");
                maxStep = Math.Max(maxStep, s - lastS); lastS = s;
            }
            Check(ref checks, maxStep < .03, "S rises in small steps with housing (largest step " + maxStep.ToString("G3") + ")");
        }
        // Purity: reading S or N_s leaves the world unchanged and is deterministic.
        {
            var wp = World(true, Technology.Cultivation); var cp = wp.Camps[0]; House(wp, cp, 120, 60);
            string fingerprint = SaveCodec.Fingerprint(wp); double first = NeolithicProduction.SocialCohesion(wp, cp, 250);
            for (int i = 0; i < 5; i++) Near(ref checks, NeolithicProduction.SocialCohesion(wp, cp, 250), first, 0, "S is deterministic");
            Check(ref checks, SaveCodec.Fingerprint(wp) == fingerprint, "cohesion reads do not mutate the world");
        }

        // Fission gain holds H at its present value when the relief weight is 0 and interpolates with the weight otherwise.
        {
            var wf = World(true, Technology.Cultivation); var cf = wf.Camps[0]; cf.Population = 200; cf.ClothPerCapita = 1; House(wf, cf, 80, 60);
            wf.Config.FissionHousingRelief = 0;
            double h0 = H(wf.Config.Neolithic, 80, 60, 200); int nd = Kinship.DaughterSize(200, FissionPolicy.Neutral), stay = 200 - nd;
            var e0 = Fission.EstimateSplit(wf, cf, FissionPolicy.Neutral);
            Near(ref checks, e0.Gain, S(wf.Config.Neolithic, h0, stay) / S(wf.Config.Neolithic, h0, 200) - 1, 1e-12, "relief 0: fission gain holds H at its present value, only the headcount changes");
            wf.Config.FissionHousingRelief = 1;
            double hRelieved = H(wf.Config.Neolithic, 80, 60 * (stay / 200.0), stay);
            var e1 = Fission.EstimateSplit(wf, cf, FissionPolicy.Neutral);
            Check(ref checks, hRelieved > h0 && e1.Gain > e0.Gain, "relief 1: the stayers' H rises and so does the gain");
            wf.Config.FissionHousingRelief = .5;
            var eh = Fission.EstimateSplit(wf, cf, FissionPolicy.Neutral);
            Check(ref checks, eh.Gain > e0.Gain && eh.Gain < e1.Gain, "relief 0.5 lies between the two gains");
        }
        // Immigration's forward estimator: arrivals bring no housing, so the destination's H is diluted by headcount; the source's H rises as it loses people.
        {
            var wi = World(true, Technology.Cultivation); var ci = wi.Camps[0]; ci.Population = 100; ci.ClothPerCapita = 1; ci.FoodNeedsMetRatio = ci.WaterNeedsMetRatio = 1; ci.RudimentaryExpectation = 1e-12; House(wi, ci, 70, 0);
            var ni = wi.Config.Neolithic; var model = Fission.QoLModelOf(wi, ci);
            Near(ref checks, model.At(100), S(ni, .7, 100) * (ni.RudimentaryWeightClothing + ni.RudimentaryWeightHousing * (1 - 1e-12 * .3)), 1e-9, "model at present residents uses the present H");
            double q130 = model.ForwardAt(130, 1e9), q100 = model.ForwardAt(100, 0);
            Near(ref checks, q130, S(ni, 70 / 130.0, 130), 1e-6, "destination: 30 arrivals dilute H to 70/130 (expectation ~0 isolates cohesion)");
            Near(ref checks, model.ForwardAt(70), S(ni, 1, 70), 1e-6, "source: after losing 30 the remaining 70 people are fully housed, H rises to 1");
            Check(ref checks, S(ni, 70 / 130.0, 130) < S(ni, .7, 130), "dilution lowers S relative to an undiluted share");
            Check(ref checks, model.ForwardAt(85) > model.ForwardAt(100), "losing people raises the source's forward QoL");
        }

        // Section 3.5 fission-onset table with Q* = 0.73 (closed form, B*R given).
        (double scale, double br, int expected)[] table =
        {
            (220, .90, 101), (220, 1.0, 123), (550, .90, 252), (550, 1.0, 309), (1780, .82, 607), (1780, 1.0, 999)
        };
        foreach (var row in table)
        {
            double onset = FissionOnset(n, row.scale, row.br, .73);
            Check(ref checks, Math.Abs(onset - row.expected) <= 1, "fission onset Ns=" + row.scale + " B*R=" + row.br + " gives " + onset + ", expected " + row.expected);
            double s = Math.Exp(-Math.Pow(onset / row.scale, n.SocialCohesionExponent));
            Near(ref checks, row.br * s, .73, 1e-12, "B*R*S(N*)=Q* for Ns=" + row.scale);
        }
        Near(ref checks, FissionOnset(n, 220, .70, .73), 0, 0, "N*=0 when B*R <= Q*");

        // Save round trip with the new fields; fingerprint stable.
        var saved = NeolithicChecks.Fixture(true); saved.Config.Neolithic.CohesionBandScale = 233; saved.Config.Neolithic.CohesionHousingGain = 5.5; saved.Config.Neolithic.LightHousingPermanence = .33; saved.Config.Neolithic.LightHousingPeoplePerUnit = 2; saved.Config.Neolithic.SocialCohesionExponent = 3;
        var loaded = SaveCodec.Load(SaveCodec.Save(saved));
        var ln = loaded.Config.Neolithic;
        Check(ref checks, ln.CohesionBandScale == 233 && ln.CohesionHousingGain == 5.5 && ln.LightHousingPermanence == .33 && ln.LightHousingPeoplePerUnit == 2 && ln.SocialCohesionExponent == 3, "save round trip keeps configured cohesion values");
        Check(ref checks, SaveCodec.Fingerprint(saved) == SaveCodec.Fingerprint(loaded), "fingerprint stable across round trip");

        // Older payload: no derived fields, but the retired label-switch fields present. Loaded worlds continue under the derived rule.
        var serializer = new DataContractSerializer(typeof(NeolithicConfig));
        string xml;
        using (var ms = new MemoryStream()) { serializer.WriteObject(ms, new SimulationConfig().Neolithic); xml = Encoding.UTF8.GetString(ms.ToArray()); }
        string stripped = Regex.Replace(xml, "<(SocialCohesionExponent|CohesionBandScale|CohesionHousingGain|LightHousingPermanence|LightHousingPeoplePerUnit)>[^<]*</\\1>", "");
        Check(ref checks, stripped.Length < xml.Length && !stripped.Contains("CohesionBandScale") && !stripped.Contains("LightHousingPermanence"), "fixture removed the new fields");
        string legacy = stripped.Replace("<CohesionScalePaleolithic", "<CohesionScalePaleolithic");
        int insert = legacy.LastIndexOf("</NeolithicConfig>"); if (insert < 0) insert = legacy.LastIndexOf("</", legacy.Length - 2);
        legacy = legacy.Insert(insert, "<CohesionScaleAgricultural>1780</CohesionScaleAgricultural><CohesionScaleNomadic>550</CohesionScaleNomadic><CohesionScalePaleolithic>220</CohesionScalePaleolithic>");
        NeolithicConfig old; bool read = true;
        try { using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(legacy))) old = (NeolithicConfig)serializer.ReadObject(ms); } catch (SerializationException) { read = false; old = null; }
        Check(ref checks, read, "a payload carrying the retired scale fields still deserializes (the fields are ignored)");
        Check(ref checks, old.SocialCohesionExponent == 2 && old.CohesionBandScale == n.CohesionBandScale && old.CohesionHousingGain == n.CohesionHousingGain && old.LightHousingPermanence == n.LightHousingPermanence && old.LightHousingPeoplePerUnit == n.LightHousingPeoplePerUnit, "legacy payload gets the derived-rule defaults");
        old.Validate();

        // Validation rejects non-positive or out-of-range values.
        foreach (int which in new[] { 0, 1, 2, 3, 4, 5 })
        {
            var bad = new SimulationConfig().Neolithic;
            if (which == 0) bad.SocialCohesionExponent = 0; else if (which == 1) bad.CohesionBandScale = 0; else if (which == 2) bad.LightHousingPeoplePerUnit = 0; else if (which == 3) bad.CohesionHousingGain = -1; else if (which == 4) bad.LightHousingPermanence = 1.5; else bad.LightHousingPermanence = -.1;
            bool rejected = false; try { bad.Validate(); } catch (ArgumentException) { rejected = true; }
            Check(ref checks, rejected, "invalid cohesion value " + which + " rejected");
        }

        Console.WriteLine("PASS: " + checks + " QoL cohesion assertions");
        return checks;
    }
}
