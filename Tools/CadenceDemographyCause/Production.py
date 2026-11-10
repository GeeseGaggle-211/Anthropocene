from pathlib import Path
import subprocess, sys
root=Path.cwd()
src=(root/'Tools/CadenceDemographyCause/Run.py').read_text()
old="seed,'small',str(work),treatment"
assert src.count(old)==1
src=src.replace(old,"seed,'production',str(work),treatment")
src=src.replace("root/'TestResults'/'CadenceDemographyCause'", "root/'TestResults'/'CadenceDemographyCauseProduction'")
old='        var reports = record.Demographics.ToDictionary(x => x.Camp);'
assert src.count(old)==1
src=src.replace(old,'        if (w.Config.Width >= 1024 && elapsed < 1300) return;\n'+old)
out=root/'TestResults/ProductionCauseDriver.py'
out.parent.mkdir(parents=True,exist_ok=True)
out.write_text(src)
subprocess.run([sys.executable,str(out),'11','baseline'],check=True)
