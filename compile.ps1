#!/usr/bin/env pwsh
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

$solution = Join-Path $root 'SharpModMenu.sln'
$project = Join-Path $root 'src/SharpModMenu/SharpModMenu.csproj'

if (-not (Test-Path $project)) {
	throw "Project file not found at $project"
}

[xml]$projectXml = Get-Content -Raw $project
function Invoke-CheckedCommand {
	param (
		[Parameter(Mandatory = $true)][scriptblock]$Command,
		[Parameter(Mandatory = $true)][string]$StepName
	)

	& $Command
	if ($LASTEXITCODE -ne 0) {
		throw "$StepName failed with exit code $LASTEXITCODE"
	}
}

function Get-ProjectPropertyValue {
	param (
		[Parameter(Mandatory = $true)][xml]$ProjectXml,
		[Parameter(Mandatory = $true)][string]$PropertyName
	)

	$node = $ProjectXml.SelectSingleNode("//Project/PropertyGroup/$PropertyName")
	if ($node -and $node.InnerText) {
		return [string]$node.InnerText
	}

	return ''
}

$assemblyName = Get-ProjectPropertyValue -ProjectXml $projectXml -PropertyName 'AssemblyName'
if ([string]::IsNullOrWhiteSpace($assemblyName)) {
	$assemblyName = [System.IO.Path]::GetFileNameWithoutExtension($project)
}

$targetFramework = Get-ProjectPropertyValue -ProjectXml $projectXml -PropertyName 'TargetFramework'
if ([string]::IsNullOrWhiteSpace($targetFramework)) {
	$targetFramework = 'net8.0'
}

$projectDir = Split-Path -Parent $project
$publishOutput = Join-Path $projectDir "bin/Release/$targetFramework/publish"

$compiledRoot = Join-Path $root 'compiled'
$counterStrikeSharpRoot = Join-Path $compiledRoot 'counterstrikesharp'
$pluginTarget = Join-Path $compiledRoot "counterstrikesharp/plugins/$assemblyName"
$sharedTarget = Join-Path $compiledRoot "counterstrikesharp/shared/$assemblyName"
$configPluginTarget = Join-Path $compiledRoot "counterstrikesharp/configs/plugins/SharpModMenu"
$cssUniversalSharedTarget = Join-Path $compiledRoot 'counterstrikesharp/shared/CSSUniversalMenuAPI'
$cssUniversalNugetDll = Join-Path $env:USERPROFILE '.nuget/packages/cssuniversalmenuapi/0.4.0/lib/net8.0/CSSUniversalMenuAPI.dll'

$dataFileNames = @('sharpmodmenu_config.jsonc', 'menu_config.jsonc', 'sharpmodmenu_translations.jsonc', 'menu_translations.jsonc')
$existingConfigPluginDir = Join-Path $compiledRoot "counterstrikesharp/configs/plugins/SharpModMenu"
$existingPluginDataDir = Join-Path $compiledRoot "counterstrikesharp/plugins/$assemblyName"
$existingSharedDataDir = Join-Path $compiledRoot "counterstrikesharp/shared/$assemblyName"
$preservedDataRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("sharpmodmenu-config-backup-" + [Guid]::NewGuid().ToString("N"))
$preservedDataFiles = @{}

function Backup-DataFiles {
	param (
		[Parameter(Mandatory = $true)][string[]]$FileNames,
		[Parameter(Mandatory = $true)][string[]]$SourceDirectories,
		[Parameter(Mandatory = $true)][string]$BackupRoot,
		[Parameter(Mandatory = $true)][hashtable]$BackupMap
	)

	foreach ($fileName in $FileNames) {
		$bestCandidatePath = $null
		$bestCandidateWriteTime = [DateTime]::MinValue

		foreach ($sourceDir in $SourceDirectories) {
			$candidate = Join-Path $sourceDir $fileName
			if (-not (Test-Path $candidate)) {
				continue
			}

			try {
				$candidateWriteTime = (Get-Item $candidate).LastWriteTimeUtc
			} catch {
				$candidateWriteTime = [DateTime]::MinValue
			}

			if (-not $bestCandidatePath -or $candidateWriteTime -gt $bestCandidateWriteTime) {
				$bestCandidatePath = $candidate
				$bestCandidateWriteTime = $candidateWriteTime
			}
		}

		if ($bestCandidatePath) {
			New-Item -ItemType Directory -Path $BackupRoot -Force | Out-Null
			$backupPath = Join-Path $BackupRoot $fileName
			Copy-Item -Path $bestCandidatePath -Destination $backupPath -Force
			$BackupMap[$fileName] = $backupPath
		}
	}
}

