using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Anthropocene.Simulation;
class Probe {
static void Main(string[] args) {
var w = SaveCodec.Load(File.ReadAllBytes(args[0]));
Console.WriteLine("initial " + SaveCodec.Fingerprint(w));
for(int i=0;i<4;i++) { var ai = AiPlanning.Decide(w); new TurnResolver().Resolve(w, TurnPlanner.Prepare(w, ai.Orders)); Console.WriteLine("step " + i + " " + SaveCodec.Fingerprint(w)); }
var env = new NativeElderEnvironment();
var reset = env.Reset(new ElderResetRequest { Snapshot = File.ReadAllBytes(args[0]), ControlledCampId = w.Camps[0].Id });
for(int i=0;i<3;i++) { var result = env.Step(new ElderDirective { RetainedFieldOperation=.7, RetainedHerdFraction=.5, DevelopmentBudget=.2, FieldDevelopmentShare=.6 }); Console.WriteLine("old-elder " + i + " " + result.StateFingerprint); }
NativeElderEnvironment.VerifyReplay(env.ReplayLog);
}}
