from pathlib import Path

def edit(path, replacements):
    p=Path(path); text=p.read_text()
    for before,after in replacements:
        if text.count(before)!=1:
            raise RuntimeError((path,before[:100],text.count(before)))
        text=text.replace(before,after)
    p.write_text(text)

edit('Assets/_Anthropocene/Simulation/TurnPlanning.cs',[
('public int Camp, Tile;\n        public bool Abandoning;', 'public int Camp, Tile, Residents;\n        public bool Abandoning;'),
('prepared.Horizon, useDraft ? draft.Intents : null);','prepared.Horizon, useDraft ? draft.Intents : null, acceptedActions);'),
('int effectiveTile = prepared.Placement.Destinations[camp.Id];\n                bool abandoning', 'int effectiveTile = prepared.Placement.Destinations[camp.Id];\n                int residents = ResidentCountAfterActions(camp, acceptedActions);\n                bool abandoning'),
('cached.Tile == effectiveTile && cached.Abandoning == abandoning)', 'cached.Tile == effectiveTile && cached.Abandoning == abandoning && cached.Residents == residents)'),
('return AutomaticIntent(world, camp, effectiveTile, abandoning,\n                    index', 'return AutomaticIntent(world, camp, effectiveTile, abandoning, residents,\n                    index'),
('private static AutomaticPlan AutomaticIntent(WorldState world, Camp camp, int effectiveTile, bool abandoning,','private static AutomaticPlan AutomaticIntent(WorldState world, Camp camp, int effectiveTile, bool abandoning, int residents,'),
('var planningCamp = PlanningCampAt(camp, effectiveTile, trade);\n            var settings', '''var planningCamp = PlanningCampAt(camp, effectiveTile, trade);
            if (residents != camp.Population)
            {
                // Dispatch reserves residents before automatic staffing. Its people
                // remain connected food consumers, but cannot work at the camp.
                planningCamp = planningCamp.WithPrivateInventory();
                planningCamp.Population = residents;
                planningCamp.PendingDispatchConsumers = camp.Population - residents;
            }
            var settings'''),
('Population = camp.Population, Technologies = camp.People.NeolithicTechnologies.ToArray(),','Population = residents, Technologies = camp.People.NeolithicTechnologies.ToArray(),'),
('private static Dictionary<int, FishingAccessPlan> BuildFishingAccessPlans(WorldState world,','''private static int ResidentCountAfterActions(Camp camp, IReadOnlyDictionary<int, Order> actions)
        {
            if (actions != null && actions.TryGetValue(camp.Id, out var action) && action.Kind == OrderKind.Dispatch)
                return Math.Max(0, camp.Population - action.Population);
            return camp.Population;
        }

        private static Dictionary<int, FishingAccessPlan> BuildFishingAccessPlans(WorldState world,'''),
('IReadOnlyDictionary<int, DraftIntent> reusable = null)','IReadOnlyDictionary<int, DraftIntent> reusable = null,\n            IReadOnlyDictionary<int, Order> actions = null)'),
('int residents = Math.Max(0, camp.Population);\n                FishingAccessPlan access', 'int residents = ResidentCountAfterActions(camp, actions);\n                FishingAccessPlan access'),
('var fishingAccess = BuildFishingAccessPlans(world, world.Camps, placement, TurnHorizon.Next(world));','''var draftActions = orders.Where(o => o.Kind == OrderKind.Dispatch)
                .GroupBy(o => o.Entity).ToDictionary(g => g.Key, g => g.First());
            var fishingAccess = BuildFishingAccessPlans(world, world.Camps, placement, TurnHorizon.Next(world), null, draftActions);'''),
('int tile = placement.Destinations[camp.Id]; bool abandon = abandoning.Contains(camp.Id);\n                fishingAccess', 'int tile = placement.Destinations[camp.Id]; bool abandon = abandoning.Contains(camp.Id);\n                int residents = ResidentCountAfterActions(camp, draftActions);\n                fishingAccess'),
('var plan = AutomaticIntent(world, camp, tile, abandon, index,','var plan = AutomaticIntent(world, camp, tile, abandon, residents, index,'),
('return new DraftIntent { Camp = camp.Id, Tile = tile, Abandoning = abandon,', 'return new DraftIntent { Camp = camp.Id, Tile = tile, Abandoning = abandon, Residents = residents,'),
])
edit('Assets/_Anthropocene/Simulation/Model.cs',[
('public double GrowthRemainder, DeathRemainder, StepFoodIncome;', '''// Transient planning preview only: dispatched residents still need food
        // before their new detachment exists in WorldState.Hunters.
        [NonSerialized] internal int PendingDispatchConsumers;
        public double GrowthRemainder, DeathRemainder, StepFoodIncome;''')])
edit('Assets/_Anthropocene/Simulation/ProductionDemand.cs',[
('int consumers = d.Consumers = c.Population + w.Hunters.Where(', 'int consumers = d.Consumers = c.Population + c.PendingDispatchConsumers + w.Hunters.Where(')])
edit('Assets/_Anthropocene/Simulation/EconomicPlanner.cs',[
('result.QoL = c.QoL; result.TradeExports', 'result.QoL = c.QoL; result.PendingDispatchConsumers = c.PendingDispatchConsumers; result.TradeExports')])
edit('Tools/Headless/Anthropocene.Headless.csproj',[
('<Compile Include="Program.cs" />','<Compile Include="Program.cs" />\n    <Compile Include="DispatchCohortChecks.cs" />')])
edit('Tools/Headless/Program.cs',[
('new ParallelChecks.Entry { Name = "EconomicTransitionChecks", Run = () => EconomicTransitionChecks.Run() },','new ParallelChecks.Entry { Name = "EconomicTransitionChecks", Run = () => EconomicTransitionChecks.Run() },\n                new ParallelChecks.Entry { Name = "DispatchCohortChecks", Run = () => DispatchCohortChecks.Run() },')])
