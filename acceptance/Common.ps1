$ErrorActionPreference = "Stop"

$AcceptanceDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ServiceRoot = Split-Path -Parent $AcceptanceDir
$SettingsPath = Join-Path $AcceptanceDir "acceptance-settings.ps1"

if (-not (Test-Path -LiteralPath $SettingsPath)) {
    throw "Settings not found: $SettingsPath"
}
. $SettingsPath

function Assert-Configured {
    $required = @(
        @{ Name = "HistoryTag"; Value = $HistoryTag },
        @{ Name = "EventServer"; Value = $EventServer }
    )
    foreach ($item in $required) {
        if ([String]::IsNullOrEmpty($item.Value)) {
            throw ($item.Name + " must be set in " + $SettingsPath)
        }
    }
    if ($ApiBind -ne "127.0.0.1") { throw "ApiBind must remain 127.0.0.1 for acceptance." }
}

function Get-RunDirectory {
    $pointer = Join-Path $AcceptanceDir "current-run.txt"
    if (-not (Test-Path -LiteralPath $pointer)) {
        throw "No acceptance run exists. Run RUN-00-PREPARE.bat first."
    }
    $path = [IO.File]::ReadAllText($pointer).Trim()
    if ([String]::IsNullOrEmpty($path) -or -not (Test-Path -LiteralPath $path)) {
        throw "Invalid current run directory: $path"
    }
    return $path
}

function Save-Text([string]$Path, [string]$Text) {
    [IO.File]::WriteAllText($Path, $Text, (New-Object Text.UTF8Encoding($false)))
}

function Get-Sha256([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    try {
        $sha = New-Object Security.Cryptography.SHA256Managed
        try { $hash = $sha.ComputeHash($stream) } finally { $sha.Clear() }
    } finally { $stream.Close() }
    return ([BitConverter]::ToString($hash)).Replace("-", "").ToLowerInvariant()
}

function Invoke-HttpJson([string]$Method, [string]$Path, [string]$Body, [string]$OutputPath) {
    $url = "http://" + $ApiBind + ":" + $ApiPort + $Path
    $request = [Net.HttpWebRequest]::Create($url)
    $request.Method = $Method
    $request.ContentType = "application/json"
    $request.Timeout = 65000
    $request.ReadWriteTimeout = 65000
    if ($Method -eq "POST") {
        $bytes = [Text.Encoding]::UTF8.GetBytes($Body)
        $request.ContentLength = $bytes.Length
        $stream = $request.GetRequestStream()
        try { $stream.Write($bytes, 0, $bytes.Length) } finally { $stream.Close() }
    }
    try {
        $response = $request.GetResponse()
        $status = [int]$response.StatusCode
        $reader = New-Object IO.StreamReader($response.GetResponseStream())
        try { $text = $reader.ReadToEnd() } finally { $reader.Close(); $response.Close() }
    } catch [Net.WebException] {
        if ($_.Exception.Response -eq $null) { throw }
        $response = $_.Exception.Response
        $status = [int]$response.StatusCode
        $reader = New-Object IO.StreamReader($response.GetResponseStream())
        try { $text = $reader.ReadToEnd() } finally { $reader.Close(); $response.Close() }
    }
    Save-Text $OutputPath $text
    return @{ Status = $status; Text = $text }
}

function Invoke-HttpCsv([string]$Path, [string]$OutputPath) {
    $url = "http://" + $ApiBind + ":" + $ApiPort + $Path
    $request = [Net.HttpWebRequest]::Create($url); $request.Method = "GET"; $request.Timeout = 65000; $request.ReadWriteTimeout = 65000
    try {
        $response = $request.GetResponse(); $status = [int]$response.StatusCode; $headers = $response.Headers
        $reader = New-Object IO.StreamReader($response.GetResponseStream())
        try { $text = $reader.ReadToEnd() } finally { $reader.Close(); $response.Close() }
    } catch [Net.WebException] {
        if ($_.Exception.Response -eq $null) { throw }; $response = $_.Exception.Response; $status = [int]$response.StatusCode; $headers = $response.Headers
        $reader = New-Object IO.StreamReader($response.GetResponseStream()); try { $text = $reader.ReadToEnd() } finally { $reader.Close(); $response.Close() }
    }
    Save-Text $OutputPath $text
    return @{ Status = $status; Text = $text; Headers = $headers }
}

function Read-Json([string]$Path) {
    [Reflection.Assembly]::LoadWithPartialName("System.Web.Extensions") | Out-Null
    $serializer = New-Object Web.Script.Serialization.JavaScriptSerializer
    $serializer.MaxJsonLength = 67108864
    $serializer.RecursionLimit = 10000
    return $serializer.DeserializeObject([IO.File]::ReadAllText($Path))
}

function Quote-Json([string]$Value) {
    if ($Value -eq $null) { return "null" }
    return '"' + $Value.Replace('\', '\\').Replace('"', '\"') + '"'
}

function Convert-SourceToRawUtcText([string]$Value) {
    $source = [DateTime]::ParseExact($Value, "yyyy-MM-dd HH:mm:ss", [Globalization.CultureInfo]::InvariantCulture)
    $source = [DateTime]::SpecifyKind($source, [DateTimeKind]::Unspecified)
    $zone = [TimeZoneInfo]::FindSystemTimeZoneById($SourceTimeZone)
    return [TimeZoneInfo]::ConvertTimeToUtc($source, $zone).ToString("yyyy-MM-dd HH:mm:ss", [Globalization.CultureInfo]::InvariantCulture)
}

function Convert-SourceCursorToRawUtcText([string]$Value) {
    $source = [DateTime]::ParseExact($Value, "yyyy-MM-ddTHH:mm:ss.fff", [Globalization.CultureInfo]::InvariantCulture)
    $source = [DateTime]::SpecifyKind($source, [DateTimeKind]::Unspecified)
    $zone = [TimeZoneInfo]::FindSystemTimeZoneById($SourceTimeZone)
    return [TimeZoneInfo]::ConvertTimeToUtc($source, $zone).ToString("yyyy-MM-ddTHH:mm:ss.fff", [Globalization.CultureInfo]::InvariantCulture)
}

function Require-Http200($Result, [string]$Operation) {
    if ($Result.Status -ne 200) {
        throw ($Operation + " returned HTTP " + $Result.Status + ": " + $Result.Text)
    }
}

function Assert-ServiceRunning {
    $healthFile = Join-Path $env:TEMP ("dcs-service-health-" + [Guid]::NewGuid().ToString("N") + ".json")
    try {
        $health = Invoke-HttpJson "GET" "/health" "" $healthFile
        Require-Http200 $health "service health"
    } catch {
        throw "DcsDataService is not listening. Run RUN-02-START-SERVICE.bat first."
    } finally {
        if (Test-Path -LiteralPath $healthFile) { Remove-Item -LiteralPath $healthFile -Force }
    }
}
