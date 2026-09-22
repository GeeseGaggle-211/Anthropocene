param(
    [Parameter(Mandatory = $true)][string]$InputDirectory,
    [Parameter(Mandatory = $true)][string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$states = @{}
$matrix = @{}
Import-Csv -LiteralPath (Join-Path $InputDirectory 'matrix.csv') | ForEach-Object { $matrix[$_.id] = $_ }

function Number([object]$Value) {
    if ($null -eq $Value) { return 0.0 }
    return [double]$Value
}

function Snapshot($row) {
    $population = [int]$row.residentPopulation
    [pscustomobject]@{
        Turn = [int]$row.turn
        Year = Number $row.year
        Population = $population
        LinkedHunters = [int]$row.linkedHunters
        Q = Number $row.q
        E = Number $row.e
        FoodSatisfaction = Number $row.foodSatisfaction
        CampReserves = Number $row.campReserves
        LinkedReserves = Number $row.linkedReserves
        Farmers = [int]$row.farmers
        FoodGatherers = Number $row.foodGatherers
        MaterialLabor = Number $row.materialAssigned
        FarmerShare = if ($population) { (Number $row.farmers) / $population } else { 0 }
        FoodGathererShare = if ($population) { (Number $row.foodGatherers) / $population } else { 0 }
        MaterialLaborShare = if ($population) { (Number $row.materialAssigned) / $population } else { 0 }
        Specialists = [int]$row.specialists
        QoL = Number $row.qols.qol
        Moves = [int]$row.moves
        Footprint = (($row.footprint | ForEach-Object { "$($_.environment)/$($_.vegetation)" }) -join ';')
        Unlocked = (($row.unlocked | ForEach-Object { [string]$_ }) -join ';')
    }
}

Get-Content -LiteralPath (Join-Path $InputDirectory 'trajectory.jsonl') | ForEach-Object {
    if (![string]::IsNullOrWhiteSpace($_)) {
        $row = $_ | ConvertFrom-Json
        if ($null -ne $row.event) { return }
        if ($null -eq $row.turn) { return }
        $id = [string]$row.id
        if (!$states.ContainsKey($id)) {
            $states[$id] = [ordered]@{ First = $null; Discovery = $null; Final = $null; MinFood = [double]::PositiveInfinity; FirstCraft = $null; FirstSpecialist = $null; FirstGood = @{}; FirstAssignment = @{}; FirstUnlock = @{} }
        }
        $state = $states[$id]
        $snap = Snapshot $row
        if ($null -eq $state.First) { $state.First = $snap }
        if ($row.technology -eq 'Cultivation' -and $null -eq $state.Discovery) { $state.Discovery = $snap }
        $state.Final = $snap
        $state.MinFood = [Math]::Min($state.MinFood, (Number $row.foodSatisfaction))
        if ($null -eq $state.FirstSpecialist -and [int]$row.specialists -gt 0) { $state.FirstSpecialist = $snap }
        foreach ($production in @($row.production)) {
            if ((Number $production.output) -gt 0) {
                if ($null -eq $state.FirstCraft) {
                    $state.FirstCraft = [pscustomobject]@{ Turn = [int]$row.turn; Year = Number $row.year; Good = [string]$production.good; Role = [string]$production.role; Output = Number $production.output }
                }
                if (!$state.FirstGood.ContainsKey([string]$production.good)) { $state.FirstGood[[string]$production.good] = [pscustomobject]@{ Turn = [int]$row.turn; Year = Number $row.year; Role = [string]$production.role; Output = Number $production.output } }
            }
        }
        foreach ($assignment in $row.submittedAssignments.PSObject.Properties) {
            if ((Number $assignment.Value) -gt 0 -and !$state.FirstAssignment.ContainsKey($assignment.Name)) { $state.FirstAssignment[$assignment.Name] = [int]$row.turn }
        }
        foreach ($technology in @($row.unlocked)) {
            $name = [string]$technology
            if (!$state.FirstUnlock.ContainsKey($name)) { $state.FirstUnlock[$name] = [int]$row.turn }
        }
    }
}

$out = foreach ($entry in $states.GetEnumerator() | Sort-Object Key) {
    $s = $entry.Value
    $first = $s.First; $discovery = $s.Discovery; $final = $s.Final
    $m = $matrix[$entry.Key]
    [pscustomobject]@{
        Id = $entry.Key
        ActualInitialPopulation = [int]$m.initialPopulation; ActualInitialFood = Number $m.initialFood
        FirstResolvedTurn = $first.Turn; FirstResolvedPopulation = $first.Population; FirstResolvedQ = $first.Q; FirstResolvedE = $first.E
        DiscoveryTurn = if ($null -eq $discovery) { '' } else { $discovery.Turn }
        DiscoveryPopulation = if ($null -eq $discovery) { '' } else { $discovery.Population }
        DiscoveryQ = if ($null -eq $discovery) { '' } else { $discovery.Q }
        DiscoveryE = if ($null -eq $discovery) { '' } else { $discovery.E }
        FinalTurn = $final.Turn; FinalPopulation = $final.Population; FinalQ = $final.Q; FinalE = $final.E
        MinimumFoodSatisfaction = $s.MinFood; FinalCampReserves = $final.CampReserves; FinalLinkedReserves = $final.LinkedReserves
        FinalFarmers = $final.Farmers; FinalFoodGatherers = $final.FoodGatherers; FinalMaterialLabor = $final.MaterialLabor
        FinalFarmerShare = $final.FarmerShare; FinalFoodGathererShare = $final.FoodGathererShare; FinalMaterialLaborShare = $final.MaterialLaborShare
        FirstSpecialistTurn = if ($null -eq $s.FirstSpecialist) { '' } else { $s.FirstSpecialist.Turn }
        FirstSpecialistPopulation = if ($null -eq $s.FirstSpecialist) { '' } else { $s.FirstSpecialist.Population }
        FirstCraftTurn = if ($null -eq $s.FirstCraft) { '' } else { $s.FirstCraft.Turn }
        FirstCraftGood = if ($null -eq $s.FirstCraft) { '' } else { $s.FirstCraft.Good }
        FirstCraftRole = if ($null -eq $s.FirstCraft) { '' } else { $s.FirstCraft.Role }
        ControlledPopulationBefore = [int]$m.controlledPopulationBefore; ControlledPopulationAfter = [int]$m.controlledPopulationAfter; ControlledFoodBefore = Number $m.controlledFoodBefore; ControlledFoodAfter = Number $m.controlledFoodAfter; ControlledSettlementRatio = Number $m.transitionSupportRatio
        FinalQoL = $final.QoL; Moves = $final.Moves; FinalFootprint = $final.Footprint; FinalUnlocked = $final.Unlocked
    }
}
$out | Export-Csv -NoTypeInformation -LiteralPath (Join-Path $OutputDirectory 'phase-metrics.csv')
$emergence = foreach ($entry in $states.GetEnumerator() | Sort-Object Key) {
    foreach ($good in $entry.Value.FirstGood.GetEnumerator() | Sort-Object Key) { [pscustomobject]@{ Id = $entry.Key; Kind = 'useful-output'; Name = $good.Key; Turn = $good.Value.Turn; Year = $good.Value.Year; Role = $good.Value.Role; Output = $good.Value.Output } }
    foreach ($role in $entry.Value.FirstAssignment.GetEnumerator() | Sort-Object Key) { [pscustomobject]@{ Id = $entry.Key; Kind = 'submitted-assignment'; Name = $role.Key; Turn = $role.Value; Year = ''; Role = $role.Key; Output = '' } }
    foreach ($technology in $entry.Value.FirstUnlock.GetEnumerator() | Sort-Object Key) { [pscustomobject]@{ Id = $entry.Key; Kind = 'technology-observed'; Name = $technology.Key; Turn = $technology.Value; Year = ''; Role = ''; Output = '' } }
}
$emergence | Export-Csv -NoTypeInformation -LiteralPath (Join-Path $OutputDirectory 'emergence.csv')
Write-Host "Wrote $($out.Count) phase rows and $($emergence.Count) emergence rows to $OutputDirectory"
