using System;
using System.Collections.Generic;
using System.Linq;
using Anthropocene.Simulation;

// G1 label-fidelity diagnostics: the economic diagnostics carry the native planner's pre-execution Herder staffing
// (assigned residents and planned worker-years, independent of tool-covered service) and keep migration's
// person-equivalent food estimate out of physical food transfer sums.
internal static class ElderEconomicStaffingChecks
{
    private static int checks;
    private static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception("ElderEconomicStaffing: " + message); }
    private static double D(object value) => Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);

    public static int Run()
    {
        checks = 0;
        MigrationUnitChecks();
        HerderStaffingChecks();
        Console.WriteLine("PASS: " + checks + " elder economic staffing assertions");
        return checks;
    }

    private static void MigrationUnitChecks()
    {
        var rows = new List<Dictionary<string, object>>
        {
            new Dictionary<string, object> { ["kind"] = "migration", ["sourceCamp"] = 1, ["destinationCamp"] = 2, ["people"] = 3,
                ["plantFood"] = 2.0, ["animalFood"] = 1.0, ["aquaticFood"] = 0.5, ["carriedFoodPersonEquivalents"] = 40.0 },
            new Dictionary<string, object> { ["kind"] = "fission-inventory", ["sourceCamp"] = 1, ["destinationCamp"] = 2, ["people"] = 4,
                ["plantFood"] = 10.0, ["animalFood"] = 0.0, ["aquaticFood"] = 0.0 },
            new Dictionary<string, object> { ["kind"] = "hunter-logistics", ["hunterId"] = 9, ["deposited"] = 100.0 },
        };
        Check(Math.Abs(ElderEconomicCapture.TransferFood(rows, 2, true) - 13.5) < 1e-12,
            "physical food transfer-in excludes the migration person-equivalent estimate");
        Check(Math.Abs(ElderEconomicCapture.TransferFood(rows, 1, false) - 13.5) < 1e-12,
            "physical food transfer-out excludes the migration person-equivalent estimate");
        Check(ElderEconomicCapture.TransferFood(rows, 1, true) == 0, "a source camp receives no transfer food");
    }

    private static void HerderStaffingChecks()
    {
        int herdingOnly = 1 << (int)Technology.Cultivation | 1 << (int)Technology.Watercraft;
        var snapshot = ElderModel9Environment.GenerateOpening(17, 8, out _, 40, herdingOnly);
        var environment = new ElderModel9Environment { EconomicDiagnosticsEnabled = true };
        var step = environment.Reset(snapshot, true);
        string[] keys = { "assignedHerderResidents", "managementHerderResidents", "developmentHerderResidents",
            "managementHerderWorkerYears", "developmentHerderWorkerYears", "assignedHerderWorkerYears" };
        int staffedRows = 0, allocationRows = 0, migrationRows = 0;
        for (int turn = 0; turn < 6; turn++)
        {
            step = environment.Step(new Dictionary<int, ElderDirective>());
            foreach (var diagnostic in step.EconomicDiagnostics.Values)
            {
                var herding = (Dictionary<string, object>)diagnostic.Domains["herding"];
                var manufacturing = (Dictionary<string, object>)diagnostic.Domains["manufacturing"];
                bool allocated = (bool)manufacturing["active"];
                if (!allocated)
                {
                    foreach (string key in keys)
                        Check(herding.ContainsKey(key) && herding[key] == null
                            && diagnostic.Unavailable["domains.herding." + key] == "no-native-demand-allocation",
                            "staffing is explicitly unavailable, never zero, without a native allocation");
                    continue;
                }
                allocationRows++;
                foreach (string key in keys) Check(herding[key] is double, "allocated rows carry every Herder staffing field");
                double assigned = D(herding["assignedHerderResidents"]), management = D(herding["managementHerderResidents"]),
                    development = D(herding["developmentHerderResidents"]);
                Check(assigned == management + development && assigned >= 0 && management >= 0 && development >= 0
                    && assigned == Math.Floor(assigned), "assigned Herders are whole residents split into management and development");
                var population = (Dictionary<string, object>)diagnostic.Domains["population"];
                Check(assigned <= D(population["openingResidents"]), "assigned Herders never exceed the opening residents");
                Check(Math.Abs(D(herding["assignedHerderWorkerYears"]) - assigned * diagnostic.Years) < 1e-9
                    && Math.Abs(D(herding["managementHerderWorkerYears"]) - management * diagnostic.Years) < 1e-9
                    && Math.Abs(D(herding["developmentHerderWorkerYears"]) - development * diagnostic.Years) < 1e-9,
                    "planned Herder worker-years are the assigned residents' full decision interval");
                Check(Math.Abs(D(herding["plannedHerderYears"]) - management * diagnostic.Years) < 1e-9,
                    "management worker-years agree with the legacy planned management field");
                Check(diagnostic.Units["domains.herding.assignedHerderResidents"] == "people"
                    && diagnostic.Units["domains.herding.assignedHerderWorkerYears"] == "worker-years", "staffing fields declare units");
                if (assigned > 0) staffedRows++;
                foreach (var transfer in diagnostic.Transfers.Where(t => (string)t["kind"] == "migration"))
                {
                    migrationRows++;
                    Check(!transfer.ContainsKey("carriedFood") && transfer.ContainsKey("carriedFoodPersonEquivalents"),
                        "migration rows name the person-equivalent estimate explicitly");
                }
            }
        }
        Check(allocationRows > 0, "the herding fixture produces native allocations");
        Check(staffedRows > 0, "the herding fixture staffs Herders in at least one decision");
        Console.WriteLine("ElderEconomicStaffing: " + allocationRows + " allocated rows, " + staffedRows + " staffed, " + migrationRows + " migration witnesses");
    }
}
