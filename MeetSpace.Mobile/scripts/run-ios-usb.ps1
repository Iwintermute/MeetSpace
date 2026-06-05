param(
	[Parameter(Mandatory = $true)]
	[string]$DeviceUdid,

	[Parameter(Mandatory = $false)]
	[string]$BundleId = "com.meetspace.mobile",

	[Parameter(Mandatory = $false)]
	[string]$ProjectPath,

	[Parameter(Mandatory = $false)]
	[string]$Configuration = "Debug"
)
if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
	$scriptRoot = $PSScriptRoot
	if ([string]::IsNullOrWhiteSpace($scriptRoot)) {
		$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
	}

	$ProjectPath = Join-Path $scriptRoot "..\MeetSpace.Mobile.csproj"
}

$projectFullPath = (Resolve-Path $ProjectPath).Path

dotnet build $projectFullPath `
	-t:Run `
	-f net10.0-ios `
	-c $Configuration `
	-p:RuntimeIdentifier=ios-arm64 `
	-p:_DeviceName=":v2:udid=$DeviceUdid" `
	-p:CodesignKey="Apple Development" `
	-p:CodesignProvision=Automatic `
	-p:ProvisioningType=automatic `
	-p:ApplicationId=$BundleId
