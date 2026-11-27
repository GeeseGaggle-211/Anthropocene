using System;
using System.Collections.Generic;
using System.Linq;
using Anthropocene.Simulation;

namespace Anthropocene.CivicGovernanceE2E
{
    internal static class WorldFixtures
    {
        public static SimulationConfig BaseConfig(
            int width = 20,
            int height = 14,
            int players = 2,
            bool civicEnabled = true,
            bool sanitationEnabled = false,
            bool smoothCalendar = false,
            bool agriculture = true,
            bool fission = true,
            bool immigration = true,
            bool trade = true,
            bool fishing = false,
            double landBiomassMultiplier = 63.0)
        {
            var cfg = ConfigLoader.LoadProfile("CivicGovernanceOpeningConfig.json");
            cfg.Width = width;
            cfg.Height = height;
            cfg.Players = players;
            cfg.StartSeparation = Math.Min(cfg.StartSeparation, Math.Max(3, Math.Min(width, height) / 3));
            cfg.FissionEnabled = fission;
            cfg.ImmigrationEnabled = immigration;
            cfg.TradeEnabled = trade;
            cfg.NeolithicFishingEnabled = fishing;
            if (fishing) cfg.CorrectedAquaticSeasonalPressureEnabled = true;
            cfg.LandBiomassMultiplier = landBiomassMultiplier;
            if (!smoothCalendar)
            {
                cfg.SmoothCalendar = null;
                cfg.TurnDays = 20.0 * cfg.DaysPerYear;
            }
            if (!agriculture)
                cfg.Agriculture = null;
            if (!civicEnabled)
            {
                cfg.Civic = null;
            }
            else
            {
                cfg.Civic.Enabled = true;
                cfg.Civic.SanitationEnabled = sanitationEnabled;
            }
            cfg.Validate();
            return cfg;
        }

        public static Tile MakeFertileTile(
            int id,
            SimulationConfig cfg,
            float moisture = 0.65f,
            float temperature = 18.0f,
            FreshwaterSource waterSource = FreshwaterSource.RainPool,
            float maxFreshwater = 25.0f,
            float elevation = 0.25f,
            EnvironmentKind env = EnvironmentKind.Temperate,
            VegetationTier veg = VegetationTier.Medium,
            int elevationLevel = 1,
            float seasonalMoistureAmp = 0.0f)
        {
            var tile = new Tile
            {
                Id = id,
                Environment = env,
                NaturalVegetationPotential = veg,
                CurrentVegetation = veg,
                ElevationLevel = elevationLevel,
                Elevation = elevation,
                WaterBody = WaterKind.None,
                FreshwaterSource = waterSource,
                LandCondition = 1.0,
                AquaticCondition = 1.0
            };

            float foodRate = (float)EcologyMath.DailyRate(cfg.LandFoodRegeneration, cfg.BaseDays);
            float waterRate = (float)EcologyMath.DailyRate(
                waterSource == FreshwaterSource.Oasis ? cfg.OasisWaterRecovery : cfg.WaterRecovery,
                cfg.BaseDays);

            for (int s = 0; s < 4; s++)
            {
                float mOffset = seasonalMoistureAmp == 0f ? 0f : (s == 1 || s == 2 ? seasonalMoistureAmp * 0.5f : -seasonalMoistureAmp * 0.5f);
                float m = (float)Numbers.Clamp(moisture + mOffset, 0.02, 0.98);
                float fw = waterSource == FreshwaterSource.None ? 0f : maxFreshwater;
                tile.Seasons[s] = new SeasonData
                {
                    Temperature = temperature,
                    Moisture = m,
                    RegionalMoisture = m,
                    MaxFreshwater = fw,
                    FoodRecoveryPerDay = foodRate,
                    BiomassRecoveryPerDay = foodRate,
                    WaterRecoveryPerDay = waterSource == FreshwaterSource.None ? 0f : waterRate,
                    Habitat = 0.65f
                };
            }
            FoodEcology.ConfigurePotential(tile, cfg, initializeStock: true);
            tile.Food = tile.Seasons[0].MaxFood;
            tile.PotentialFood = tile.Seasons[0].MaxPotentialFood;
            tile.Freshwater = waterSource == FreshwaterSource.None ? 0.0 : tile.Seasons[0].MaxFreshwater;
            return tile;
        }

        public static Tile MakeAridTile(int id, SimulationConfig cfg, float elevation = 0.25f, int elevationLevel = 1, float moisture = 0.06f)
        {
            return MakeFertileTile(
                id,
                cfg,
                moisture: moisture,
                temperature: 26.0f,
                waterSource: FreshwaterSource.None,
                maxFreshwater: 0.0f,
                elevation: elevation,
                env: EnvironmentKind.Desert,
                veg: VegetationTier.Low,
                elevationLevel: elevationLevel);
        }

