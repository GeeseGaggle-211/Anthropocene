using System;
using System.Collections.Generic;
using System.Linq;
using Anthropocene.Simulation;

internal static class AgentContractChecks
{
    private static int count;
    private static void Check(bool ok, string message) { count++; if (!ok) throw new Exception("Agent contract: " + message); }
    public static int Run()
    {
        count = 0;
        var w = new WorldGenerator().Generate(1729, SimulationConfig.Small(3));
        var c = w.Camps[0]; var rival = w.Camps[1];
        const int ridgeMask = 0x2D;
        w.Tiles[c.Tile].RidgeMask = ridgeMask;
        Check(w.Tiles[c.Tile].RidgeMask != 0, "ridge fixture starts from a nonzero source mask");
        var hunter = new Hunter { Id = w.NextId++, Player = c.Player, SourceCamp = c.Id, SocietyId = c.SocietyId, Population = 8, Tile = c.Tile };
        w.Hunters.Add(hunter); c.Population -= 8;
        var wild = new Herd { Id = w.NextId++, Animals = 12, Tile = c.Tile }; w.Herds.Add(wild);
        rival.Tile = w.Map.Neighbors(c.Tile).First(n => n != c.Tile);
        var builder = new AgentObservationBuilder(); var policy = new HeuristicTribePolicy(); var o = builder.Build(w, c.Player);
        string fingerprint = SaveCodec.Fingerprint(w), json = ObservationContract.Serialize(o, c.Mandate);
        var dto = ObservationContract.Deserialize(json);
        Check(o.Tiles[c.Tile].RidgeMask == ridgeMask && dto.Tiles.Single(t => t.Id == c.Tile).RidgeMask == ridgeMask, "nonzero source ridge mask reaches the filtered observation and serialized DTO exactly");
        var replay = SaveCodec.Load(SaveCodec.Save(w));
        var replayObservation = builder.Build(replay, c.Player);
        var replayDto = ObservationContract.Deserialize(ObservationContract.Serialize(replayObservation, c.Mandate));
        Check(replay.Tiles[c.Tile].RidgeMask == ridgeMask && replayObservation.Tiles[c.Tile].RidgeMask == ridgeMask && replayDto.Tiles.Single(t => t.Id == c.Tile).RidgeMask == ridgeMask && SaveCodec.Fingerprint(replay) == fingerprint, "ridge mask survives save/load replay through source, observation and DTO");
        Check(dto.Schema == AgentObservation.SchemaVersion && dto.Tiles.Length == o.Tiles.Count && dto.Hunters.Single().SourceCamp == c.Id && dto.Experience.SequenceEqual(c.Experience), "serializable observation retains the filtered policy inputs and source settlement");
        Check(dto.SiteCoverage.Length == o.SiteCoverage.Count && dto.SiteCoverage.All(s =>
            s.HumanFood >= 0 && s.HumanFood <= 3 && s.HerdForage >= 0 && s.HerdForage <= 3 &&
            s.HerdWater >= 0 && s.HerdWater <= 3 &&
            Math.Abs(s.HumanFood * 20 - Math.Round(s.HumanFood * 20)) < 1e-8 &&
            Math.Abs(s.HerdForage * 20 - Math.Round(s.HerdForage * 20)) < 1e-8 &&
            Math.Abs(s.HerdWater * 20 - Math.Round(s.HerdWater * 20)) < 1e-8),
            "candidate-site coverage remains local, capped and quantized in the serialized observation");
        Check(dto.Herds.Single(h => h.Id == wild.Id).RepresentativeCount == 10 && !dto.Herds.Single(h => h.Id == wild.Id).ExactCount, "serialized wild counts remain estimates");
        Check(dto.LegalActions.All(m => m.Actions.All(a => OrderRules.Validate(o, a) == null && m.Allows(a))), "every enumerated action is accepted by the shared validator");
        Check(dto.LegalActions.First(m => m.Entity == c.Id).MaximumDispatch == Math.Min(c.GathererPopulation, c.Population - w.Config.MinimumCamp), "dispatch range exposes the actual available workforce");
        Check(OrderRules.Validate(o, new Order(OrderKind.NoOp, hunter.Id, destination: 0)) != null && OrderRules.Validate(o, new Order(OrderKind.Dispatch, c.Id, destination: 0, population: 1)) != null, "non-movement orders cannot smuggle an unvalidated destination");
        Check(SaveCodec.Fingerprint(w) == fingerprint, "observation and mask generation are read-only");
        rival.Population += 300; rival.Food += 100000; wild.Animals = 14;
        int hidden = Enumerable.Range(0, w.Tiles.Length).First(n => !o.Tiles.ContainsKey(n)); w.Tiles[hidden].Food += 123456;
        w.History.Add(new TurnRecord { Events = { "secret rival event" }, Orders = { "secret rival order" } });
        Check(ObservationContract.Serialize(builder.Build(w, c.Player), c.Mandate) == json, "hidden stores, populations, tiles, rival orders and within-band wild counts do not change the DTO");
        var original = policy.Decide(builder.Build(w, c.Player), c.Mandate).Orders.Select(a => a.ToString()).ToArray();
        c.Player = 77; hunter.Player = 77; w.Societies.Single(s => s.Id == c.SocietyId).Player = 77;
        var relabeled = policy.Decide(builder.Build(w, 77), c.Mandate).Orders.Select(a => a.ToString()).ToArray();
        Check(original.SequenceEqual(relabeled), "relabeling player identity does not change policy choices");
        var index = new PlanningIndex(w); var indexed = builder.Build(w, 77, index);
        Check(ObservationContract.Serialize(indexed, c.Mandate) == ObservationContract.Serialize(builder.Build(w, 77), c.Mandate), "shared spatial snapshot and standalone observation agree");
        Check(indexed.Tiles[c.Tile].RidgeMask == ridgeMask, "shared spatial snapshot retains the exact ridge mask");
        var preferences = Enumerable.Range(1, 200).Select(seed => PolicyState.Seeded(seed)).ToArray();
        Check(preferences.Any(p => p.HuntingInclination > p.GatheringInclination) && preferences.Any(p => p.FishingInclination > p.GatheringInclination) && preferences.Any(p => p.GatheringInclination > Math.Max(p.HuntingInclination, p.FishingInclination)), "seeded policy state supports all three preferences without player-ID cohorts");
        HusbandryMovement();
        AgriculturalMovement();
        Console.WriteLine("PASS: " + count + " agent-contract assertions"); return count;
    }

