using System;
using System.Collections.Generic;
using System.Linq;
using Anthropocene.Simulation;

internal static class HusbandryDiffusionChecks
{
    private static int checks;
    private static void Check(bool ok, string message)
    {
        checks++;
        if (!ok) throw new Exception("Husbandry diffusion: " + message);
    }
    private static void Near(double a, double b, string message, double tolerance = 1e-9)
        => Check(Math.Abs(a - b) <= tolerance * Math.Max(1, Math.Abs(b)),
            message + " (" + a + " vs " + b + ")");

    private static WorldState Settlement(Technology primary)
    {
        var world = NeolithicChecks.Fixture(false);
        var camp = world.Camps.Single();
        camp.Technology = primary;
        camp.Technologies[(int)primary] = true;
        camp.People.Progression.Era = Era.Neolithic;
        world.Config.SecondaryTechnologyThreshold = 100;
        world.Config.HusbandryProximityPracticePerYear = 2.5;
        world.Config.HusbandryProximityReferenceAnimals = 20;
        world.Config.HusbandryDomesticExposureMultiplier = 2;
        world.Config.TamingSpeciesPracticeThreshold = 50;
        world.Config.HusbandryTamingEnabled = true;
        world.Config.TurnDays = 20 * world.Config.DaysPerYear;
        Ownership.Bind(world);
        return world;
    }

    private static Herd WildNear(WorldState world, Camp camp, int animals = 20,
        string species = "goat")
    {
        int tile = world.Map.Neighbors(camp.Tile)
            .Where(id => world.Tiles[id].TraversableLand)
            .OrderBy(id => id).First();
        var herd = new Herd
        {
            Id = world.NextId++, Tile = tile, Species = species,
            Animals = animals, Capacity = 100
        };
        world.Herds.Add(herd);
        return herd;
    }

    private static void ProximityUnlocksFor(Technology primary)
    {
        var world = Settlement(primary);
        var camp = world.Camps.Single();
        var herd = WildNear(world, camp, 20);
        double hunting = camp.Experience[(int)Domain.Hunting];
        double neoHunting = camp.People.NeolithicExperience[(int)Domain.Hunting];

        double first = TechnologyDiscovery.RecordHusbandryProximity(world);
        Near(first, 50, primary + " first full-herd proximity credit");
        Near(camp.People.TechnologyExposure[(int)Domain.Hunting], 50,
            primary + " stores Husbandry exposure separately");
        Near(camp.Experience[(int)Domain.Hunting], hunting,
            primary + " proximity does not increase hunting proficiency");
        Near(camp.People.NeolithicExperience[(int)Domain.Hunting], neoHunting,
            primary + " proximity does not masquerade as activity practice");
        Near(camp.People.SpeciesExperience.Single(p => p.Species == herd.Species).Experience,
            50, primary + " records species familiarity for later taming");
        Check(!camp.Technologies[(int)Technology.AnimalHusbandry] &&
            !world.Herds.Any(h => h.DomesticCamp == camp.Id),
            primary + " knowledge exposure alone neither unlocks early nor creates animals");

        double second = TechnologyDiscovery.RecordHusbandryProximity(world);
        Near(second, 50, primary + " second proximity credit");
        TechnologyDiscovery.Resolve(world, new TurnRecord());
        Check(camp.Technologies[(int)Technology.AnimalHusbandry],
            primary + " reaches Husbandry through repeated local exposure");
        Check(!world.Herds.Any(h => h.DomesticCamp == camp.Id),
            primary + " Husbandry discovery still does not create livestock");
    }