        public static Tile MakeOasisTile(int id, SimulationConfig cfg, float elevation = 0.25f)
        {
            var tile = MakeFertileTile(
                id,
                cfg,
                moisture: 0.55f,
                temperature: 25.0f,
                waterSource: FreshwaterSource.Oasis,
                maxFreshwater: (float)cfg.OasisFreshwater,
                elevation: elevation,
                env: EnvironmentKind.Desert,
                veg: VegetationTier.Medium,
                elevationLevel: 1);
            tile.Oasis = true;
            tile.OasisInfluence = 1.0f;
            FoodEcology.ConfigurePotential(tile, cfg, initializeStock: true);
            tile.Food = tile.Seasons[0].MaxFood;
            tile.PotentialFood = tile.Seasons[0].MaxPotentialFood;
            tile.Freshwater = tile.Seasons[0].MaxFreshwater;
            return tile;
        }

        public static Tile MakeCoastalShallowsTile(int id, SimulationConfig cfg)
        {
            var tile = new Tile
            {
                Id = id,
                Environment = EnvironmentKind.Shallows,
                NaturalVegetationPotential = VegetationTier.Low,
                CurrentVegetation = VegetationTier.Low,
                ElevationLevel = 0,
                Elevation = -0.05f,
                WaterBody = WaterKind.Saltwater,
                FreshwaterSource = FreshwaterSource.None,
                LandCondition = 1.0,
                AquaticCondition = 1.0
            };
            float rate = (float)EcologyMath.DailyRate(cfg.FoodRegeneration, cfg.BaseDays);
            for (int s = 0; s < 4; s++)
            {
                tile.Seasons[s] = new SeasonData
                {
                    Temperature = 16.0f,
                    Moisture = 1.0f,
                    RegionalMoisture = 0.7f,
                    MaxFood = 300.0f,
                    MaxPotentialFood = 600.0f,
                    MaxFreshwater = 0.0f,
                    MaxSaltwater = 100.0f,
                    FoodRecoveryPerDay = rate,
                    BiomassRecoveryPerDay = rate,
                    WaterRecoveryPerDay = 0.0f,
                    Habitat = 0.7f
                };
            }
            tile.Food = tile.Seasons[0].MaxFood;
            tile.PotentialFood = tile.Seasons[0].MaxPotentialFood;
            tile.Freshwater = 0.0;
            tile.Saltwater = tile.Seasons[0].MaxSaltwater;
            return tile;
        }

        public static WorldState CreateEmptyWorld(SimulationConfig cfg, int seed = 101)
        {
            var w = new WorldState
            {
                Seed = seed,
                Config = cfg,
                Generation = new GenerationReport(),
                Tiles = new Tile[cfg.Width * cfg.Height],
                PlayerCount = cfg.Players,
                Canals = new List<CanalSegment>(),
                IrrigatedFields = new List<IrrigatedField>()
            };
            return w;
        }

        public static void AddSurfaceResources(
            WorldState w,
            IEnumerable<int> tiles,
            ushort stone = 4000,
            ushort clay = 4000,
            ushort wood = 4000,
            int copperDepositTile = -1)
        {
            int count = w.Tiles.Length;
            var res = new WorldResourceState
            {
                Version = 3,
                GenerationVersion = 2,
                CoreGeneratorVersion = WorldState.GeneratorVersion,
                MaximumQuantity = 100000,
                DeepReserveMultiplier = 0.0,
                DeepMiningTechnology = "",
                WoodRecoveryPerYear = 0.05,
                CatalogHash = ResourceCatalog.Hash,
                ProfileHash = ResourceCatalog.Hash,
                ConfigurationHash = ResourceCatalog.Hash,
                Provinces = Array.Empty<GeologicalProvince>(),
                Province = new ushort[count],
                History = new ushort[count],
                Host = new byte[count],
                Marine = new byte[count],
                Stone = new ushort[count],
                Clay = new ushort[count],
                Wood = new ushort[count],
                Bodies = Array.Empty<DepositBody>()
            };

            foreach (int t in tiles)
            {
                if (t < 0 || t >= count || !w.Tiles[t].Land) continue;
                res.Stone[t] = stone;
                res.Clay[t] = clay;
                res.Wood[t] = wood;
            }

            if (copperDepositTile >= 0 && copperDepositTile < count && w.Tiles[copperDepositTile].Land)
            {
                var profile = GeologicalProfiles.All.First();
                var comp = new DepositComponent(MaterialId.Copper, 10000);
                var site = new DepositSite
                {
                    Tile = copperDepositTile,
                    Share = 10000,
                    Exposure = 10000,
                    Burial = BurialDepth.Surface,
                    Setting = MarineSetting.Land
                };
                res.Bodies = new[]
                {
                    new DepositBody
                    {
                        Id = 1,
                        Profile = profile.Id,
                        OriginTile = copperDepositTile,
                        InitialQuantity = 50000,
                        Quantity = 50000,
                        SiteExposureWeightTotal = 10000.0 * 10000.0,
                        Shape = DepositShape.Vein,
                        Concentration = MineralConcentration.Deposit,
                        Hardness = 50,
                        Quality = 85,
                        Components = new[] { comp },
                        Sites = new[] { site },
                        TransportPath = Array.Empty<int>()
                    }
                };
            }

            w.Resources = res;
            ResourceValidation.Validate(w);
        }

