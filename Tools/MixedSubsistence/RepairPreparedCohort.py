#!/usr/bin/env python3
from pathlib import Path
import sys

p=Path('Tools/Headless/Program.cs'); s=p.read_text()
a='                new ParallelChecks.Entry { Name = "MixedSettlementAcquisitionChecks", Run = () => MixedSettlementAcquisitionChecks.Run() },'
assert s.count(a)==1
if 'Name = "PreparedResidentCohortChecks"' not in s:
    s=s.replace(a,a+'\n                new ParallelChecks.Entry { Name = "PreparedResidentCohortChecks", Run = () => PreparedResidentCohortChecks.Run() },')
p.write_text(s)
p=Path('Tools/Headless/Anthropocene.Headless.csproj'); s=p.read_text()
a='  <Compile Include="MixedSettlementAcquisitionChecks.cs" />'
assert s.count(a)==1
if 'PreparedResidentCohortChecks.cs' not in s: s=s.replace(a,a+'\n  <Compile Include="PreparedResidentCohortChecks.cs" />')
p.write_text(s)
if '--register-only' in sys.argv: raise SystemExit(0)

def edit(path, changes):
    p=Path(path); s=p.read_text()
    for old,new in changes:
        assert s.count(old)==1, (path,old[:100],s.count(old))
        s=s.replace(old,new)
    p.write_text(s)

edit('Assets/_Anthropocene/Simulation/Model.cs',[
 ('        public int Id, Player, SocietyId, Tile, Population, DeficitTurns;',
  '        public int Id, Player, SocietyId, Tile, Population, DeficitTurns;\n        // Pure planning-view state only: residents already committed to Dispatch\n        // remain food consumers, but are unavailable for resident work. Never saved.\n        [NonSerialized] internal int PlanningAdditionalFoodConsumers;'),
 ('public int LinkedPopulation(Camp camp) => camp.Population + Hunters.Where',
  'public int LinkedPopulation(Camp camp) => camp.Population + camp.PlanningAdditionalFoodConsumers + Hunters.Where')])
edit('Assets/_Anthropocene/Simulation/ProductionDemand.cs',[
 ('int consumers = d.Consumers = c.Population + w.Hunters.Where',
  'int consumers = d.Consumers = c.Population + c.PlanningAdditionalFoodConsumers + w.Hunters.Where')])
edit('Assets/_Anthropocene/Simulation/NeolithicProduction.cs',[
 ('(residents < 0 ? c.Population : residents) + w.Hunters.Where',
  '(residents < 0 ? c.Population : residents) + c.PlanningAdditionalFoodConsumers + w.Hunters.Where')])
edit('Assets/_Anthropocene/Simulation/EconomicPlanner.cs',[
 ('            result.QoL = c.QoL; result.TradeExports',
  '            result.PlanningAdditionalFoodConsumers = c.PlanningAdditionalFoodConsumers;\n            result.QoL = c.QoL; result.TradeExports')])
p=Path('Assets/_Anthropocene/Simulation/GenerationalEconomy.cs'); s=p.read_text()
a='camp.Population + w.Hunters.Where'
assert s.count(a)==4, s.count(a)
s=s.replace(a,'camp.Population + camp.PlanningAdditionalFoodConsumers + w.Hunters.Where')
p.write_text(s)