    private static void Proximity()
    {
        ProximityUnlocksFor(Technology.Cultivation);
        ProximityUnlocksFor(Technology.Watercraft);

        // Foreign managed animals are stronger demonstrations: ten animals at
        // 2x weight equal the twenty-animal reference exposure.
        var wild = Settlement(Technology.Cultivation);
        var wildCamp = wild.Camps.Single();
        WildNear(wild, wildCamp, 10);
        TechnologyDiscovery.RecordHusbandryProximity(wild);
        Near(wildCamp.People.TechnologyExposure[(int)Domain.Hunting], 25,
            "ten wild animals provide half of the reference exposure");

        var managed = Settlement(Technology.Cultivation);
        var subject = managed.Camps.Single();
        int rivalTile = managed.Map.Radius(subject.Tile, 6)
            .Where(id => managed.Map.Distance(subject.Tile, id) == 6 &&
                managed.Tiles[id].TraversableLand).OrderBy(id => id).First();
        var rival = new Camp
        {
            Id = managed.NextId++, Player = 1, Tile = rivalTile, Population = 20
        };
        managed.Camps.Add(rival); managed.Config.Players = 2; managed.PlayerCount = 2;
        Ownership.Bind(managed);
        int adjacent = managed.Map.Neighbors(subject.Tile)
            .Where(id => managed.Tiles[id].TraversableLand).OrderBy(id => id).First();
        managed.Herds.Add(new Herd
        {
            Id = managed.NextId++, Tile = adjacent, Species = "goat",
            Animals = 10, Capacity = 100, DomesticCamp = rival.Id,
            OwnerSociety = rival.SocietyId
        });
        TechnologyDiscovery.RecordHusbandryProximity(managed);
        Near(subject.People.TechnologyExposure[(int)Domain.Hunting], 50,
            "ten foreign managed animals provide a full reference exposure");
    }

    private static void TradeContact()
    {
        var world = Settlement(Technology.Cultivation);
        var receiver = world.Camps.Single().People;
        var teacher = new Society
        {
            Id = 999, Player = 2, Technology = Technology.AnimalHusbandry,
            Technologies = new bool[4], Progression = new EraProgression { Era = Era.Neolithic }
        };
        teacher.Technologies[(int)Technology.AnimalHusbandry] = true;
        teacher.Technologies[(int)Technology.Watercraft] = true;
        world.Config.TradePracticePerValue = .1;
        double credited = TechnologyDiscovery.RecordTradeContactPractice(
            world, receiver, teacher, 500);
        Near(credited, 100, "one trade contact credits each unknown counterparty technology");
        Near(receiver.TechnologyExposure[(int)Domain.Hunting], 50,
            "trade contact carries Husbandry knowledge");
        Near(receiver.TechnologyExposure[(int)Domain.Fishing], 50,
            "trade contact carries Watercraft knowledge");
        Check(receiver.Experience.All(x => x == 0) &&
            receiver.NeolithicExperience.All(x => x == 0),
            "trade contact changes no performance practice");
    }

    private static void SettledTradePath()
    {
        var world = Settlement(Technology.Cultivation);
        var requester = world.Camps.Single();
        requester.Inventory.HousingMaterial = 10;

        var responder = new Camp
        {
            Id = world.NextId++, Player = 1,
            Tile = world.Map.Radius(requester.Tile, 4)
                .Where(id => world.Map.Distance(requester.Tile, id) == 4 && world.Tiles[id].TraversableLand)
                .OrderBy(id => id).First(),
            Population = 40
        };
        responder.Inventory.Cloth = 10;
        world.Camps.Add(responder);
        world.Config.Players = 2; world.PlayerCount = 2;
        Ownership.Bind(world);
        responder.People.Progression.Era = Era.Neolithic;
        responder.Technology = Technology.AnimalHusbandry;
        responder.Technologies[(int)Technology.AnimalHusbandry] = true;
        responder.Technologies[(int)Technology.Watercraft] = true;
        world.Config.TradePracticePerValue = .1;

        var plan = new TradePlan();
        plan.Agreements.Add(new TradeAgreement
        {
            Requester = requester.Id, Responder = responder.Id,
            Distance = world.Map.Distance(requester.Tile, responder.Tile),
            Want = TradeGood.Cloth, Give = TradeGood.HousingMaterial,
            WantQuantity = 1, GiveQuantity = 1, Ratio = 1,
            WantParts = new[] { 1.0 }, GiveParts = new[] { 1.0 },
            RequesterGain = 100, ResponderGain = 100
        });
        var record = new TurnRecord { Turn = world.Turn };
        var settlement = Trade.Settle(world, plan, record);
        Trade.SettleAfterProduction(world, settlement, record);
        Trade.Finish(world, settlement, record);

        Check(record.Trades.Count == 1 && record.Trades[0].Delivered == 1,
            "the bilateral knowledge witness settles one real all-stock barter");
        Check(requester.People.TechnologyExposure[(int)Domain.Hunting] > 0 &&
            requester.People.TechnologyExposure[(int)Domain.Fishing] > 0,
            "the requester learns both unknown opening technologies held by its counterparty");
        Check(responder.People.TechnologyExposure[(int)Domain.Gathering] > 0,
            "the responder learns Cultivation knowledge from the requester");
        Check(requester.Experience[(int)Domain.Hunting] == 0 &&
            requester.Experience[(int)Domain.Fishing] == 0 &&
            responder.Experience[(int)Domain.Gathering] == 0,
            "settled bilateral knowledge leaves performance practice unchanged");
    }