        public static Camp AddVillage(
            WorldState w,
            int player,
            int tile,
            int population,
            bool isVillage = true,
            bool grantBaseTechs = true,
            bool grantPlough = true,
            bool grantIrrigation = false,
            bool grantPottery = true,
            double housingCoverage = 1.0,
            double clothPerCapita = 2.0,
            double housingMaterial = 2000.0,
            double tools = 2000.0,
            double farmTools = 200.0,
            double pots = 0.0,
            double foodYears = 15.0,
            double stationaryYears = 80.0,
            int stationaryTurns = 4,
            bool autoEconomy = true,
            params string[] precedents)
        {
            double foodPerYear = PhysicalDemography.FoodPerPersonYear(w.Config);
            var camp = new Camp
            {
                Id = w.NextId++,
                Player = player,
                Tile = tile,
                Population = population,
                IsVillage = isVillage,
                StationaryYears = stationaryYears,
                StationaryTurns = stationaryTurns,
                VillageTurns = isVillage ? Math.Max(1, stationaryTurns) : 0,
                EconomyAutomationTurns = autoEconomy ? -1 : 0,
                PlantFood = population * foodPerYear * foodYears,
                RudimentaryExpectation = 1.0,
                EconomicLabor = new EconomicLaborPlan()
            };
            camp.Inventory.Cloth = population * clothPerCapita;
            camp.Inventory.HousingMaterial = housingMaterial;
            camp.Inventory.Tools = tools;
            camp.Inventory.FarmTools = farmTools;
            camp.Inventory.Pots = pots;
            camp.Inventory.Stone = 500.0;
            camp.Inventory.Clay = 500.0;
            camp.Inventory.Wood = 500.0;

            w.Camps.Add(camp);
            w.PlayerCount = Math.Max(w.PlayerCount, player + 1);
            w.Config.Players = Math.Max(w.Config.Players, player + 1);
            Ownership.Bind(w);

            camp.People.Progression.Era = Era.Neolithic;
            camp.Technology = Technology.Cultivation;
            camp.Technologies[(int)Technology.Cultivation] = true;
            camp.People.TransitionTurn = 1;

            if (grantBaseTechs)
            {
                AddTech(camp.People, NeolithicTech.Toolmaking);
                AddTech(camp.People, NeolithicTech.Weaving);
                AddTech(camp.People, NeolithicTech.Carpentry);
                AddTech(camp.People, NeolithicTech.Construction);
            }
            if (grantPottery) AddTech(camp.People, NeolithicTech.Pottery);
            if (grantPlough) AddTech(camp.People, NeolithicTech.Plough);
            if (grantIrrigation) AddTech(camp.People, NeolithicTech.Irrigation);

            if (precedents != null)
            {
                foreach (string p in precedents)
                {
                    if (!string.IsNullOrEmpty(p) && !camp.People.HasCivicPrecedent(p))
                        camp.People.CivicPrecedents.Add(p);
                }
            }

            if (housingCoverage > 0 && population > 0)
            {
                double units = Math.Ceiling(population * housingCoverage / w.Config.Neolithic.HousingPeoplePerUnit);
                NeolithicProduction.AddFixedHousing(w, camp, units, w.Days);
            }

            if (w.Config.FissionEnabled)
                ExploreRadius(w, camp, w.Config.FissionRadius + 2);

            return camp;
        }

        public static void ExploreRadius(WorldState w, Camp camp, int radius)
        {
            if (w == null || camp == null || camp.People == null) return;
            var mem = ContactKnowledge.MemoryOf(w, camp.People);
            foreach (int t in w.Map.Radius(camp.Tile, radius))
                mem.Explored.Add(t, w.Turn);
        }

