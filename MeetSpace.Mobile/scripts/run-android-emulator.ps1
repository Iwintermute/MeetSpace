param(
	[Parameter(Mandatory = $false)]
	[string]$ProjectPath,
	[Parameter(Mandatory = $false)]
	[string]$Configuration = "Debug",
	[Parameter(Mandatory = $false)]
	[string]$AvdName,
	[Parameter(Mandatory = $false)]
	[string]$SdkRoot
)

$ErrorActionPreference = "Stop"

function Resolve-ToolPath {
	param(
		[Parameter(Mandatory = $true)]
		[string]$PreferredPath,
		[Parameter(Mandatory = $true)]
		[string]$CommandName
	)

	if (Test-Path $PreferredPath) {
		return (Resolve-Path $PreferredPath).Path
	}

	$command = Get-Command $CommandName -ErrorAction SilentlyContinue
	if ($command -and $command.Path) {
		return $command.Path
	}

	throw "Tool '$CommandName' not found. Install Android SDK / platform-tools and retry."
}

function Get-RunningEmulatorSerial {
	param(
		[Parameter(Mandatory = $true)]
		[string]$AdbExe
	)

	$lines = & $AdbExe devices
	foreach ($line in $lines) {
		if ($line -match "^(emulator-\d+)\s+(device|offline)$") {
			return $Matches[1]
		}
	}

	return $null
}

function Wait-EmulatorAppears {
	param(
		[Parameter(Mandatory = $true)]
		[string]$AdbExe,
		[Parameter(Mandatory = $false)]
		[int]$TimeoutSeconds = 300
	)

	$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
	do {
		$serial = Get-RunningEmulatorSerial -AdbExe $AdbExe
		if (-not [string]::IsNullOrWhiteSpace($serial)) {
			return $serial
		}

		Start-Sleep -Seconds 2
	} while ((Get-Date) -lt $deadline)

	return $null
}

function Wait-EmulatorBootCompleted {
	param(
		[Parameter(Mandatory = $true)]
		[string]$AdbExe,
		[Parameter(Mandatory = $true)]
		[string]$Serial,
		[Parameter(Mandatory = $false)]
		[int]$TimeoutSeconds = 300
	)

	& $AdbExe -s $Serial wait-for-device | Out-Null

	$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
	do {
		$bootCompleted = (& $AdbExe -s $Serial shell getprop sys.boot_completed).Trim()
		if ($bootCompleted -eq "1") {
			return
		}

		Start-Sleep -Seconds 2
	} while ((Get-Date) -lt $deadline)

	throw "Emulator '$Serial' did not finish booting in $TimeoutSeconds seconds."
}

function Stop-CleanAndroidProcesses {
	param(
		[Parameter(Mandatory = $true)]
		[string]$AdbExe
	)

	Write-Host "Stopping stale Android/emulator processes..."

	try {
		& $AdbExe kill-server | Out-Null
	}
	catch {
	}

	$processNames = @(
		"emulator",
		"qemu-system-x86_64",
		"qemu-system-x86_64-headless",
		"qemu-system-i386",
		"qemu-system-aarch64",
		"adb"
	)

	foreach ($processName in $processNames) {
		try {
			Get-Process -Name $processName -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
		}
		catch {
		}
	}

	Start-Sleep -Seconds 2

	try {
		& $AdbExe start-server | Out-Null
	}
	catch {
	}
}

if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
	$scriptRoot = $PSScriptRoot
	if ([string]::IsNullOrWhiteSpace($scriptRoot)) {
		$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
	}

	$ProjectPath = Join-Path $scriptRoot "..\MeetSpace.Mobile.csproj"
}

$projectFullPath = (Resolve-Path $ProjectPath).Path

if ([string]::IsNullOrWhiteSpace($SdkRoot)) {
	if (Test-Path "C:\Emulator") {
		$SdkRoot = "C:\Emulator"
	}
	else {
		$SdkRoot = "$env:LOCALAPPDATA\Android\Sdk"
	}
}

$SdkRoot = (Resolve-Path $SdkRoot).Path

$emulatorExe = Resolve-ToolPath `
	-PreferredPath (Join-Path $SdkRoot "emulator\emulator.exe") `
	-CommandName "emulator"

$adbExe = Resolve-ToolPath `
	-PreferredPath (Join-Path $SdkRoot "platform-tools\adb.exe") `
	-CommandName "adb"

$availableAvds = & $emulatorExe -list-avds
if (-not $availableAvds -or $availableAvds.Count -eq 0) {
	throw "No AVD found. Create one in Android Device Manager first."
}

if ([string]::IsNullOrWhiteSpace($AvdName)) {
	$preferredAvd = $availableAvds | Where-Object { $_ -eq "MeetSpace_API35" } | Select-Object -First 1
	if ($preferredAvd) {
		$AvdName = $preferredAvd
	}
	else {
		$AvdName = $availableAvds[0]
	}
}

if (-not ($availableAvds -contains $AvdName)) {
	throw "AVD '$AvdName' not found. Available: $($availableAvds -join ', ')"
}

Write-Host "SDK root: $SdkRoot"
Write-Host "Emulator: $emulatorExe"
Write-Host "ADB: $adbExe"

Stop-CleanAndroidProcesses -AdbExe $adbExe

Write-Host "Starting Android emulator: $AvdName"
Start-Process -FilePath $emulatorExe -ArgumentList @("-avd", $AvdName, "-netdelay", "none", "-netspeed", "full") | Out-Null

$serial = Wait-EmulatorAppears -AdbExe $adbExe -TimeoutSeconds 300
if ([string]::IsNullOrWhiteSpace($serial)) {
	throw "Emulator did not appear in adb. Check virtualization/Hyper-V and retry."
}

Write-Host "Waiting for emulator boot completion ($serial)..."
Wait-EmulatorBootCompleted -AdbExe $adbExe -Serial $serial -TimeoutSeconds 300

try {
	& $adbExe -s $serial shell input keyevent 82 | Out-Null
}
catch {
}

Write-Host "Launching MAUI app on emulator..."
$requiredAndroidJar = Join-Path $SdkRoot "platforms\android-36\android.jar"
if (-not (Test-Path $requiredAndroidJar)) {
	Write-Host "Android API 36 is missing. Installing Android dependencies..."
	$installArgs = @(
		"build",
		$projectFullPath,
		"-t:InstallAndroidDependencies",
		"-f", "net10.0-android",
		"-p:AndroidSdkDirectory=$SdkRoot",
		"-p:AcceptAndroidSDKLicenses=true"
	)

	& dotnet @installArgs
	if ($LASTEXITCODE -ne 0) {
		throw "Failed to install Android dependencies. Exit code $LASTEXITCODE."
	}
}

$runArgs = @(
	"build",
	$projectFullPath,
	"-t:Run",
	"-f", "net10.0-android",
	"-c", $Configuration,
	"-p:AndroidSdkDirectory=$SdkRoot",
	"-p:AdbTarget=-s $serial"
)

& dotnet @runArgs
if ($LASTEXITCODE -ne 0) {
	throw "dotnet build -t:Run failed with exit code $LASTEXITCODE."
}

Write-Host ""
Write-Host "Done: emulator '$AvdName' ($serial) and app are running."