edit('Assets/_Anthropocene/Simulation/TurnPlanning.cs',[
 ('        public int Camp, Tile;\n        public bool Abandoning;',
  '        public int Camp, Tile, Residents;\n        public bool Abandoning;'),
 ('prepared.Horizon, useDraft ? draft.Intents : null);',
  'prepared.Horizon, useDraft ? draft.Intents : null, acceptedActions);'),
 ('                bool abandoning = prepared.AbandoningVillages.Contains(camp.Id);\n                if (useDraft',
  '                bool abandoning = prepared.AbandoningVillages.Contains(camp.Id);\n                int departing = DepartingResidents(camp, acceptedActions);\n                int residents = Math.Max(0, camp.Population - departing);\n                if (useDraft'),
 ('cached.Tile == effectiveTile && cached.Abandoning == abandoning)',
  'cached.Tile == effectiveTile && cached.Abandoning == abandoning && cached.Residents == residents)'),
 ('index, trade, access, developmentSearch,\n                    developmentDiagnostics);',
  'index, trade, access, developmentSearch,\n                    developmentDiagnostics, departing);'),
 ('        private static AutomaticPlan AutomaticIntent(WorldState world, Camp camp, int effectiveTile, bool abandoning,',
  '        private static int DepartingResidents(Camp camp, IReadOnlyDictionary<int, Order> actions)\n            => actions != null && actions.TryGetValue(camp.Id, out var action) &&\n                action.Kind == OrderKind.Dispatch ? Math.Max(0, action.Population) : 0;\n\n        private static AutomaticPlan AutomaticIntent(WorldState world, Camp camp, int effectiveTile, bool abandoning,'),
 ('            DemandController.DevelopmentDiagnostics developmentDiagnostics =\n                null)',
  '            DemandController.DevelopmentDiagnostics developmentDiagnostics =\n                null, int departing = 0)'),
 ('            using var profile = PhaseProfile.Scope("Intent");\n            var planningCamp = PlanningCampAt(camp, effectiveTile, trade);',
  '            using var profile = PhaseProfile.Scope("Intent");\n            var planningCamp = PlanningCampAt(camp, effectiveTile, trade);\n            if (departing > 0)\n            {\n                planningCamp = planningCamp.WithPrivateInventory();\n                planningCamp.Population = Math.Max(0, camp.Population - departing);\n                planningCamp.PlanningAdditionalFoodConsumers = departing;\n            }'),
 ('                Population = camp.Population, Technologies = camp.People.NeolithicTechnologies.ToArray(),',
  '                Population = planningCamp.Population, Technologies = camp.People.NeolithicTechnologies.ToArray(),'),
 ('            IReadOnlyDictionary<int, DraftIntent> reusable = null)',
  '            IReadOnlyDictionary<int, DraftIntent> reusable = null,\n            IReadOnlyDictionary<int, Order> acceptedActions = null)'),
 ('                int residents = Math.Max(0, camp.Population);',
  '                int residents = Math.Max(0, camp.Population - DepartingResidents(camp, acceptedActions));'),
 ('            var fishingAccess = BuildFishingAccessPlans(world, world.Camps, placement, TurnHorizon.Next(world));',
  '            var draftActions = orders.Where(o => o.Kind != OrderKind.Economy && o.Kind != OrderKind.FissionPolicy).ToDictionary(o => o.Entity);\n            var fishingAccess = BuildFishingAccessPlans(world, world.Camps, placement, TurnHorizon.Next(world), null, draftActions);'),
 ('                var plan = AutomaticIntent(world, camp, tile, abandon, index,\n                    trade, access);\n                return new DraftIntent { Camp = camp.Id, Tile = tile, Abandoning = abandon,',
  '                int departing = DepartingResidents(camp, draftActions);\n                var plan = AutomaticIntent(world, camp, tile, abandon, index,\n                    trade, access, departing: departing);\n                return new DraftIntent { Camp = camp.Id, Tile = tile, Residents = Math.Max(0, camp.Population - departing), Abandoning = abandon,'),
 ('            int residents = Math.Max(0, camp.Population - departing);\n            int connectedHunters',
  '            int residents = Math.Max(0, camp.Population - departing);\n            if (departing > 0)\n            {\n                planningCamp = planningCamp.WithPrivateInventory();\n                planningCamp.Population = residents;\n                planningCamp.PlanningAdditionalFoodConsumers = departing;\n            }\n            int connectedHunters')])
print('Prepared cohort repaired: available resident workforce, total food consumers, and draft cache keys remain distinct.')