        public static void AddTech(Society s, string tech)
        {
            if (s.NeolithicTechnologies == null) s.NeolithicTechnologies = new List<string>();
            if (!s.NeolithicTechnologies.Contains(tech)) s.NeolithicTechnologies.Add(tech);
        }

        public static void CultivateFootprint(WorldState w, Camp camp, double capital = 1.0, int maxTiles = 7)
        {
            int done = 0;
            double maxCap = 1.0 / (w.Config.ReferenceIntensity * w.Config.ReferenceIntensity);
            double clamped = Numbers.Clamp(capital, 0.0, maxCap);
            foreach (int id in CultivationEconomy.Footprint(w, camp))
            {
                if (done >= maxTiles) break;
                var t = w.Tiles[id];
                t.Cultivated = true;
                t.Cultivation = (float)CultivationEconomy.Intensity(w.Config, clamped);
                t.CultivationCapital = clamped;
                t.CultivationOperator = camp.Id;
                // Keep 0.0 to match SaveCodec.Load default (CultivationOperatingFraction is not serialized),
                // preserving bit-identical CloneWorld / SaveCodec parity.
                t.CultivationOperatingFraction = 0.0;
                RefreshTileStandingFood(w, id);
                done++;
            }
        }

        public static void RefreshTileStandingFood(WorldState w, int tileId)
        {
            var t = w.Tiles[tileId];
            var view = LazyEcology.ProjectEcology(w, tileId, w.Days);
            var cap = new EcologyCapacity(w, t, view, w.Season);
            t.PotentialFood = cap.Biomass;
            t.Food = Math.Min(cap.Food, t.PotentialFood);
        }

        public static void RefreshFootprintStandingFood(WorldState w, Camp camp)
        {
            foreach (int id in CultivationEconomy.Footprint(w, camp))
            {
                if (w.Tiles[id].CultivationOperator == camp.Id)
                    RefreshTileStandingFood(w, id);
            }
        }

        public static void SlashHomeFootprint(WorldState w, Camp camp, float keepFraction)
        {
            foreach (int t in w.Map.Radius(camp.Tile, 1))
            {
                for (int s = 0; s < 4; s++)
                {
                    var sd = w.Tiles[t].Seasons[s];
                    sd.MaxFood *= keepFraction;
                    sd.MaxPotentialFood *= keepFraction;
                    w.Tiles[t].Seasons[s] = sd;
                }
                w.Tiles[t].Food = Math.Min(w.Tiles[t].Food * keepFraction, w.Tiles[t].Seasons[0].MaxPotentialFood);
                w.Tiles[t].PotentialFood = w.Tiles[t].Seasons[0].MaxPotentialFood;
            }
            ValidateWorld(w);
        }

        public static void ClearFootprintCultivation(WorldState w, Camp camp)
        {
            foreach (int id in CultivationEconomy.Footprint(w, camp))
            {
                var t = w.Tiles[id];
                t.Cultivated = false;
                t.Cultivation = 0.0f;
                t.CultivationCapital = 0.0;
                t.CultivationOperator = 0;
                t.CultivationOperatingFraction = 0.0;
                RefreshTileStandingFood(w, id);
            }
            camp.Workforce.Release(WorkforceRoles.Farmer, CultivationEconomy.Workers(camp, WorkforceRoles.Farmer), camp.Population);
            ValidateWorld(w);
        }

        public static void EstablishContacts(WorldState w, Camp a, Camp b)
        {
            if (a == null || b == null || a.SocietyId == b.SocietyId) return;
            ContactKnowledge.RecordTrade(w, a.People, b);
            ContactKnowledge.RecordTrade(w, b.People, a);
        }

        public static void EstablishAllContacts(WorldState w)
        {
            for (int i = 0; i < w.Camps.Count; i++)
                for (int j = i + 1; j < w.Camps.Count; j++)
                    EstablishContacts(w, w.Camps[i], w.Camps[j]);
        }

