param([string]$Origin = 'https://localhost:7127')
$ErrorActionPreference = 'Stop'
$originUri = [Uri]$Origin
if ($originUri.Scheme -ne 'https' -or $originUri.Host -notin @('localhost','127.0.0.1','[::1]') -or $originUri.UserInfo -or $originUri.Query -or $originUri.Fragment -or $originUri.AbsolutePath -ne '/') { throw 'Use a local HTTPS origin only.' }
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$apiDll = Join-Path $repoRoot 'xKsef.Api/bin/Debug/net10.0/xKsef.Api.dll'
$frontend = Join-Path $repoRoot 'xKsef.Web/dist'
if (!(Test-Path -LiteralPath $apiDll) -or !(Test-Path -LiteralPath (Join-Path $frontend 'index.html'))) { throw 'Build the API (Debug) and React before starting live tests.' }
$artifacts = Join-Path $repoRoot 'artifacts'
New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
$manifestPath = Join-Path $artifacts 'live-api-manifest.json'
if (Test-Path -LiteralPath $manifestPath) {
    $previous = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if (Get-Process -Id $previous.pid -ErrorAction SilentlyContinue) { throw 'Stop the API identified by the previous live manifest before starting another run.' }
}
$applicationName = 'xksef-live-' + [Guid]::NewGuid().ToString('N')
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:Ksef__Environment = 'TEST'
$env:BrowserFrontend__Path = $frontend
$env:ConnectionStrings__DefaultConnection = "Server=(LocalDb)\MSSQLLocalDB;Database=xKsef;Trusted_Connection=True;TrustServerCertificate=True;Application Name=$applicationName"
$env:Kestrel__Endpoints__HttpsLan__Url = $originUri.GetLeftPart([UriPartial]::Authority)
$env:Logging__LogLevel__Default = 'Warning'
$api = Start-Process dotnet -ArgumentList @(('"{0}"' -f $apiDll),'--urls',$Origin) -WorkingDirectory (Join-Path $repoRoot 'xKsef.Api') -WindowStyle Hidden -RedirectStandardOutput (Join-Path $artifacts 'safety-api.log') -RedirectStandardError (Join-Path $artifacts 'safety-api-error.log') -PassThru
try {
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    $ready = $false
    while ([DateTime]::UtcNow -lt $deadline) {
        if ($api.HasExited) { throw 'Live API exited during startup. Inspect the local API log.' }
        try { $ready = (Invoke-WebRequest -UseBasicParsing -Uri ($Origin.TrimEnd('/') + '/login') -TimeoutSec 1).StatusCode -eq 200 } catch { $ready = $false }
        if ($ready) { break }
        Start-Sleep -Milliseconds 100
    }
    if (!$ready) { throw 'Trusted local HTTPS did not become ready.' }
    @{ pid=$api.Id; origin=$originUri.GetLeftPart([UriPartial]::Authority); applicationName=$applicationName; database='xKsef'; dataSource='(LocalDb)\MSSQLLocalDB'; apiDll=$apiDll } | ConvertTo-Json | Set-Content -LiteralPath $manifestPath -Encoding UTF8
    Write-Output "Live API ready. PID: $($api.Id). Manifest: $manifestPath"
} catch { Stop-Process -Id $api.Id -Force -ErrorAction SilentlyContinue; throw }