    private static void HusbandryMovement()
    {
        var world = NeolithicChecks.Fixture(false); var camp = world.Camps[0];
        camp.Technology = Technology.AnimalHusbandry;
        camp.Technologies[(int)Technology.AnimalHusbandry] = true;
        camp.People.Progression.Era = Era.Neolithic;
        camp.PlantFood = 100000;
        world.Herds.Add(new Herd { Id = world.NextId++, Tile = camp.Tile, DomesticCamp = camp.Id,
            OwnerSociety = camp.SocietyId, Animals = 100, Capacity = 200 });
        int destination = world.Map.Neighbors(camp.Tile).Where(id => world.Tiles[id].TraversableLand).OrderBy(id => id).First();
        int rested = world.Map.Neighbors(destination).Where(id => world.Map.Distance(camp.Tile, id) == 2 &&
            world.Tiles[id].TraversableLand).OrderBy(id => id).First();
        foreach (var tile in world.Tiles)
        {
            tile.Food = tile.PotentialFood = tile.Freshwater = 0;
            for (int season = 0; season < tile.Seasons.Length; season++)
            {
                var profile = tile.Seasons[season];
                profile.MaxFood = profile.MaxPotentialFood = profile.MaxFreshwater = 0;
                profile.WaterRecoveryPerDay = 0;
                tile.Seasons[season] = profile;
            }
        }
        var fresh = world.Tiles[rested]; fresh.PotentialFood = 50000; fresh.Freshwater = 50000;
        for (int season = 0; season < fresh.Seasons.Length; season++)
        {
            var profile = fresh.Seasons[season];
            profile.MaxPotentialFood = 50000; profile.MaxFreshwater = 50000;
            fresh.Seasons[season] = profile;
        }
        var observation = new AgentObservationBuilder().Build(world, camp.Player);
        ValidationOnlyObservationMatchesActions(world, camp, observation);
        var current = observation.SiteCoverage.Single(s => s.Tile == camp.Tile);
        var roundTrip = ObservationContract.Deserialize(ObservationContract.Serialize(observation, camp.Mandate));
        Check(roundTrip.SiteCoverage.Length == observation.SiteCoverage.Count && roundTrip.SiteCoverage.All(s =>
            s.HumanFood >= 0 && s.HumanFood <= 3 && s.HerdForage >= 0 && s.HerdForage <= 3 &&
            s.HerdWater >= 0 && s.HerdWater <= 3 &&
            Math.Abs(s.HumanFood * 20 - Math.Round(s.HumanFood * 20)) < 1e-8 &&
            Math.Abs(s.HerdForage * 20 - Math.Round(s.HerdForage * 20)) < 1e-8 &&
            Math.Abs(s.HerdWater * 20 - Math.Round(s.HerdWater * 20)) < 1e-8),
            "husbandry support estimates survive the filtered observation contract");
        Check(current.HerdForage == 0 && current.HerdWater == 0,
            "the filtered husbandry view detects exhausted local pasture and water");
        var chosen = new HeuristicTribePolicy().Decide(observation, camp.Mandate).Orders.Single(o => o.Entity == camp.Id);
        Check(chosen.Kind == OrderKind.Move && chosen.Destination != camp.Tile &&
            world.Map.Distance(chosen.Destination, rested) <= 1,
            "the shared tribe policy moves domestic animals toward rested local forage and water");
    }