        public static WorldState CreatePlainsWorld(
            int population = 1000,
            bool civicEnabled = true,
            bool sanitationEnabled = false,
            bool smoothCalendar = false,
            bool fission = true,
            bool immigration = true,
            bool trade = false,
            bool grantPlough = true,
            bool withResources = true,
            int width = 20,
            int height = 14,
            double landBiomassMultiplier = 63.0,
            params string[] precedents)
        {
            var cfg = BaseConfig(
                width: width,
                height: height,
                players: 1,
                civicEnabled: civicEnabled,
                sanitationEnabled: sanitationEnabled,
                smoothCalendar: smoothCalendar,
                agriculture: true,
                fission: fission,
                immigration: immigration,
                trade: trade,
                landBiomassMultiplier: landBiomassMultiplier);

            var w = CreateEmptyWorld(cfg, seed: 101);
            for (int i = 0; i < w.Tiles.Length; i++)
                w.Tiles[i] = MakeFertileTile(i, cfg, moisture: 0.65f, temperature: 18.0f, waterSource: FreshwaterSource.RainPool, maxFreshwater: 25.0f);

            int center = w.Map.At(width / 2, height / 2);
            var camp = AddVillage(w, 0, center, population,
                grantPlough: grantPlough,
                grantIrrigation: false,
                foodYears: 12.0,
                precedents: precedents);

            CultivateFootprint(w, camp, capital: 0.9);
            if (withResources)
                AddSurfaceResources(w, w.Map.Radius(center, 2), stone: 4000, clay: 4000, wood: 4000);

            ValidateWorld(w);
            return w;
        }

