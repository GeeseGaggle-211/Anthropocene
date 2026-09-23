param(
    [Parameter(Mandatory = $true)][string]$InputDirectory,
    [Parameter(Mandatory = $true)][string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$InputDirectory = [IO.Path]::GetFullPath($InputDirectory)
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$matrix = Get-Content (Join-Path $InputDirectory 'matrix.jsonl') | ForEach-Object { $_ | ConvertFrom-Json }
$matrix | Where-Object { $_.id -ne 'replay' } | Select-Object id,b,turns,durationYears,durationFixture,scarceDurationFixture,fixedForageFixture,initialResidents,finalResidents,initialAnimals,finalAnimals,finalGrowthRemainder,finalDeathRemainder,finalDeficitTurns,totalAnimalBirths,totalAnimalDeaths,acceptedAnimalProduct,potentialAnimalProduct,managedFoodRequested,managedFoodRealized,managedWaterRequested,managedWaterRealized,fingerprint,error | Export-Csv (Join-Path $OutputDirectory 'matrix.csv') -NoTypeInformation

function Number($rows, [scriptblock]$select, [string]$mode) {
    $values = @($rows | ForEach-Object $select)
    if ($mode -eq 'sum') { return ($values | Measure-Object -Sum).Sum }
    if ($mode -eq 'min') { return ($values | Measure-Object -Minimum).Minimum }
    if ($mode -eq 'max') { return ($values | Measure-Object -Maximum).Maximum }
    throw "Unsupported mode $mode"
}
function Role($row, [string]$name) { $p = $row.submittedAssignments.PSObject.Properties[$name]; if ($null -eq $p) { return 0 }; return [double]$p.Value }

$rows = Get-Content (Join-Path $InputDirectory 'trajectory.jsonl') | ForEach-Object { $_ | ConvertFrom-Json } | Where-Object { $null -ne $_.turn }
$phase = foreach ($group in $rows | Where-Object { $_.id -like 'live-*' } | Group-Object id) {
    $g = @($group.Group | Sort-Object turn); $first = $g[0]; $last = $g[-1]
    [pscustomobject]@{
        id = $group.Name; turns = $g.Count
        residentStart = $first.residentBefore; residentFinal = $last.resident; linkedFinal = $last.linked
        animalsStart = $first.herdBefore.Animals; animalsFinal = $last.herd; animalHumanRatioFinal = $last.animalRatio
        phaseStartHerders = Role $first 'Herder'; phaseFinalHerders = Role $last 'Herder'
        realizedGathererWorkerEquivalentFinal = $last.gatheringWorkerEquivalent; residualGatherersFinal = $last.residualGatherers
        minimumNutrition = Number $g { $_.report.Nutrition } min; minimumHydration = Number $g { $_.report.WaterNutrition } min; minimumManagement = Number $g { $_.report.ManagementFulfillment } min
        minimumFoodSatisfaction = Number $g { $_.campFood.FoodNeedsMetRatio } min; minimumWaterSatisfaction = Number $g { $_.campFood.WaterNeedsMetRatio } min
        animalBirths = Number $g { $_.report.NewbornAnimals } sum; animalDeaths = Number $g { $_.report.StarvationDeaths } sum
        humanBirths = Number $g { $_.demographic.Additions } sum; humanDeaths = Number $g { $_.demographic.Deaths } sum
        acceptedAnimalProduct = Number $g { $_.report.AcceptedFood } sum; potentialAnimalProduct = Number $g { $_.report.PotentialFood } sum
        managedBiomassRequested = Number $g { @($_.managedPressure | Where-Object Pool -eq 'Biomass' | Measure-Object Requested -Sum).Sum } sum
        managedBiomassRealized = Number $g { @($_.managedPressure | Where-Object Pool -eq 'Biomass' | Measure-Object Realized -Sum).Sum } sum
        managedWaterRequested = Number $g { @($_.managedPressure | Where-Object Pool -eq 'Freshwater' | Measure-Object Requested -Sum).Sum } sum
        managedWaterRealized = Number $g { @($_.managedPressure | Where-Object Pool -eq 'Freshwater' | Measure-Object Realized -Sum).Sum } sum
        moves = $last.moves
    }
}
$phase | Export-Csv (Join-Path $OutputDirectory 'phase-metrics.csv') -NoTypeInformation

$duration = foreach ($group in $rows | Where-Object { $_.durationFixture -or $_.scarceDurationFixture } | Group-Object id) {
    $g = @($group.Group | Sort-Object turn); $first = $g[0]; $last = $g[-1]
    [pscustomobject]@{ id = $group.Name; turns = $g.Count; durationYears = $first.year / $first.turn; animalsStart = $first.herdBefore.Animals; animalsFinal = $last.herd; finalGrowthRemainder = $last.herdAfterState.GrowthRemainder; finalDeathRemainder = $last.herdAfterState.DeathRemainder; finalDeficitTurns = $last.herdAfterState.DeficitTurns; potentialAnimalProduct = Number $g { $_.report.PotentialFood } sum; acceptedAnimalProduct = Number $g { $_.report.AcceptedFood } sum; managedBiomassRequested = Number $g { @($_.managedPressure | Where-Object Pool -eq 'Biomass' | Measure-Object Requested -Sum).Sum } sum; managedBiomassRealized = Number $g { @($_.managedPressure | Where-Object Pool -eq 'Biomass' | Measure-Object Realized -Sum).Sum } sum; managedWaterRequested = Number $g { @($_.managedPressure | Where-Object Pool -eq 'Freshwater' | Measure-Object Requested -Sum).Sum } sum; managedWaterRealized = Number $g { @($_.managedPressure | Where-Object Pool -eq 'Freshwater' | Measure-Object Realized -Sum).Sum } sum }
}
$duration | Export-Csv (Join-Path $OutputDirectory 'duration-metrics.csv') -NoTypeInformation
Write-Host "Wrote compact tables to $OutputDirectory"