    private static void AgriculturalMovement()
    {
        WorldState Fixture(bool developed)
        {
            var world = NeolithicChecks.Fixture(); var camp = world.Camps[0];
            camp.Mandate.Mobility = 1;
            if (developed) camp.Workforce.Assign(WorkforceRoles.Farmer, camp.Population, camp.Population);
            var local = new HashSet<int>(world.Map.Radius(camp.Tile, 1));
            foreach (var tile in world.Tiles)
            {
                bool rich = world.Map.Distance(camp.Tile, tile.Id) == 2;
                tile.Food = rich ? 180 : 5;
                tile.PotentialFood = rich ? 1600 : 800;
                for (int s = 0; s < tile.Seasons.Length; s++)
                {
                    var profile = tile.Seasons[s];
                    profile.MaxFood = rich ? 180 : 5;
                    profile.MaxPotentialFood = rich ? 1600 : 800;
                    tile.Seasons[s] = profile;
                }
                if (developed && local.Contains(tile.Id))
                {
                    tile.CultivationOperator = camp.Id;
                    tile.CultivationCapital = 1;
                    tile.CultivationOperatingFraction = 1;
                }
            }
            return world;
        }
        var developed = Fixture(true); var camp = developed.Camps[0];
        var view = new AgentObservationBuilder().Build(developed, camp.Player);
        ValidationOnlyObservationMatchesActions(developed, camp, view);
        var dto = ObservationContract.Deserialize(ObservationContract.Serialize(view, camp.Mandate));
        Check(dto.AgriculturalSites.Length == view.AgriculturalSites.Count && dto.AgriculturalSites.All(s =>
            s.ManagedRecoveryCoverage >= 0 && s.ManagedRecoveryCoverage <= 10 && s.LostRebuildBurden >= 0 &&
            Math.Abs(s.ManagedRecoveryCoverage * 20 - Math.Round(s.ManagedRecoveryCoverage * 20)) < 1e-8),
            "own-field forward payoff is capped and quantized in the filtered agricultural observation");
        var developedPayoff = view.AgriculturalSites.Single(s => s.Tile == camp.Tile);
        Check(developedPayoff.ManagedRecoveryCoverage > 0,
            "operated depleted fields have a positive bounded coming-turn food benefit");
        int visibleOutsideField = developed.Map.Radius(camp.Tile, 2)
            .First(id => developed.Map.Distance(camp.Tile, id) == 2);
        string beforeOwnership = ObservationContract.Serialize(view, camp.Mandate);
        developed.Tiles[visibleOutsideField].CultivationOperator = camp.Id + 1000;
        Check(ObservationContract.Serialize(new AgentObservationBuilder().Build(developed, camp.Player), camp.Mandate) == beforeOwnership,
            "foreign field ownership beyond the camp footprint cannot leak into agricultural site estimates");
        var unworked = Fixture(true); var unworkedCamp = unworked.Camps[0];
        unworkedCamp.Workforce.Release(WorkforceRoles.Farmer, unworkedCamp.Population, unworkedCamp.Population);
        foreach (int id in unworked.Map.Radius(unworkedCamp.Tile, 1))
        {
            var tile = unworked.Tiles[id];
            var capacity = new EcologyCapacity(unworked, tile, new EcologyView(tile), unworked.Season);
            tile.Food = Math.Min(tile.PotentialFood, capacity.Food);
        }
        var unworkedView = new AgentObservationBuilder().Build(unworked, unworkedCamp.Player);
        var unworkedPayoff = unworkedView.AgriculturalSites.Single(s => s.Tile == unworkedCamp.Tile);
        Check(unworkedPayoff.ManagedRecoveryCoverage == 0 &&
            unworkedView.AgriculturalSites.All(s => s.LostRebuildBurden == 0),
            "full fields without operating labor cannot invent managed renewal or an abandonment penalty");
        var stay = new HeuristicTribePolicy().Decide(view, camp.Mandate).Orders.Single(o => o.Entity == camp.Id);
        Check(stay.Kind == OrderKind.NoOp,
            "developed own fields and their coming managed recovery retain an agricultural camp at a productive site");
        camp.IsVillage = true;
        camp.PlantFood = 100000;
        var secureVillage = new AgentObservationBuilder().Build(developed, camp.Player);
        Check(secureVillage.AgriculturalSites.Count == 0 &&
            new HeuristicTribePolicy().Decide(secureVillage, camp.Mandate).Orders.Single(o => o.Entity == camp.Id).Kind == OrderKind.NoOp,
            "secure villages skip relocation forecasts and do not abandon fields");
        var virgin = Fixture(false); var freshCamp = virgin.Camps[0];
        var move = new HeuristicTribePolicy().Decide(new AgentObservationBuilder().Build(virgin, freshCamp.Player), freshCamp.Mandate)
            .Orders.Single(o => o.Entity == freshCamp.Id);
        Check(move.Kind == OrderKind.Move && virgin.Map.Distance(freshCamp.Tile, move.Destination) == 1,
            "a camp without developed fields moves toward clearly richer land even under the agricultural policy");
        var depleted = Fixture(true); var depletedCamp = depleted.Camps[0];
        foreach (int id in depleted.Map.Radius(depletedCamp.Tile, 1))
        {
            var tile = depleted.Tiles[id];
            tile.Food = tile.PotentialFood = 0;
            tile.CultivationCapital = 0;
            tile.CultivationOperator = -1;
            for (int s = 0; s < tile.Seasons.Length; s++)
            {
                var profile = tile.Seasons[s]; profile.MaxFood = profile.MaxPotentialFood = 0;
                tile.Seasons[s] = profile;
            }
        }
        var escape = new HeuristicTribePolicy().Decide(new AgentObservationBuilder().Build(depleted, depletedCamp.Player), depletedCamp.Mandate)
            .Orders.Single(o => o.Entity == depletedCamp.Id);
        Check(escape.Kind == OrderKind.Move && depleted.Map.Distance(depletedCamp.Tile, escape.Destination) == 1,
            "a depleted agricultural camp can relocate rather than receiving a fixed wait order");
        depletedCamp.IsVillage = true;
        depletedCamp.PlantFood = 0;
        var emergencyVillage = new AgentObservationBuilder().Build(depleted, depletedCamp.Player);
        var emergencyChoice = new HeuristicTribePolicy().Decide(emergencyVillage, depletedCamp.Mandate)
            .Orders.Single(o => o.Entity == depletedCamp.Id);
        Check(emergencyVillage.AgriculturalSites.Count > 0 && emergencyChoice.Kind == OrderKind.Move,
            "the same AI can abandon a village in a severe food emergency");
    }