        public static WorldState CreateValleyWorld(
            int population = 1000,
            bool civicEnabled = true,
            bool sanitationEnabled = false,
            bool smoothCalendar = false,
            bool fission = true,
            bool immigration = true,
            bool trade = false,
            bool withResources = true,
            bool withCopperDeposit = true,
            int width = 20,
            int height = 14,
            double riverMaxFreshwater = 150.0,
            double landBiomassMultiplier = 63.0,
            bool withViableExitPocket = false,
            params string[] precedents)
        {
            var cfg = BaseConfig(
                width: width,
                height: height,
                players: 1,
                civicEnabled: civicEnabled,
                sanitationEnabled: sanitationEnabled,
                smoothCalendar: smoothCalendar,
                agriculture: true,
                fission: fission,
                immigration: immigration,
                trade: trade,
                landBiomassMultiplier: landBiomassMultiplier);

            var w = CreateEmptyWorld(cfg, seed: 202);
            int center = w.Map.At(width / 2, height / 2);

            // Fill entire map with arid non-viable desert to create physical circumscription (D2 high).
            for (int i = 0; i < w.Tiles.Length; i++)
                w.Tiles[i] = MakeAridTile(i, cfg, elevation: 0.25f, elevationLevel: 1, moisture: 0.05f);

            // Radius-1 river valley: 2 river tiles + center + 4 dry alluvial terrace tiles.
            var neighbors = w.Map.Neighbors(center).ToArray();
            Assert.Check(neighbors.Length == 6, "Valley center must have 6 hex neighbors.");

            int river1 = neighbors[0];
            int river2 = neighbors[3];

            // Semi-arid alluvial terraces: zero dry-season 0.02 clipping, strictly < TargetMoisture=0.50 in all 4 seasons.
            w.Tiles[center] = MakeFertileTile(center, cfg, moisture: 0.34f, temperature: 22.0f,
                waterSource: FreshwaterSource.None, maxFreshwater: 0.0f, elevation: 0.20f,
                env: EnvironmentKind.Grassland, veg: VegetationTier.Low, seasonalMoistureAmp: 0.16f);

            // Two high-flow river source tiles (slightly higher elevation so gravity routes pass RouteValid).
            w.Tiles[river1] = MakeFertileTile(river1, cfg, moisture: 0.55f, temperature: 22.0f,
                waterSource: FreshwaterSource.River, maxFreshwater: (float)riverMaxFreshwater, elevation: 0.25f,
                env: EnvironmentKind.Temperate, veg: VegetationTier.Medium, seasonalMoistureAmp: 0.20f);
            w.Tiles[river2] = MakeFertileTile(river2, cfg, moisture: 0.55f, temperature: 22.0f,
                waterSource: FreshwaterSource.River, maxFreshwater: (float)riverMaxFreshwater, elevation: 0.25f,
                env: EnvironmentKind.Temperate, veg: VegetationTier.Medium, seasonalMoistureAmp: 0.20f);

            // Four dry alluvial terrace tiles adjacent to center and at least one river tile.
            int[] terraceIndices = { neighbors[1], neighbors[2], neighbors[4], neighbors[5] };
            foreach (int tid in terraceIndices)
            {
                w.Tiles[tid] = MakeFertileTile(tid, cfg, moisture: 0.32f, temperature: 22.0f,
                    waterSource: FreshwaterSource.None, maxFreshwater: 0.0f, elevation: 0.20f,
                    env: EnvironmentKind.Grassland, veg: VegetationTier.Low, seasonalMoistureAmp: 0.16f);
            }

            // Optional downstream viable exit pocket at dist >= FissionRadius (preserves valley circumscription while preventing desert-locked vacuity).
            if (withViableExitPocket)
            {
                int exitCenter = w.Map.At(width / 2 + cfg.FissionRadius, height / 2);
                foreach (int et in w.Map.Radius(exitCenter, 1))
                {
                    if (w.Map.Distance(center, et) < cfg.FissionRadius) continue;
                    w.Tiles[et] = MakeFertileTile(et, cfg, moisture: 0.65f, temperature: 18.0f,
                        waterSource: FreshwaterSource.RainPool, maxFreshwater: 25.0f, elevation: 0.20f);
                }
            }

            var camp = AddVillage(w, 0, center, population,
                grantPlough: true,
                grantIrrigation: true,
                grantPottery: true,
                foodYears: 35.0,
                pots: Math.Max(40, population * 0.15),
                precedents: precedents);
            if (population >= 20)
                camp.Workforce.Assign(WorkforceRoles.Farmer, Math.Min((int)(population * 0.80), population - 10), population);

            // Plough-supported initial intensity (q=0.52) avoids the >PloughSupportedIntensity maintenance penalty trap.
            double maxCap = 1.0 / (cfg.ReferenceIntensity * cfg.ReferenceIntensity);
            double ploughCap = Math.Min(maxCap, Math.Pow(cfg.Neolithic.PloughSupportedIntensity / cfg.ReferenceIntensity, 2));
            CultivateFootprint(w, camp, capital: ploughCap);

            // Route center off the non-drinking river; Area=0.18 sustains WaterService >= 0.90 across 5 fields while still throttling under single-river scarcity.
            int drinkRiver = FreshwaterAccess.Source(w, center);
            int otherRiver = drinkRiver == river1 ? river2 : river1;
            var canalTargets = new List<(int source, int field)>();
            if (w.Map.Distance(otherRiver, center) == 1) canalTargets.Add((otherRiver, center));
            foreach (int tid in terraceIndices)
            {
                if (w.Map.Distance(river1, tid) == 1) canalTargets.Add((river1, tid));
                else if (w.Map.Distance(river2, tid) == 1) canalTargets.Add((river2, tid));
            }
            foreach (var pair in canalTargets)
            {
                Assert.Check(IrrigationEconomy.RouteValid(w, pair.source, new[] { pair.field }),
                    $"Valley canal route {pair.source}->{pair.field} must pass RouteValid.");
                var seg = new CanalSegment
                {
                    Id = w.NextId++,
                    Operator = camp.Id,
                    Source = pair.source,
                    From = pair.source,
                    Tile = pair.field,
                    Parent = -1,
                    Construction = 1.0,
                    ActiveDays = 0.0,
                    LastUpdatedDays = w.Days,
                    Throughput = cfg.Agriculture.AnnualThroughput,
                    Condition = 1.0,
                    FundedYears = 60.0,
                    CommissioningYears = 0.0
                };
                w.Canals.Add(seg);
                w.IrrigatedFields.Add(new IrrigatedField
                {
                    Tile = pair.field,
                    Operator = camp.Id,
                    Endpoint = seg.Id,
                    Area = 0.18,
                    Soil = 0.65,
                    LastUpdatedDays = w.Days,
                    WaterService = 1.0
                });
                w.Tiles[pair.field].Irrigated = true;
            }
            w.IrrigatedFields.Sort((a, b) => a.Tile.CompareTo(b.Tile));
            RefreshFootprintStandingFood(w, camp);

            if (withResources)
            {
                AddSurfaceResources(
                    w,
                    w.Map.Radius(center, 1),
                    stone: 6000,
                    clay: 6000,
                    wood: 6000,
                    copperDepositTile: withCopperDeposit ? terraceIndices[0] : -1);
            }

            ValidateWorld(w);
            return w;
        }

        public static WorldState CreateCoastalWorld(
            int population = 800,
            bool civicEnabled = true,
            bool sanitationEnabled = false,
            int width = 20,
            int height = 14)
        {
            var cfg = BaseConfig(
                width: width,
                height: height,
                players: 1,
                civicEnabled: civicEnabled,
                sanitationEnabled: sanitationEnabled,
                fission: false,
                immigration: false,
                trade: false,
                fishing: true);

            var w = CreateEmptyWorld(cfg, seed: 303);
            for (int r = 0; r < height; r++)
            {
                for (int q = 0; q < width; q++)
                {
                    int id = w.Map.At(q, r);
                    if (r < height / 2)
                        w.Tiles[id] = MakeCoastalShallowsTile(id, cfg);
                    else
                        w.Tiles[id] = MakeFertileTile(id, cfg, moisture: 0.60f, waterSource: FreshwaterSource.RainPool, maxFreshwater: 25.0f);
                }
            }

            int center = w.Map.At(width / 2, height / 2);
            var camp = AddVillage(w, 0, center, population, grantPlough: false, grantIrrigation: false, foodYears: 10.0);
            camp.Technology = Technology.Watercraft;
            camp.Technologies[(int)Technology.Watercraft] = true;
            camp.Technologies[(int)Technology.Cultivation] = false;
            camp.PlantFood = 0.0;
            camp.AquaticFood = population * PhysicalDemography.FoodPerPersonYear(cfg) * 8.0;

            ValidateWorld(w);
            return w;
        }

