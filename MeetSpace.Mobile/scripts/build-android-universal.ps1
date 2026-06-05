param(
	[Parameter(Mandatory = $false)]
	[string]$ProjectPath,

	[Parameter(Mandatory = $false)]
	[string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
	$scriptRoot = $PSScriptRoot
	if ([string]::IsNullOrWhiteSpace($scriptRoot)) {
		$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
	}

	$ProjectPath = Join-Path $scriptRoot "..\MeetSpace.Mobile.csproj"
}
$projectFullPath = (Resolve-Path $ProjectPath).Path

dotnet build $projectFullPath `
	-t:SignAndroidPackage `
	-f net10.0-android `
	-c $Configuration `
	-p:RunAOTCompilation=false

$releaseRoot = Join-Path (Split-Path $projectFullPath -Parent) "bin\$Configuration\net10.0-android"

Write-Host ""
Write-Host "Universal Android artifacts:"

$primaryArtifacts = @(
	(Join-Path $releaseRoot "com.meetspace.mobile-Signed.apk"),
	(Join-Path $releaseRoot "com.meetspace.mobile-Signed.aab"),
	(Join-Path $releaseRoot "com.meetspace.mobile.aab")
) | Where-Object { Test-Path $_ }

foreach ($artifactPath in $primaryArtifacts) {
	Write-Host " - $artifactPath"
}

$signedApkPath = Join-Path $releaseRoot "com.meetspace.mobile-Signed.apk"
if (!(Test-Path $signedApkPath)) {
	$fallbackSignedApk = Get-ChildItem -Path $releaseRoot -Recurse -File -Filter *Signed.apk |
		Sort-Object FullName |
		Select-Object -First 1
	if ($fallbackSignedApk) {
		$signedApkPath = $fallbackSignedApk.FullName
	}
}

$signedApk = $null
if ($signedApkPath -and (Test-Path $signedApkPath)) {
	$signedApk = Get-Item $signedApkPath
}

if ($signedApk) {
	Add-Type -AssemblyName System.IO.Compression.FileSystem
	$zip = [IO.Compression.ZipFile]::OpenRead($signedApk.FullName)
	$abis = $zip.Entries |
		Where-Object { $_.FullName -like "lib/*/lib*.so" } |
		ForEach-Object { ($_.FullName -split "/")[1] } |
		Sort-Object -Unique
	$zip.Dispose()

	Write-Host ""
	Write-Host "Detected ABIs in $($signedApk.Name): $($abis -join ', ')"
}