    private static void ValidationOnlyObservationMatchesActions(WorldState world, Camp camp, AgentObservation full)
    {
        var validation = new AgentObservationBuilder().Build(world, camp.Player,
            includeCandidateForecasts: false);
        Check(validation.SiteCoverage.Count == 0 && validation.AgriculturalSites.Count == 0,
            "order validation skips movement-only candidate forecasts");
        var expected = LegalActions.Build(full);
        var actual = LegalActions.Build(validation);
        Check(expected.Length == actual.Length && expected.Zip(actual, (left, right) =>
            left.Entity == right.Entity && left.MinimumDispatch == right.MinimumDispatch &&
            left.MaximumDispatch == right.MaximumDispatch &&
            left.Actions.Select(a => a.ToString()).SequenceEqual(right.Actions.Select(a => a.ToString())))
            .All(equal => equal),
            "validation-only observation preserves all legal action masks");
        foreach (var order in expected.SelectMany(mask => mask.Actions))
            Check(OrderRules.Validate(full, order) == OrderRules.Validate(validation, order),
                "validation-only observation preserves accepted order decisions");
        var rejected = new Order(OrderKind.Move, camp.Id, destination: world.Tiles.Length + 1);
        Check(OrderRules.Validate(full, rejected) == OrderRules.Validate(validation, rejected),
            "validation-only observation preserves rejected movement decisions");
    }
}
