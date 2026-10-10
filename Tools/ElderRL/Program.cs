using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Anthropocene.Simulation;

namespace Anthropocene.Tools.ElderRL
{
    internal static class Program
    {
        private static readonly JsonSerializerOptions Json =
            new JsonSerializerOptions { WriteIndented = true,
                IncludeFields = true };

        private static int Main(string[] args)
        {
            try
            {
                string command = args.Length == 0 ? "schema" :
                    args[0].ToLowerInvariant();
                switch (command)
                {
                    case "schema": Schema(); break;
                    case "reset-smoke": ResetSmoke(Seed(args)); break;
                    case "step-smoke": StepSmoke(Seed(args)); break;
                    case "replay-smoke": ReplaySmoke(Seed(args)); break;
                    case "benchmark": Benchmark(Seed(args),
                        Integer(args, "--steps", 2)); break;
                    default: Usage(); return 2;
                }
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                return 1;
            }
        }

        private static void Schema()
        {
            ElderObservationSchema.Validate();
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                observationSchemaVersion = ElderRLContract.ObservationSchemaVersion,
                actionSchemaVersion = ElderRLContract.ActionSchemaVersion,
                environmentSchemaVersion = ElderRLContract.EnvironmentSchemaVersion,
                criticSidecarSchemaVersion = ElderRLContract.CriticSidecarSchemaVersion,
                institutionalMemoryStorageSchemaVersion =
                    ElderInstitutionMemory.StorageSchemaVersion,
                simulatorVersion = NativeElderEnvironment.SimulatorVersion,
                buildHash = NativeElderEnvironment.BuildHash,
                tensorFeatureCount = ElderRLContract.TensorFeatureCount,
                bounds = new
                {
                    localSites = ElderRLContract.MaxLocalSites,
                    fishingGrounds = ElderRLContract.MaxFishingGrounds,
                    herdGroups = ElderRLContract.MaxHerdGroups,
                    commodities = ElderRLContract.MaxCommoditySlots,
                    contacts = ElderRLContract.MaxKnownContacts
                },
                registry = ElderObservationSchema.Registry,
                rowKeys = new
                {
                    capabilities = ElderObservationSchema.CapabilityRows,
                    commodities = ElderObservationSchema.CommodityRows,
                    memory = ElderObservationSchema.MemoryRows,
                    memoryTimeConstantsYears =
                        ElderInstitutionMemory.TimeConstantsYears
                },
                defaultEnvironment = new ElderEnvironmentOptions(),
                defaultDirective = new ElderDirective()
            }, Json));
        }

        private static void ResetSmoke(int seed)
        {
            var fixture = Fixture(seed); var camp = fixture.Camps[0];
            var environment = new NativeElderEnvironment();
            var reset = environment.Reset(Request(fixture, camp.Id, 1));
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                status = "PASS", command = "reset-smoke", seed,
                reset.ObservationSchemaVersion, reset.ActionSchemaVersion,
                reset.EnvironmentSchemaVersion, reset.SimulatorVersion,
                reset.BuildHash, reset.ConfigurationHash,
                reset.InitialStateFingerprint, reset.ControlledCampId,
                reset.ControlledPlayer,
                trainingCriticSidecar = reset.TrainingCriticSidecar,
                observation = Summary(reset.Observation)
            }, Json));
        }

        private static void StepSmoke(int seed)
        {
            var fixture = Fixture(seed); var camp = fixture.Camps[0];
            var environment = new NativeElderEnvironment();
            environment.Reset(Request(fixture, camp.Id, 2));
            var result = environment.Step(SmokeAction());
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                status = "PASS", command = "step-smoke", seed,
                result.ScalarReward, result.ElapsedYears,
                result.Terminated, result.Truncated,
                result.StateFingerprint,
                reward = result.Reward,
                requested = result.Decision.Requested,
                accepted = result.Decision.Accepted,
                realized = result.Decision.Realized,
                interventions = result.Decision.Interventions,
                trainingCriticSidecar = result.TrainingCriticSidecar,
                observation = Summary(result.Observation)
            }, Json));
        }

        private static void ReplaySmoke(int seed)
        {
            var fixture = Fixture(seed); var camp = fixture.Camps[0];
            var environment = new NativeElderEnvironment();
            environment.Reset(Request(fixture, camp.Id, 3));
            ElderStepResult last = null;
            for (int step = 0; step < 3; step++)
            {
                var action = SmokeAction();
                action.DevelopmentBudget = .2 + .2 * step;
                action.FieldDevelopmentShare = .25 + .25 * step;
                last = environment.Step(action);
            }
            NativeElderEnvironment.VerifyReplay(environment.ReplayLog,
                new PhaseExecution(4, 2, true, 1));
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                status = "PASS", command = "replay-smoke", seed,
                steps = environment.ReplayLog.Steps.Count,
                initial = environment.ReplayLog.InitialStateFingerprint,
                final = last.StateFingerprint,
                buildHash = environment.ReplayLog.BuildHash,
                configurationHash = environment.ReplayLog.ConfigurationHash
            }, Json));
        }

        private static void Benchmark(int seed, int steps)
        {
            if (steps < 1 || steps > 100)
                throw new ArgumentOutOfRangeException(nameof(steps));
            var fixture = Fixture(seed); int camp = fixture.Camps[0].Id;
            byte[] snapshot = SaveCodec.Save(fixture);
            var results = new List<object>();
            foreach (int workers in new[] { 1, 4, 8, 16, 32 })
            {
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                long before = GC.GetTotalMemory(true);
                var timer = Stopwatch.StartNew();
                Parallel.For(0, workers, new ParallelOptions
                {
                    MaxDegreeOfParallelism = workers
                }, _ =>
                {
                    var environment = new NativeElderEnvironment(execution:
                        new PhaseExecution());
                    environment.Reset(Request(snapshot, camp, steps));
                    for (int step = 0; step < steps; step++)
                        environment.Step(SmokeAction());
                });
                timer.Stop();
                long after = GC.GetTotalMemory(false);
                double environmentSteps = workers * steps;
                results.Add(new
                {
                    workers, stepsPerEnvironment = steps,
                    elapsedMilliseconds = timer.Elapsed.TotalMilliseconds,
                    environmentStepsPerSecond = environmentSteps /
                        Math.Max(1e-9, timer.Elapsed.TotalSeconds),
                    retainedMegabytes = Math.Max(0, after - before) / 1048576.0
                });
            }
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                status = "PASS", command = "benchmark", seed, results
            }, Json));
        }

        private static ElderDirective SmokeAction() => new ElderDirective
        {
            RetainedFieldOperation = .75,
            RetainedHerdFraction = .8,
            DevelopmentBudget = .4,
            FieldDevelopmentShare = .6,
            MovementCandidate = -1
        };

        private static object Summary(ElderObservation observation)
            => observation == null ? null : new
            {
                observation.SchemaVersion,
                population = observation.Core.Population,
                connectedConsumers = observation.Core.ConnectedConsumers,
                previousIntervalYears = observation.Core.PreviousIntervalYears,
                nextIntervalYears = observation.Core.NextIntervalYears,
                qol = observation.Core.QoL,
                fieldIntensity = observation.Fields.CurrentIntensity,
                memoryPresent = observation.Memory.Present,
                memoryMaturity = observation.Memory.Maturity,
                herdAnimals = observation.Herd.Animals,
                sites = observation.Sites.Length,
                fishing = observation.Fishing.Length,
                herdGroups = observation.HerdGroups.Length,
                commodities = observation.Commodities.Length,
                contacts = observation.Contacts.Length,
                movement = observation.Mask.Movement,
                legalMovement = observation.Mask.LegalMovementCandidates
            };

        private static ElderResetRequest Request(WorldState fixture, int camp,
            int limit) => Request(SaveCodec.Save(fixture), camp, limit);

        private static ElderResetRequest Request(byte[] snapshot, int camp,
            int limit) => new ElderResetRequest
        {
            Snapshot = snapshot,
            ControlledCampId = camp,
            Options = new ElderEnvironmentOptions
            {
                EpisodeDecisionLimit = limit,
                MovementEnabled = false,
                IncludeTrainingCriticSidecar = true
            },
            ExpectedSimulatorVersion = NativeElderEnvironment.SimulatorVersion
        };

        private static WorldState Fixture(int seed)
        {
            var config = SimulationConfig.Small(1);
            config.Width = 20; config.Height = 14;
            config.EconomicAllocation = EconomicAllocationStrategy.DemandControl;
            config.AnnualHumanGrowthRate = 0;
            config.FissionEnabled = config.ImmigrationEnabled =
                config.TradeEnabled = false;
            var world = new WorldState
            {
                Seed = seed, Config = config,
                Generation = new GenerationReport(),
                Tiles = new Tile[config.Width * config.Height],
                PlayerCount = 1, Turn = 4, Days = 1000
            };
            for (int i = 0; i < world.Tiles.Length; i++)
            {
                var tile = new Tile
                {
                    Id = i, ElevationLevel = 1,
                    Terrain = Terrain.Grassland,
                    FreshwaterSource = FreshwaterSource.RainPool,
                    Freshwater = 20000, Food = 1200,
                    PotentialFood = 12000,
                    LastEcologyUpdateDays = world.Days
                };
                for (int season = 0; season < 4; season++)
                    tile.Seasons[season] = new SeasonData
                    {
                        Temperature = 18, Moisture = .55f,
                        RegionalMoisture = .5f, MaxFood = 1200,
                        MaxPotentialFood = 12000,
                        MaxFreshwater = 20000,
                        FoodRecoveryPerDay = .004f,
                        BiomassRecoveryPerDay = .004f,
                        WaterRecoveryPerDay = .004f, Habitat = .6f
                    };
                world.Tiles[i] = tile;
            }
            var camp = new Camp
            {
                Id = world.NextId++, Player = 0,
                Tile = world.Map.At(7, 7), Population = 120,
                PlantFood = 6000, AnimalFood = 1000,
                RudimentaryExpectation = .5, StationaryYears = 80,
                StationaryTurns = 4, IsVillage = true,
                EconomyAutomationTurns = -1
            };
            camp.Inventory.HandTools = 240;
            camp.Inventory.FarmTools = 100;
            camp.Inventory.HerdingTools = 20;
            camp.Inventory.Wood = camp.Inventory.Stone =
                camp.Inventory.Clay = 1000;
            world.Camps.Add(camp); Ownership.Bind(world);
            camp.Technology = Technology.Cultivation;
            camp.Technologies[(int)Technology.Cultivation] = true;
            camp.Technologies[(int)Technology.AnimalHusbandry] = true;
            camp.People.Progression.Era = Era.Neolithic;
            camp.People.NeolithicTechnologies.Add(NeolithicTech.Construction);
            foreach (int tile in world.Map.Radius(camp.Tile, 1))
            {
                world.Tiles[tile].CultivationOperator = camp.Id;
                world.Tiles[tile].CultivationCapital = .16;
                world.Tiles[tile].CultivationOperatingFraction = 1;
            }
            int farmers = Math.Min(camp.Population,
                (int)Math.Ceiling(CultivationEconomy
                    .MaintenanceFarmersForPlanning(world, camp,
                        config.TurnDays / config.DaysPerYear) - 1e-9));
            camp.Workforce.Assign(WorkforceRoles.Farmer, farmers,
                camp.Population);
            NeolithicProduction.AddFixedHousing(world, camp, 30, world.Days);
            world.Herds.Add(new Herd
            {
                Id = world.NextId++, Tile = camp.Tile,
                DomesticCamp = camp.Id, OwnerSociety = camp.SocietyId,
                Animals = 24, Capacity = 1000
            });
            return world;
        }

        private static int Seed(string[] args)
            => Integer(args, "--seed", 9301);

        private static int Integer(string[] args, string name, int fallback)
        {
            string prefix = name + "=";
            var value = args.FirstOrDefault(arg => arg.StartsWith(prefix,
                StringComparison.OrdinalIgnoreCase));
            return value == null ? fallback : int.Parse(value.Substring(
                prefix.Length), System.Globalization.CultureInfo.InvariantCulture);
        }

        private static void Usage()
        {
            Console.Error.WriteLine("ElderRL commands:");
            Console.Error.WriteLine("  schema");
            Console.Error.WriteLine("  reset-smoke [--seed=N]");
            Console.Error.WriteLine("  step-smoke [--seed=N]");
            Console.Error.WriteLine("  replay-smoke [--seed=N]");
            Console.Error.WriteLine("  benchmark [--seed=N] [--steps=N]");
        }
    }
}