    private static void Taming()
    {
        var world = Settlement(Technology.Cultivation);
        var camp = world.Camps.Single();
        camp.Technologies[(int)Technology.AnimalHusbandry] = true;
        var wild = WildNear(world, camp, 20);
        camp.People.SpeciesExperience.Add(new SpeciesPractice
        {
            Species = wild.Species,
            Experience = world.Config.TamingSpeciesPracticeThreshold
        });
        int before = world.Herds.Sum(h => h.Animals);
        var record = new TurnRecord();
        int captured = HusbandryAcquisition.ResolveTaming(world,
            new[] { new HusbandryAcquisition.Opportunity(camp.Id, wild.Id) }, record);
        var domestic = world.Herds.Single(h => h.DomesticCamp == camp.Id);
        Check(captured == world.Config.InitialDomesticAnimals &&
            domestic.Animals == world.Config.InitialDomesticAnimals,
            "a qualified interaction seeds the configured managed herd");
        Check(wild.Animals == 20 - captured && world.Herds.Sum(h => h.Animals) == before,
            "taming transfers live animals and conserves the total herd population");
        Check(record.ManagedHerds.Count == 0,
            "boundary taming does not retroactively produce managed-herd receipts");

        // Existing animals only fill the seed to the target.
        var partial = Settlement(Technology.Cultivation);
        var pc = partial.Camps.Single();
        pc.Technologies[(int)Technology.AnimalHusbandry] = true;
        var pw = WildNear(partial, pc, 20);
        pc.People.SpeciesExperience.Add(new SpeciesPractice
        {
            Species = pw.Species, Experience = 100
        });
        partial.Herds.Add(new Herd
        {
            Id = partial.NextId++, Tile = pc.Tile, Species = pw.Species,
            Animals = 4, DomesticCamp = pc.Id, OwnerSociety = pc.SocietyId
        });
        Check(HusbandryAcquisition.ResolveTaming(partial,
            new[] { new HusbandryAcquisition.Opportunity(pc.Id, pw.Id) },
            new TurnRecord()) == 1,
            "taming fills an existing seed herd only to the configured target");

        // Knowledge, species familiarity, proximity and a real opportunity are
        // all independently necessary.
        var noTech = Settlement(Technology.Cultivation); var nc = noTech.Camps.Single();
        var nw = WildNear(noTech, nc, 20);
        nc.People.SpeciesExperience.Add(new SpeciesPractice { Species = nw.Species, Experience = 100 });
        Check(HusbandryAcquisition.ResolveTaming(noTech,
            new[] { new HusbandryAcquisition.Opportunity(nc.Id, nw.Id) },
            new TurnRecord()) == 0, "no Husbandry knowledge means no taming");

        var unfamiliar = Settlement(Technology.Cultivation); var uc = unfamiliar.Camps.Single();
        uc.Technologies[(int)Technology.AnimalHusbandry] = true;
        var uw = WildNear(unfamiliar, uc, 20);
        Check(HusbandryAcquisition.ResolveTaming(unfamiliar,
            new[] { new HusbandryAcquisition.Opportunity(uc.Id, uw.Id) },
            new TurnRecord()) == 0, "no species familiarity means no taming");

        var distant = Settlement(Technology.Cultivation); var dc = distant.Camps.Single();
        dc.Technologies[(int)Technology.AnimalHusbandry] = true;
        dc.People.SpeciesExperience.Add(new SpeciesPractice { Species = "goat", Experience = 100 });
        int far = distant.Map.Radius(dc.Tile, 2)
            .Where(id => distant.Map.Distance(dc.Tile, id) == 2 &&
                distant.Tiles[id].TraversableLand).OrderBy(id => id).First();
        var dh = new Herd { Id = distant.NextId++, Tile = far, Species = "goat", Animals = 20, Capacity = 100 };
        distant.Herds.Add(dh);
        Check(HusbandryAcquisition.ResolveTaming(distant,
            new[] { new HusbandryAcquisition.Opportunity(dc.Id, dh.Id) },
            new TurnRecord()) == 0, "taming requires a local interacted herd");
    }

    public static int Run()
    {
        checks = 0;
        Proximity();
        TradeContact();
        SettledTradePath();
        Taming();
        Console.WriteLine("PASS: " + checks + " husbandry diffusion assertions");
        return checks;
    }
}