        public static WorldState CreateOasisAndRiverDualWorld(
            int oasisPop = 2200,
            int riverPop = 2200,
            bool sanitationEnabled = true,
            bool agriculture = true,
            int width = 24,
            int height = 16,
            double landBiomassMultiplier = 63.0,
            params string[] precedents)
        {
            var cfg = BaseConfig(
                width: width,
                height: height,
                players: 2,
                civicEnabled: true,
                sanitationEnabled: sanitationEnabled,
                agriculture: agriculture,
                fission: false,
                immigration: false,
                trade: false,
                landBiomassMultiplier: landBiomassMultiplier);

            var w = CreateEmptyWorld(cfg, seed: 404);
            for (int i = 0; i < w.Tiles.Length; i++)
                w.Tiles[i] = MakeAridTile(i, cfg, elevation: 0.25f, moisture: 0.05f);

            int oasisCenter = w.Map.At(5, height / 2);
            w.Tiles[oasisCenter] = MakeOasisTile(oasisCenter, cfg);
            // Make oasis tile and radius-1 cultivable enough to feed residents while keeping 6 neighbors strictly dry!
            foreach (int nb in w.Map.Neighbors(oasisCenter))
            {
                w.Tiles[nb] = MakeFertileTile(nb, cfg, moisture: 0.45f, temperature: 24.0f,
                    waterSource: FreshwaterSource.None, maxFreshwater: 0.0f, elevation: 0.25f,
                    env: EnvironmentKind.Grassland, veg: VegetationTier.Medium);
            }

            int riverCenter = w.Map.At(width - 6, height / 2);
            var rNeighbors = w.Map.Neighbors(riverCenter).ToArray();
            w.Tiles[riverCenter] = MakeFertileTile(riverCenter, cfg, moisture: 0.55f, temperature: 20.0f,
                waterSource: FreshwaterSource.RainPool, maxFreshwater: 25.0f, elevation: 0.20f);
            w.Tiles[rNeighbors[0]] = MakeFertileTile(rNeighbors[0], cfg, moisture: 0.65f, temperature: 20.0f,
                waterSource: FreshwaterSource.River, maxFreshwater: 150.0f, elevation: 0.25f);
            w.Tiles[rNeighbors[3]] = MakeFertileTile(rNeighbors[3], cfg, moisture: 0.65f, temperature: 20.0f,
                waterSource: FreshwaterSource.River, maxFreshwater: 150.0f, elevation: 0.25f);
            foreach (int nb in new[] { rNeighbors[1], rNeighbors[2], rNeighbors[4], rNeighbors[5] })
            {
                w.Tiles[nb] = MakeFertileTile(nb, cfg, moisture: 0.60f, temperature: 20.0f,
                    waterSource: FreshwaterSource.RainPool, maxFreshwater: 25.0f, elevation: 0.20f);
            }

            var oasisCamp = AddVillage(w, 0, oasisCenter, oasisPop,
                grantPlough: true, grantIrrigation: false, housingMaterial: 4000.0, tools: 4000.0, foodYears: 15.0,
                precedents: precedents);
            var riverCamp = AddVillage(w, 1, riverCenter, riverPop,
                grantPlough: true, grantIrrigation: false, housingMaterial: 4000.0, tools: 4000.0, foodYears: 15.0,
                precedents: precedents);

            CultivateFootprint(w, oasisCamp, capital: 2.0);
            CultivateFootprint(w, riverCamp, capital: 2.0);
            AddSurfaceResources(w, w.Map.Radius(oasisCenter, 1).Concat(w.Map.Radius(riverCenter, 1)), stone: 6000, clay: 6000, wood: 6000);

            ValidateWorld(w);
            return w;
        }

