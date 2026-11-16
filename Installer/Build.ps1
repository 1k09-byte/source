param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Platform = "x64",
    [bool]$Clean = $true
)

$ErrorActionPreference = "Stop"
$projectDir = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $projectDir "publish\win-x64"
$issFile = Join-Path $PSScriptRoot "kaliteConfig.iss"
$redistDir = Join-Path $PSScriptRoot "redist"

function Ensure-Prerequisite {
    param([string]$Name, [string]$Url)

    $target = Join-Path $redistDir $Name
    if (Test-Path -LiteralPath $target) { return $target }

    if (-not (Test-Path -LiteralPath $redistDir)) {
        New-Item -ItemType Directory -Path $redistDir -Force | Out-Null
    }

    Write-Host "--> downloading prerequisite $Name ..."
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $oldProgress = $ProgressPreference
    $ProgressPreference = "SilentlyContinue"
    try {
        Invoke-WebRequest -Uri $Url -OutFile $target -UseBasicParsing
    }
    finally {
        $ProgressPreference = $oldProgress
    }

    $signature = Get-AuthenticodeSignature -LiteralPath $target
    if ($signature.Status -ne "Valid" -or $signature.SignerCertificate.Subject -notmatch "Microsoft") {
        Remove-Item -LiteralPath $target -Force -ErrorAction SilentlyContinue
        throw "Prerequisite $Name is not validly signed by Microsoft."
    }
    return $target
}

if ($Clean) {
    Write-Host "--> clean: wiping publish output and obj/ intermediates..."
    if (Test-Path -LiteralPath $publishDir) {
        Remove-Item -LiteralPath $publishDir -Recurse -Force
    }
    $objDir = Join-Path $projectDir "obj"
    if (Test-Path -LiteralPath $objDir) {
        Remove-Item -LiteralPath $objDir -Recurse -Force
    }
}

$csprojPath = Join-Path $projectDir "src\kaliteConfig\kaliteConfig.csproj"
$version = & dotnet msbuild $csprojPath -getProperty:Version
$version = ($version | Select-Object -First 1)
if (-not $version) { $version = "0.1.0" }
$version = ($version -split '\.')[0..3] -join '.'
Write-Host "Full build v$version ($Configuration/$Runtime)..."

Write-Host "--> preparing installer prerequisites..."
Ensure-Prerequisite -Name "VC_redist.x64.exe" -Url "https://aka.ms/vs/17/release/vc_redist.x64.exe" | Out-Null
Ensure-Prerequisite -Name "MicrosoftEdgeWebView2Setup.exe" -Url "https://go.microsoft.com/fwlink/p/?LinkId=2124703" | Out-Null

Write-Host "--> dotnet publish (full flavor)..."
& dotnet publish $csprojPath `
    -c $Configuration -r $Runtime --self-contained true `
    -p:Platform=$Platform `
    -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

$exe = Join-Path $publishDir "kaliteConfig.exe"
if (-not (Test-Path -LiteralPath $exe)) { throw "Publish output missing: $exe" }

$publishedVersion = (Get-Item -LiteralPath $exe).VersionInfo.FileVersion
if ($publishedVersion) { $publishedVersion = $publishedVersion.Trim() }
if ($publishedVersion -ne $version) {
    throw "Published exe reports '$publishedVersion' but this build is '$version'. Refusing to package a mismatched installer."
}

$isccCmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
$iscc = if ($isccCmd) { $isccCmd.Source } else { $null }
foreach ($candidate in @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe")) {
    if (-not $iscc -and (Test-Path -LiteralPath $candidate)) { $iscc = $candidate; break }
}
if (-not $iscc) {
    throw "ISCC.exe not found. Install Inno Setup 6, then re-run. Publish output is ready at: $publishDir"
}

Write-Host "--> ISCC $issFile ..."
$stagingDir = Join-Path $env:TEMP "kaliteConfig-setup-staging"
if (Test-Path -LiteralPath $stagingDir) { Remove-Item -LiteralPath $stagingDir -Recurse -Force }
New-Item -ItemType Directory -Path $stagingDir -Force | Out-Null
$isccDefines = @("/DPublishDir=$publishDir", "/DMyAppVersion=$version", "/O$stagingDir")
& $iscc @isccDefines $issFile
if ($LASTEXITCODE -ne 0) { throw "ISCC failed." }

$staged = Join-Path $stagingDir "kaliteConfig-Setup-$version.exe"
if (-not (Test-Path -LiteralPath $staged)) { throw "Installer missing after ISCC: $staged" }

$setupVersion = (Get-Item -LiteralPath $staged).VersionInfo.FileVersion
if ($setupVersion) { $setupVersion = $setupVersion.Trim() }
if ($setupVersion -ne $version) {
    throw "Installer reports '$setupVersion' but this build is '$version'. Refusing to ship a mislabelled setup."
}
$distDir = Join-Path $projectDir "dist"
if (-not (Test-Path -LiteralPath $distDir)) { New-Item -ItemType Directory -Path $distDir -Force | Out-Null }
$setup = Join-Path $distDir "kaliteConfig-Setup-$version.exe"
Move-Item -LiteralPath $staged -Destination $setup -Force
if (-not (Test-Path -LiteralPath $setup)) { throw "Installer missing after ISCC: $setup" }
Write-Host "OK: $setup ($([math]::Round((Get-Item $setup).Length / 1MB, 1)) MB)"
