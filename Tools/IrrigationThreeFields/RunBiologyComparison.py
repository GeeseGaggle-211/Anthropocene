"""Cloud-only secondary diagnostic. Never edits production source or the baseline harness.
Creates an archived derived harness to distinguish crop-only moisture from recomputed
irrigated biological potential and renewal. These are proposals, not accepted effects.
"""
from pathlib import Path
import subprocess
import json
import hashlib

root = Path.cwd()
base = root / 'Tools/IrrigationThreeFields/Program.cs'
src = base.read_text()

def replace_once(old: str, new: str) -> None:
    global src
    assert src.count(old) == 1, (old[:100], src.count(old))
    src = src.replace(old, new)

replace_once('private static long Integrations;', 'private static long Integrations;\n    private static SimulationConfig IrrigationConfig;\n    private static string BiologyResponse;')
replace_once('cfg.Width=1024;cfg.Height=512;cfg.Players=80;cfg.Shape=WorldShape.Continents;', 'cfg.Width=1024;cfg.Height=512;cfg.Players=80;cfg.Shape=WorldShape.Continents;\n        IrrigationConfig=cfg; BiologyResponse=args.Length>2?args[2]:"potential_only";')
old='''        var t=Copy(original);
        for(int s=0;s<4;s++) t.Seasons[s].Moisture=(float)(original.Seasons[s].Moisture+coverage*Math.Max(0,target-original.Seasons[s].Moisture));
        // Only crop moisture changes. No regenerated biomass, rainfall, terrain or fresh-water stock.
        return t;'''
new='''        var t=Copy(original);
        for(int s=0;s<4;s++)
        {
            var before=original.Seasons[s];
            t.Seasons[s].Moisture=(float)(before.Moisture+coverage*Math.Max(0,target-before.Moisture));
            if(coverage<=0 || t.Seasons[s].Moisture<=before.Moisture) continue;
            // A proposed local irrigated ceiling, using the SAME source productivity law.
            // Existing accessible wild-food capacity, rainfall, geography and stocks are not re-created.
            double potential=FoodEcology.LandBiomassBase*IrrigationConfig.LandBiomassMultiplier*
                FoodEcology.LandProductivity(t,t.Seasons[s],IrrigationConfig);
            t.Seasons[s].MaxPotentialFood=(float)Math.Max(before.MaxPotentialFood,potential);
            if(BiologyResponse=="potential_and_renewal")
            {
                double warmth=Numbers.Clamp((before.Temperature+8)/30.0,.015,1);
                double fraction=IrrigationConfig.LandFoodRegenerationInitialized?IrrigationConfig.LandFoodRegeneration:IrrigationConfig.FoodRegeneration;
                double rate=EcologyMath.DailyRate(fraction*(.2+t.Seasons[s].Moisture)*warmth,IrrigationConfig.BaseDays);
                t.Seasons[s].FoodRecoveryPerDay=(float)Math.Max(before.FoodRecoveryPerDay,rate);
                t.Seasons[s].BiomassRecoveryPerDay=(float)Math.Max(before.BiomassRecoveryPerDay,rate);
            }
        }
        return t;'''
replace_once(old,new)
replace_once('Console.WriteLine("FIXTURES controlled=9 generated="+sample+" eligible="+candidates.Count);', 'fixtures=fixtures.Where(f=>f.Kind=="generated").ToList();\n        Console.WriteLine("RESPONSE="+BiologyResponse+" generated="+sample+" eligible="+candidates.Count);')
replace_once('scope="Cloud native C# kernel diagnostic, not a full live village.', 'biologyResponse=BiologyResponse,\n            responseScope="SECONDARY SENSITIVITY: native biological capacity is recomputed at supplied effective moisture; potential_and_renewal also uses the native generation renewal equation. Natural MaxFood, climate, terrain and live stocks are not injected or overwritten. The inherited baseline scope below applies except its statement that biomass/recovery are fixed, which is intentionally false in these labelled variants. These overlays are not accepted production rules, natural vegetation feedback, full water balance, or live towns.",\n            scope="Cloud native C# kernel diagnostic, not a full live village.')

harness=root/'TestResults/IrrigationResponseHarness'
harness.mkdir(parents=True,exist_ok=True)
(harness/'Program.cs').write_text(src)
project=harness/'IrrigationResponse.csproj'
project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><ImplicitUsings>disable</ImplicitUsings><Nullable>disable</Nullable><EnableDefaultCompileItems>false</EnableDefaultCompileItems><LangVersion>9</LangVersion></PropertyGroup><ItemGroup><Compile Include="'+str(root/'Assets/_Anthropocene/Simulation/*.cs')+'"/><Compile Include="Program.cs"/></ItemGroup></Project>')
(harness/'derivation.json').write_text(json.dumps({'baseline_harness_sha256':hashlib.sha256(base.read_bytes()).hexdigest(),'derived_harness_sha256':hashlib.sha256(src.encode()).hexdigest(),'modes':['potential_only','potential_and_renewal'],'purpose':'Test native moisture-dependent potential/renewal coupling, not a new production implementation'},indent=2))
subprocess.run(['dotnet','build','-c','Release',str(project)],check=True)
for mode in ['potential_only','potential_and_renewal']:
    for seed in [11,23,47,101,1729]:
        out=root/'TestResults/IrrigationResponse'/mode/str(seed)
        subprocess.run(['dotnet','run','--no-build','-c','Release','--project',str(project),'--',str(seed),str(out),mode],check=True)