        public static WorldState CreateCompactGeneratedWorld(
            int seed = 11,
            bool civicEnabled = true,
            bool sanitationEnabled = true,
            int width = 64,
            int height = 32,
            int players = 6)
        {
            var cfg = ConfigLoader.LoadProfile("CivicGovernanceOpeningConfig.json");
            cfg.Width = width;
            cfg.Height = height;
            cfg.Players = players;
            cfg.PlateCount = 6;
            cfg.StartSeparation = 8;
            if (!civicEnabled)
            {
                cfg.Civic = null;
            }
            else
            {
                cfg.Civic.Enabled = true;
                cfg.Civic.SanitationEnabled = sanitationEnabled;
            }
            cfg.Validate();

            var resOpts = ConfigLoader.LoadResourceOptions();
            var w = new WorldGenerator().Generate(seed, cfg, resOpts);
            ValidateWorld(w);
            return w;
        }

        public static void ValidateWorld(WorldState w)
        {
            Ownership.Bind(w);
            TurnResolver.Invariants(w);
            IrrigationEconomy.Validate(w);
            if (w.Resources != null)
                ResourceValidation.Validate(w);
        }

        public static WorldState CloneWorld(WorldState w)
        {
            Ownership.Bind(w);
            return SaveCodec.Load(SaveCodec.Save(w));
        }

        public static TurnRecord StepTurn(
            WorldState w,
            TurnResolver resolver = null,
            Dictionary<int, List<Order>> orders = null,
            bool validateInvariants = true)
        {
            resolver = resolver ?? new TurnResolver();
            var submitted = orders ?? new Dictionary<int, List<Order>>();
            var prepared = TurnPlanner.Prepare(w, submitted);
            var record = resolver.Resolve(w, prepared);
            if (validateInvariants)
            {
                TurnResolver.Invariants(w);
                IrrigationEconomy.Validate(w);
            }
            return record;
        }

        public static TurnRecord StepWithAi(
            WorldState w,
            TurnResolver resolver = null,
            int excludedPlayer = -1,
            List<Order> excludedPlayerOrders = null,
            bool useDraft = false,
            bool validateInvariants = true)
        {
            resolver = resolver ?? new TurnResolver();
            PlanningDraft draft = null;
            PlanningResult ai;
            if (useDraft)
            {
                draft = TurnPlanner.Draft(w, excludedPlayer);
                ai = draft.Ai;
            }
            else
            {
                ai = AiPlanning.Decide(w, excludedPlayer: excludedPlayer);
            }

            var orders = new Dictionary<int, List<Order>>(ai.Orders);
            if (excludedPlayer >= 0 && excludedPlayerOrders != null)
                orders[excludedPlayer] = excludedPlayerOrders;

            var prepared = useDraft
                ? TurnPlanner.Prepare(w, orders, ai.Traces, null, draft)
                : TurnPlanner.Prepare(w, orders, ai.Traces);
            var record = resolver.Resolve(w, prepared);
            if (validateInvariants)
            {
                TurnResolver.Invariants(w);
                IrrigationEconomy.Validate(w);
            }
            return record;
        }

        public static void SetFixedStepYears(WorldState w, double years)
        {
            w.Config.SmoothCalendar = null;
            w.Config.TurnDays = years * w.Config.DaysPerYear;
        }

        public static void ConfigureSmoothCalendarStarted(WorldState w, double startDays = 0.0)
        {
            w.Config.SmoothCalendar = new SmoothCalendarSettings
            {
                Version = 1,
                Curve = SmoothCalendarCurve.HistoricalExponential,
                NeolithicStartYear = -5000.0,
                Started = true,
                EpochDays = 0.0
            };
            w.Days = startDays;
            w.Config.TurnDays = SmoothCalendar.ExpectedDays(w);
            w.Config.Validate();
            SmoothCalendar.RequireCurrentBoundary(w);
        }

        public static void AssignExactStewards(Camp c, int stewards)
        {
            int cur = CultivationEconomy.Workers(c, WorkforceRoles.Steward);
            if (stewards > cur)
            {
                int need = stewards - cur;
                int free = c.Workforce.Gatherers(c.Population);
                if (free < need)
                {
                    foreach (var a in c.Workforce.Assignments.Where(x => x.Role != WorkforceRoles.Steward).ToList())
                    {
                        int take = Math.Min(a.Population, need - free);
                        if (take > 0)
                        {
                            c.Workforce.Release(a.Role, take, c.Population);
                            free += take;
                        }
                        if (free >= need) break;
                    }
                }
                c.Workforce.Assign(WorkforceRoles.Steward, Math.Min(need, c.Workforce.Gatherers(c.Population)), c.Population);
            }
            else if (stewards < cur)
            {
                c.Workforce.Release(WorkforceRoles.Steward, cur - stewards, c.Population);
            }
        }
    }
}
