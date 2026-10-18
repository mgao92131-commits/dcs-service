param([string]$Destination='')
$ErrorActionPreference='Stop'
$sourceRoot=[IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..\..'))
if($Destination -eq ''){$Destination=Join-Path $sourceRoot '.work\DcsRealtimeApiTest'}
$Destination=[IO.Path]::GetFullPath($Destination)
Push-Location $sourceRoot
try {
    if (-not (Test-Path -LiteralPath (Join-Path $sourceRoot 'bin\DcsDataService.exe'))) { throw 'Build the service before staging field tests.' }
    & '.\acceptance\realtime\build-field-tests.bat'
    if($LASTEXITCODE -ne 0){throw 'Field-test build failed.'}
    [void][IO.Directory]::CreateDirectory($Destination)
    [void][IO.Directory]::CreateDirectory((Join-Path $Destination 'bin'))
    $binaryNames=@('DcsDataService.exe')
    [void][IO.Directory]::CreateDirectory((Join-Path $Destination 'test-tools'))
    foreach($tool in @('DcsRealtimeAcceptance.exe','DcsDataService.CoreTests.exe','DcsDataService.RealtimeTests.exe','DcsDataService.exe')) { Copy-Item -LiteralPath (Join-Path $sourceRoot ('test-tools\'+$tool)) -Destination (Join-Path $Destination 'test-tools') -Force }
    Get-ChildItem -LiteralPath (Join-Path $sourceRoot 'test-tools') -Filter 'DeltaV.Historian.*.dll' | ForEach-Object {Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $Destination 'test-tools') -Force}
    foreach($name in $binaryNames){Copy-Item -LiteralPath (Join-Path $sourceRoot ('bin\'+$name)) -Destination (Join-Path $Destination ('bin\'+$name)) -Force}
    foreach($directory in @('src','docs')) { Copy-Item -LiteralPath (Join-Path $sourceRoot $directory) -Destination $Destination -Recurse -Force }
    Get-ChildItem -LiteralPath (Join-Path $sourceRoot 'tests') -Recurse -File | Where-Object {$_.FullName -notmatch '[\\/]__pycache__[\\/]' -and $_.Extension -ne '.pyc'} | ForEach-Object {
        $relative=$_.FullName.Substring($sourceRoot.Length+1)
        $target=Join-Path $Destination $relative
        [void][IO.Directory]::CreateDirectory((Split-Path -Parent $target))
        Copy-Item -LiteralPath $_.FullName -Destination $target -Force
    }
    [void][IO.Directory]::CreateDirectory((Join-Path $Destination 'acceptance\realtime'))
    Get-ChildItem -LiteralPath (Join-Path $sourceRoot 'acceptance\realtime') -File | ForEach-Object {Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $Destination 'acceptance\realtime') -Force}
    Get-ChildItem -LiteralPath (Join-Path $sourceRoot 'acceptance\realtime') -Filter '*.cmd' | ForEach-Object {Copy-Item -LiteralPath $_.FullName -Destination $Destination -Force}
    foreach($name in @('build-net35-x86.bat','config.example.ini','README.md')){Copy-Item -LiteralPath (Join-Path $sourceRoot $name) -Destination $Destination -Force}
    [void][IO.Directory]::CreateDirectory((Join-Path $Destination 'skills'))
    foreach($relative in @('skills\dcs-data\SKILL.md','skills\dcs-data\config.json','skills\dcs-data\scripts\dcs.py')) {
        $target=Join-Path $Destination $relative
        [void][IO.Directory]::CreateDirectory((Split-Path -Parent $target))
        Copy-Item -LiteralPath (Join-Path $sourceRoot $relative) -Destination $target -Force
    }
    $guide=[IO.File]::ReadAllText((Join-Path $sourceRoot 'acceptance\realtime\FIELD-USAGE.zh-CN.txt'),[Text.Encoding]::UTF8)
    [IO.File]::WriteAllText((Join-Path $Destination '使用说明.txt'),$guide,(New-Object Text.UTF8Encoding($true)))
    $manifest=@{serviceVersion='1.3.0';builtAtUtc=[DateTime]::UtcNow.ToString('o');files=@{}}
    foreach($name in $binaryNames){$path=Join-Path $Destination ('bin\'+$name);$manifest.files[$name]=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash}
    [IO.File]::WriteAllText((Join-Path $Destination 'build-manifest.json'),($manifest | ConvertTo-Json -Depth 8),(New-Object Text.UTF8Encoding($false)))
    Write-Output "Package: $Destination"
} finally { Pop-Location }
