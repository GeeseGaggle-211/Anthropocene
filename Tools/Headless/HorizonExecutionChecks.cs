using System;
using System.Collections.Generic;
using Anthropocene.Simulation;

internal static class HorizonExecutionChecks
{
    public static void Run()
    {
        var world = new WorldGenerator().Generate(219, SimulationConfig.Small(2));
        var original = SaveCodec.Save(world);
        (int ActiveTiles, int TileIntegrations, long ClaimsAllocated) RunAt(double days)
        {
            var state = SaveCodec.Load(original);
            state.Config.TurnDays = days;
            var camp = state.Camps[0];
            camp.Technology = Technology.Cultivation;
            camp.Technologies[(int)Technology.Cultivation] = true;
            Ownership.Bind(state);
            var resolver = new TurnResolver(new PhaseExecution(1));
            var prepared = TurnPlanner.Prepare(state, new Dictionary<int, List<Order>>());
            resolver.Resolve(state, prepared);
            var metrics = resolver.LastMetrics;
            if (metrics.SeasonalTiles != 0 || metrics.TileIntegrations != metrics.ActiveTiles ||
                metrics.FullTurnTiles != metrics.ActiveTiles)
                throw new Exception("Horizon execution must evaluate each active tile exactly once.");
            return (metrics.ActiveTiles, metrics.TileIntegrations, metrics.ClaimsAllocated);
        }
        var reference = RunAt(365);
        foreach (double years in new[] { .5, 5.0, 10.0, 20.0 })
        {
            var measured = RunAt(years * 365);
            if (reference.TileIntegrations != measured.TileIntegrations ||
                reference.ActiveTiles != measured.ActiveTiles)
                throw new Exception("Horizon duration changed the number of tile kernels on a fixed world at " + years + " years.");
        }
        Console.WriteLine("PASS: horizon execution uses " + reference.TileIntegrations +
            " tile kernels for fractional, one-, five-, ten-, and twenty-year turns");
    }
}
