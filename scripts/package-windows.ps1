param(
    [Parameter(Mandatory = $true)]
    [string]$FfmpegArtifactDir,

    [Parameter(Mandatory = $true)]
    [string]$OutputDir
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-NativeText {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,

        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    # Windows PowerShell 5.1 can turn a native program's stderr output into
    # NativeCommandError records when ErrorActionPreference is Stop.
    # FFmpeg writes normal informational output such as -version/-buildconf
    # to stderr, so capture it under Continue and return the real exit code.
    $previousErrorActionPreference = $ErrorActionPreference

    try {
        $ErrorActionPreference = 'Continue'

        $lines = @(
            & $FilePath @Arguments 2>&1 |
                ForEach-Object { $_.ToString() }
        )

        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    [pscustomobject]@{
        ExitCode = $exitCode
        Text = ($lines -join [Environment]::NewLine).Trim()
    }
}

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$ffmpegArtifact = (Resolve-Path $FfmpegArtifactDir).Path
$ffmpegToolDir = Join-Path $ffmpegArtifact 'runtime\ffmpeg'
$ffmpegToolExe = Join-Path $ffmpegToolDir 'ffmpeg.exe'
$ffprobeToolExe = Join-Path $ffmpegToolDir 'ffprobe.exe'

if (-not (Test-Path -LiteralPath $ffmpegToolExe) -or -not (Test-Path -LiteralPath $ffprobeToolExe)) {
    throw 'Release-test FFmpeg directory does not contain ffmpeg.exe and ffprobe.exe.'
}
$output = [System.IO.Path]::GetFullPath($OutputDir)
$version = '1.0.0'
$packageName = 'Dynamic-Range-Analyzer-' + $version + '-win-x64'
$packageRoot = Join-Path $output $packageName
$publishDir = Join-Path $output '_publish'
$releaseDir = Join-Path $output 'release-artifacts'

Remove-Item -LiteralPath $packageRoot -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $publishDir -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $releaseDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $packageRoot, $publishDir, $releaseDir -Force | Out-Null

Push-Location $repo
try {
    # Run the portable suite against the exact FFmpeg/ffprobe runtime that will
    # be bundled in this release candidate, not against a machine-wide install.
    $originalPath = $env:PATH
    try {
        $env:PATH = $ffmpegToolDir + [System.IO.Path]::PathSeparator + $originalPath

        Write-Host 'Release-test FFmpeg directory:'
        Write-Host ('  ' + $ffmpegToolDir)
        & $ffmpegToolExe -hide_banner -version
        if ($LASTEXITCODE -ne 0) {
            throw 'Release-test ffmpeg.exe could not be executed.'
        }

        & $ffprobeToolExe -hide_banner -version
        if ($LASTEXITCODE -ne 0) {
            throw 'Release-test ffprobe.exe could not be executed.'
        }

        dotnet test '.\DRAnalyzer.Tests\DRAnalyzer.Tests.csproj' `
            --configuration Release `
            --filter 'Category!=ExternalReference'

        if ($LASTEXITCODE -ne 0) {
            throw 'Portable test suite failed.'
        }
    }
    finally {
        $env:PATH = $originalPath
    }

    dotnet publish '.\DRAnalyzer.App\DRAnalyzer.App.csproj' `
        --configuration Release `
        --runtime win-x64 `
        --self-contained true `
        -p:PublishSingleFile=false `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        --output $publishDir

    if ($LASTEXITCODE -ne 0) {
        throw 'dotnet publish failed.'
    }
}
finally {
    Pop-Location
}

Copy-Item -Path (Join-Path $publishDir '*') -Destination $packageRoot -Recurse -Force

$runtimeTarget = Join-Path $packageRoot 'runtime\ffmpeg'
New-Item -ItemType Directory -Path $runtimeTarget -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $ffmpegArtifact 'runtime\ffmpeg\ffmpeg.exe') -Destination $runtimeTarget -Force
Copy-Item -LiteralPath (Join-Path $ffmpegArtifact 'runtime\ffmpeg\ffprobe.exe') -Destination $runtimeTarget -Force

$licenseRoot = Join-Path $packageRoot 'licenses'
New-Item -ItemType Directory -Path $licenseRoot -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repo 'LICENSE') -Destination (Join-Path $packageRoot 'LICENSE.txt') -Force
Copy-Item -LiteralPath (Join-Path $repo 'README.md') -Destination $packageRoot -Force
Copy-Item -LiteralPath (Join-Path $repo 'docs\THIRD_PARTY.md') -Destination (Join-Path $packageRoot 'THIRD-PARTY-NOTICES.md') -Force
Copy-Item -LiteralPath (Join-Path $ffmpegArtifact 'licenses\ffmpeg') -Destination $licenseRoot -Recurse -Force
Copy-Item -LiteralPath (Join-Path $ffmpegArtifact 'FFMPEG-BUILD.txt') -Destination $packageRoot -Force

$dotnetCommand = Get-Command dotnet -ErrorAction Stop
$dotnetRoot = Split-Path $dotnetCommand.Source -Parent
$dotnetLicense = Join-Path $dotnetRoot 'LICENSE.txt'
$dotnetNotices = Join-Path $dotnetRoot 'ThirdPartyNotices.txt'
if (-not (Test-Path -LiteralPath $dotnetLicense) -or -not (Test-Path -LiteralPath $dotnetNotices)) {
    throw 'The .NET SDK license/ThirdPartyNotices files were not found beside dotnet.exe.'
}

$dotnetLicenseTarget = Join-Path $licenseRoot 'dotnet'
New-Item -ItemType Directory -Path $dotnetLicenseTarget -Force | Out-Null
Copy-Item -LiteralPath $dotnetLicense -Destination $dotnetLicenseTarget -Force
Copy-Item -LiteralPath $dotnetNotices -Destination $dotnetLicenseTarget -Force

$ffmpegExe = Join-Path $runtimeTarget 'ffmpeg.exe'
$ffprobeExe = Join-Path $runtimeTarget 'ffprobe.exe'

$ffmpegVersionResult =
    Invoke-NativeText `
        -FilePath $ffmpegExe `
        -Arguments @('-version')

if ($ffmpegVersionResult.ExitCode -ne 0) {
    throw 'Bundled ffmpeg.exe could not be executed.'
}
$ffmpegVersion = $ffmpegVersionResult.Text

$ffmpegBuildConfResult =
    Invoke-NativeText `
        -FilePath $ffmpegExe `
        -Arguments @('-buildconf')

if ($ffmpegBuildConfResult.ExitCode -ne 0) {
    throw 'Bundled ffmpeg.exe build configuration could not be read.'
}
$ffmpegBuildConf = $ffmpegBuildConfResult.Text

$ffprobeVersionResult =
    Invoke-NativeText `
        -FilePath $ffprobeExe `
        -Arguments @('-version')

if ($ffprobeVersionResult.ExitCode -ne 0) {
    throw 'Bundled ffprobe.exe could not be executed.'
}
$ffprobeVersion = $ffprobeVersionResult.Text

if ($ffmpegBuildConf -match '--enable-gpl' -or $ffmpegBuildConf -match '--enable-nonfree') {
    throw 'FFmpeg release runtime unexpectedly enables GPL or nonfree components.'
}

$smokeOutput = Join-Path $output '_ffmpeg-smoke.f64le'
& $ffmpegExe `
    -hide_banner `
    -loglevel error `
    -f lavfi `
    -i 'sine=frequency=1000:duration=0.25' `
    -f f64le `
    -acodec pcm_f64le `
    -y `
    $smokeOutput

if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $smokeOutput)) {
    throw 'Bundled FFmpeg PCM smoke test failed.'
}
Remove-Item -LiteralPath $smokeOutput -Force

$buildInfo = @(
    'Dynamic Range Analyzer 1.0.0 - Windows x64 portable package',
    ('Repository commit: ' + $env:GITHUB_SHA),
    ('Build date UTC: ' + [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ')),
    ('dotnet SDK: ' + ((dotnet --version | Out-String).Trim())),
    '',
    'FFmpeg:',
    $ffmpegVersion,
    '',
    'ffprobe:',
    $ffprobeVersion,
    '',
    'FFmpeg build configuration:',
    $ffmpegBuildConf
)
Set-Content -LiteralPath (Join-Path $packageRoot 'BUILD-INFO.txt') -Value $buildInfo -Encoding UTF8

$zipPath = Join-Path $releaseDir ($packageName + '.zip')
Compress-Archive -Path $packageRoot -DestinationPath $zipPath -CompressionLevel Optimal -Force

$zipHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $zipPath).Hash.ToLowerInvariant()
$checksumPath = $zipPath + '.sha256'
Set-Content -LiteralPath $checksumPath -Value ($zipHash + '  ' + [System.IO.Path]::GetFileName($zipPath)) -Encoding ASCII

Copy-Item -LiteralPath (Join-Path $ffmpegArtifact 'source\ffmpeg-9.0.tar.xz') -Destination $releaseDir -Force
Copy-Item -LiteralPath (Join-Path $ffmpegArtifact 'source\ffmpeg-9.0.tar.xz.asc') -Destination $releaseDir -Force
Copy-Item -LiteralPath (Join-Path $ffmpegArtifact 'FFMPEG-BUILD.txt') -Destination $releaseDir -Force
Copy-Item -LiteralPath (Join-Path $ffmpegArtifact 'SHA256SUMS.txt') -Destination (Join-Path $releaseDir 'FFMPEG-SHA256SUMS.txt') -Force

Write-Host ('Release artifacts created in: ' + $releaseDir)
