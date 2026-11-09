#!/usr/bin/env python3
"""Build and execute the isolated native-source experiment on the cloud runner."""
import hashlib
import json
from pathlib import Path
import subprocess

root = Path.cwd()
base = root / 'Tools/IrrigationThreeFields/Program.cs'
source = base.read_text()
old = 'internal static class Program'
assert source.count(old) == 1
source = source.replace(old, 'internal static partial class Program')
start = source.index('        foreach(var f in fixtures){SaveProfile(f);Sweep(w,f);}')
# Reuse the exact prior world/fixture selection. Replace only the experiment dispatch.
source = source[:start] + '        SoilRun(w,fixtures,reference,output,seed,candidates.Count);\n    }\n}\n'
folder = root / 'TestResults/IrrigationSoil600/Harness'
folder.mkdir(parents=True, exist_ok=True)
(folder / 'Program.cs').write_text(source)
addition = root / 'Tools/IrrigationSoil600/Soil600.cs'
(folder / 'Soil600.cs').write_bytes(addition.read_bytes())
project = folder / 'IrrigationSoil600.csproj'
project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><ImplicitUsings>disable</ImplicitUsings><Nullable>disable</Nullable><EnableDefaultCompileItems>false</EnableDefaultCompileItems><LangVersion>9</LangVersion></PropertyGroup><ItemGroup><Compile Include="'+str(root/'Assets/_Anthropocene/Simulation/*.cs')+'"/><Compile Include="Program.cs"/><Compile Include="Soil600.cs"/></ItemGroup></Project>')
(folder / 'derivation.json').write_text(json.dumps({'base_source_sha256':hashlib.sha256(base.read_bytes()).hexdigest(),'derived_program_sha256':hashlib.sha256(source.encode()).hexdigest(),'soil_source_sha256':hashlib.sha256(addition.read_bytes()).hexdigest(),'purpose':'600-year soil development with native unchanged ecological kernel; not production implementation'},indent=2))
subprocess.run(['dotnet','build','-c','Release',str(project)],check=True)
for seed in [11,23,47,101,1729]:
    output = root / 'TestResults/IrrigationSoil600' / str(seed)
    subprocess.run(['dotnet','run','--no-build','-c','Release','--project',str(project),'--',str(seed),str(output)],check=True)
