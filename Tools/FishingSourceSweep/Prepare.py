"""Idempotently prepare the test adapter; never edits production source files."""
from pathlib import Path
p = Path(__file__).with_name('Program.cs')
s = p.read_text()
old = '''        end.Days = start + days;
        var closing = new EcologyCapacity(w, t, end, (int)Math.Floor(end.Days / w.Config.BaseDays) % 4);
        double residual = Math.Abs(opening.Food + f.Recharge - f.Realized - f.Overflow - f.Stock);
        residual = Math.Max(residual, Math.Abs(opening.PotentialFood + f.Recharge + b.Recharge - f.Realized - f.Overflow - b.Overflow - end.PotentialFood));'''
new = '''        end.Days = start + days;
        end.Saltwater = t.Seasons[(int)Math.Floor(end.Days / w.Config.BaseDays) % 4].MaxSaltwater;
        var beforeClamp = end;
        LazyEcology.ClampCapacity(w, t, ref end);
        var closing = new EcologyCapacity(w, t, end, (int)Math.Floor(end.Days / w.Config.BaseDays) % 4);
        double foodOverflow = f.Overflow + beforeClamp.Food - end.Food;
        double biomassOverflow = f.Overflow + b.Overflow + beforeClamp.PotentialFood - end.PotentialFood;
        double residual = Math.Abs(opening.Food + f.Recharge - f.Realized - foodOverflow - end.Food);
        residual = Math.Max(residual, Math.Abs(opening.PotentialFood + f.Recharge + b.Recharge - f.Realized - biomassOverflow - end.PotentialFood));'''
if old in s:
    assert s.count(old) == 1
    p.write_text(s.replace(old, new))
elif new not in s:
    raise RuntimeError('The expected test adapter changed; refusing a blind patch')
