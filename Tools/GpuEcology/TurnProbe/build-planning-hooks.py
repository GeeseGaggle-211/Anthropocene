"""Research-only nested attribution; original simulator files stay untouched."""
from pathlib import Path
import hashlib,json
p=Path(__file__).resolve().parents[3]
src=p/'Assets/_Anthropocene/Simulation'
out=Path(__file__).parent/'generated'
targets={
 'Agents.cs':[
  ('internal static SiteCoverageObservation LocalCoverage(', 'Observation.SiteCoverage'),
  ('private static AgriculturalSiteObservation[] AgriculturalCandidates(', 'Observation.AgriculturalCandidates')],
 'SettlementEconomy.cs':[
  ('public static HerdConsumptionProfile Owned(', 'HerdProfile.Owned'),
  ('public static HerdConsumptionProfile BiologicalGrowth(', 'HerdProfile.Growth'),
  ('internal static LocalHerdSurvey Survey(', 'Site.Survey'),
  ('internal static SettlementSiteEvaluation Evaluate(', 'Site.Evaluate'),
  ('internal static RetainedSettlementPortfolio Select(', 'Portfolio.Select')],
 'ProductionDemand.cs':[
  ('internal static CampProductionDemand BuildPhysical(', 'Demand.BuildPhysical')],
 'DemandControl.cs':[
  ('string Binding(Func<Request, double> grant)', 'Planner.Binding'),
  ('Dictionary<Request, double> RayPrefix(double fieldPeople,', 'Planner.RayPrefix'),
  ('DevelopmentChoice EvaluateAbsolute(double fieldBudget,', 'Planner.EvaluateAbsolute')],
 'FoodHorizonForecast.cs':[
  ('public static FoodInvestmentForecast Investment(', 'Forecast.Investment'),
  ('public static FoodTileForecast Tile(', 'Forecast.Tile'),
  ('public static FoodSettlementForecast Settlement(', 'Forecast.Settlement')],
}
manifest=[]
for name,methods in targets.items():
 original=(src/name).read_text();s=original
 for signature,label in methods:
  assert s.count(signature)==1,(name,signature,s.count(signature))
  start=s.index(signature);brace=s.index('{',start)
  assert '=>' not in s[start:brace],(name,signature)
  s=s[:brace+1]+f'\n            using var planningAttributionScope = PhaseProfile.Scope("{label}");'+s[brace+1:]
 (out/name).write_text(s)
 manifest.append(dict(file=name,originalSha256=hashlib.sha256(original.encode()).hexdigest(),generatedSha256=hashlib.sha256(s.encode()).hexdigest()))
(out/'planning-manifest.json').write_text(json.dumps(manifest,indent=2))
