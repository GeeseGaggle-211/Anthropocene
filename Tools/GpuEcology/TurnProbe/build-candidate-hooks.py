"""Generate research-only source copies; fail closed if any targeted expression changes."""
from pathlib import Path
import hashlib,json
p=Path(__file__).resolve().parents[3]
src=p/'Assets/_Anthropocene/Simulation';out=Path(__file__).parent/'generated';out.mkdir(exist_ok=True)
manifest=[]
def replace(s,a,b,count=1):
    assert s.count(a)==count,(a[:90],s.count(a),count)
    return s.replace(a,b)
for name in ['GenerationalEconomy.cs','NeolithicProduction.cs','FishingEconomy.cs','LazyEcology.cs']:
    original=(src/name).read_text();s=original
    if name=='GenerationalEconomy.cs':
        a='var plans = w.Camps.OrderBy(c => c.Id).ToDictionary(c => c.Id, c => PlanGrowth(w, c, intervals, years, needs[c.Id]));'
        s=replace(s,a,'var plans = global::CandidateBridge.Capture || global::CandidateBridge.Enabled(global::CandidateKind.Population) ? CandidatePopulationPlans(w, intervals, years, needs) : w.Camps.OrderBy(c => c.Id).ToDictionary(c => c.Id, c => PlanGrowth(w, c, intervals, years, needs[c.Id]));')
        a='(herd.Animals + herd.GrowthRemainder) * Math.Exp(species.GrowthRate * breeding * years * herd.LastNutrition)'
        s=replace(s,a,'global::CandidateBridge.Breed(herd.Animals, herd.GrowthRemainder, species.GrowthRate, breeding, years, herd.LastNutrition)')
    if name=='NeolithicProduction.cs':
        start=s.index('            if (recipe.Stone > 0) amount = Math.Min(amount, inv.Stone / recipe.Stone);',s.index('public static double Produce('))
        end=s.index('            // A recipe ranked below Housing Material',start)
        a=s[start:end]
        assert a.count('amount = Math.Min')==7
        oracle=a.replace('amount','candidateAmount')
        reference=a
        substitutions={
            'inv.AnimalFabric - c.TradeHeldForExport(CraftGood.AnimalFabric)':'p.L',
            'inv.HousingMaterial - c.TradeHeldForExport(CraftGood.HousingMaterial)':'p.N',
            'inv.Stone':'p.B','recipe.Stone':'p.C','inv.Clay':'p.D','recipe.Clay':'p.E',
            'inv.Wood':'p.F','recipe.Wood':'p.G','c.PlantFood':'p.H','recipe.PlantFood':'p.I',
            'c.AnimalFood':'p.J','recipe.AnimalFood':'p.K','recipe.AnimalFabric':'p.M','recipe.HousingMaterial':'p.O'}
        for old,new in substitutions.items():reference=reference.replace(old,new)
        assert 'recipe.' not in reference and 'inv.' not in reference and 'c.' not in reference
        (out/'CandidateSourceReference.cs').write_text('using System;\nstatic class CandidateSourceReference { public static CandidateOutput Production(CandidateInput p) { double amount=p.A;\n'+reference+'\nreturn new CandidateOutput {A=amount,Discrete=amount>1e-12?1:0}; } }\n')
        s=s[:start]+'''            if (global::CandidateBridge.Capture || global::CandidateBridge.Enabled(global::CandidateKind.Production))
            {
                var candidateInput = global::CandidateBridge.PackProduction(c, recipe, amount);
                amount = global::CandidateBridge.Apply(global::CandidateKind.Production, candidateInput, () => {
                    double candidateAmount = amount;
'''+oracle+'''                    return new global::CandidateOutput { A = candidateAmount, Discrete = candidateAmount > 1e-12 ? 1 : 0 };
                }).A;
            }
            else
            {
'''+a+'''            }
'''+s[end:]
    if name=='NeolithicProduction.cs':
        start=s.index('                for (int balanceLayer = 0; balanceLayer < 2; balanceLayer++)')
        end=s.index('            }\n            c.TradeExports = null;',start)
        original_loop=s[start:end]
        s=s[:start]+"                if (!global::FusionBridge.Try(w, c, intervals, record, needs, recipes, coverage, housing, laborByRole, produced, steps))\n                {\n"+original_loop+"                }\n                global::FusionBridge.End(record);\n"+s[end:]
    if name=='FishingEconomy.cs':
        a='return NeolithicCapital.Decay(world.Config, site.Capital, operationFraction, elapsedYears);'
        s=replace(s,a,'return global::CandidateBridge.FisheryCapital(world, site, elapsedYears, operationFraction);')
    if name=='LazyEcology.cs':
        s=replace(s,'EcologyMath.Stock(', 'global::CandidateBridge.AquaticStock(',6)
        s=replace(s,'EcologyMath.ManagedStock(', 'global::CandidateBridge.AquaticManagedStock(',1)
    (out/name).write_text(s)
    manifest.append(dict(file=name,originalSha256=hashlib.sha256(original.encode()).hexdigest(),generatedSha256=hashlib.sha256(s.encode()).hexdigest()))
(out/'manifest.json').write_text(json.dumps(manifest,indent=2))