function Restore-DataFiles {
	param (
		[Parameter(Mandatory = $true)][hashtable]$BackupMap,
		[Parameter(Mandatory = $true)][string[]]$TargetDirectories
	)

	foreach ($entry in $BackupMap.GetEnumerator()) {
		foreach ($targetDir in $TargetDirectories) {
			New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
			Copy-Item -Path $entry.Value -Destination (Join-Path $targetDir $entry.Key) -Force
		}
	}
}

Backup-DataFiles -FileNames $dataFileNames -SourceDirectories @($existingConfigPluginDir, $existingSharedDataDir, $existingPluginDataDir) -BackupRoot $preservedDataRoot -BackupMap $preservedDataFiles
if ($preservedDataFiles.Count -gt 0) {
	Write-Host "[INFO] Preserving existing SharpModMenu config files: $($preservedDataFiles.Keys -join ', ')"
}

Remove-Item -Recurse -Force $compiledRoot -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $pluginTarget -Force | Out-Null

if (Test-Path $solution) {
	Invoke-CheckedCommand -StepName "dotnet restore solution" -Command { dotnet restore $solution }
	Invoke-CheckedCommand -StepName "dotnet publish project" -Command { dotnet publish $project -c Release --no-restore --nologo }
} else {
	Write-Host "[WARN] Solution not found at $solution. Publishing project directly."
	Invoke-CheckedCommand -StepName "dotnet restore project" -Command { dotnet restore $project }
	Invoke-CheckedCommand -StepName "dotnet publish project" -Command { dotnet publish $project -c Release --no-restore --nologo }
}

if (-not (Test-Path $publishOutput)) {
	throw "Publish output not found at $publishOutput"
}

Copy-Item -Path (Join-Path $publishOutput '*') -Destination $pluginTarget -Recurse -Force

$cssApi = Join-Path $pluginTarget 'CounterStrikeSharp.API.dll'
if (Test-Path $cssApi) {
	Remove-Item $cssApi -Force
}

$pluginCssUniversal = Join-Path $pluginTarget 'CSSUniversalMenuAPI.dll'
if (Test-Path $pluginCssUniversal) {
	Remove-Item $pluginCssUniversal -Force
}

$pluginDll = Join-Path $pluginTarget "$assemblyName.dll"
if (-not (Test-Path $pluginDll)) {
	throw "Published plugin DLL not found at $pluginDll"
}

New-Item -ItemType Directory -Path $sharedTarget -Force | Out-Null
Copy-Item -Path $pluginDll -Destination (Join-Path $sharedTarget "$assemblyName.dll") -Force

foreach ($dataFileName in @('sharpmodmenu_config.jsonc', 'menu_config.jsonc', 'sharpmodmenu_translations.jsonc', 'menu_translations.jsonc')) {
	$sourceDataFile = Join-Path $pluginTarget $dataFileName
	if (Test-Path $sourceDataFile) {
		New-Item -ItemType Directory -Path $configPluginTarget -Force | Out-Null
		Copy-Item -Path $sourceDataFile -Destination (Join-Path $configPluginTarget $dataFileName) -Force
		Remove-Item -Path $sourceDataFile -Force
	}
}

if ($preservedDataFiles.Count -gt 0) {
	Restore-DataFiles -BackupMap $preservedDataFiles -TargetDirectories @($configPluginTarget)
	Write-Host "[OK] Restored preserved SharpModMenu config files."
}

$cssUniversalSource = @(
	(Join-Path $publishOutput 'CSSUniversalMenuAPI.dll'),
	$cssUniversalNugetDll
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if ($cssUniversalSource) {
	New-Item -ItemType Directory -Path $cssUniversalSharedTarget -Force | Out-Null
	Copy-Item -Path $cssUniversalSource -Destination (Join-Path $cssUniversalSharedTarget 'CSSUniversalMenuAPI.dll') -Force
	Write-Host "[OK] CSSUniversalMenuAPI staged in shared/CSSUniversalMenuAPI."
} else {
	Write-Host "[WARN] CSSUniversalMenuAPI.dll source not found; shared/CSSUniversalMenuAPI will be missing."
}

$zipPath = Join-Path $compiledRoot "$assemblyName.zip"
if (Test-Path $zipPath) {
	Remove-Item $zipPath -Force
}
Compress-Archive -Path (Join-Path $counterStrikeSharpRoot '*') -DestinationPath $zipPath

if (Test-Path $preservedDataRoot) {
	Remove-Item -Recurse -Force $preservedDataRoot -ErrorAction SilentlyContinue
}

Write-Host "[OK] Build finished."
Write-Host " - Plugin: $pluginTarget"
Write-Host " - Shared: $sharedTarget"
Write-Host " - Config: $configPluginTarget"
Write-Host " - Zip:    $zipPath"
