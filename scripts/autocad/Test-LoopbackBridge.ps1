[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$process = $null
$fixtureRoot = $null
$tokenFixtureRoot = $null
$httpClient = $null

function Wait-CadMaxPort {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [int]$Port,

        [Parameter(Mandatory)]
        [bool]$ExpectedListening,

        [int]$Attempts = 100
    )

    for ($attempt = 0; $attempt -lt $Attempts; $attempt++) {
        $client = [Net.Sockets.TcpClient]::new(
            [Net.Sockets.AddressFamily]::InterNetwork)
        $connected = $false
        try {
            $connect = $client.ConnectAsync([Net.IPAddress]::Loopback, $Port)
            $connected = $connect.Wait(100) -and $client.Connected
        }
        catch {
            $connected = $false
        }
        finally {
            $client.Dispose()
        }
        if ($connected -eq $ExpectedListening) {
            return
        }
        Start-Sleep -Milliseconds 50
    }
    throw [InvalidOperationException]::new(
        $(if ($ExpectedListening) { 'BRIDGE_START_TIMEOUT' } else { 'BRIDGE_PORT_RELEASE_TIMEOUT' }))
}

function Read-CadMaxHttpJson {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [Net.Http.HttpClient]$Client,

        [Parameter(Mandatory)]
        [string]$Path
    )

    $response = $Client.GetAsync($Path).GetAwaiter().GetResult()
    try {
        $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        try {
            $json = $body | ConvertFrom-Json
        }
        catch {
            throw [InvalidOperationException]::new('BRIDGE_RESPONSE_INVALID')
        }
        return [pscustomobject]@{
            StatusCode = [int]$response.StatusCode
            Json = $json
            Body = $body
        }
    }
    finally {
        $response.Dispose()
    }
}

try {
    . (Join-Path $PSScriptRoot 'AutoCAD.Common.ps1')
    $repositoryRoot = Get-CadMaxRepositoryRoot
    $dotnet = Get-CadMaxDotNetExecutable
    $hostProject = Join-Path $repositoryRoot `
        'src\dotnet\CadMax.Bridge.Host\CadMax.Bridge.Host.csproj'
    & $dotnet build $hostProject --configuration Release --no-restore --verbosity quiet
    if ($LASTEXITCODE -ne 0) {
        throw [InvalidOperationException]::new('DEVELOPMENT_HOST_BUILD_FAILED')
    }

    $fixtureRoot = Join-Path $repositoryRoot (
        ".tools\autocad\loopback-tests\$([Guid]::NewGuid().ToString('N'))")
    $tokenFixtureRoot = Join-Path $repositoryRoot (
        ".tools\autocad\bridge-token-tests\$([Guid]::NewGuid().ToString('N'))")
    [IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null
    [IO.Directory]::CreateDirectory($tokenFixtureRoot) | Out-Null
    $tokenFile = Join-Path $tokenFixtureRoot 'bridge-token.json'
    $powerShell = Get-CadMaxPowerShellExecutable
    $tokenOutput = @(& $powerShell -NoLogo -NoProfile -ExecutionPolicy Bypass `
            -File (Join-Path $PSScriptRoot 'New-BridgeToken.ps1') `
            -TokenFilePath $tokenFile 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw [InvalidOperationException]::new('LOOPBACK_TOKEN_CREATE_FAILED')
    }
    $tokenPayload = Get-Content -LiteralPath $tokenFile -Raw | ConvertFrom-Json
    $token = [string]$tokenPayload.token
    if (($tokenOutput -join "`n").Contains($token)) {
        throw [InvalidOperationException]::new('TOKEN_OUTPUT_DISCLOSURE')
    }

    $hostAssembly = Join-Path $repositoryRoot `
        'src\dotnet\CadMax.Bridge.Host\bin\Release\net8.0-windows\CadMax.Bridge.Host.dll'
    if (-not (Test-Path -LiteralPath $hostAssembly -PathType Leaf)) {
        throw [InvalidOperationException]::new('DEVELOPMENT_HOST_OUTPUT_MISSING')
    }
    $standardOutput = Join-Path $fixtureRoot 'host.stdout.log'
    $standardError = Join-Path $fixtureRoot 'host.stderr.log'
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $dotnet
    [void]$startInfo.ArgumentList.Add($hostAssembly)
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.Environment['CAD_MAX_BRIDGE_TOKEN_FILE'] = $tokenFile
    $startInfo.Environment['CAD_MAX_DEVELOPMENT_BRIDGE_URL'] = `
        'http://127.0.0.1:47779'
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    if (-not $process.Start()) {
        throw [InvalidOperationException]::new('DEVELOPMENT_HOST_START_FAILED')
    }
    $outputTask = $process.StandardOutput.ReadToEndAsync()
    $errorTask = $process.StandardError.ReadToEndAsync()
    Wait-CadMaxPort -Port 47779 -ExpectedListening $true

    $httpClient = [Net.Http.HttpClient]::new()
    $httpClient.BaseAddress = [Uri]::new('http://127.0.0.1:47779')
    $httpClient.Timeout = [TimeSpan]::FromSeconds(3)
    $httpClient.DefaultRequestHeaders.Authorization = `
        [Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $token)
    $instanceIds = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    foreach ($route in @('/v1/health', '/v1/version', '/v1/capabilities', '/v1/heartbeat')) {
        $result = Read-CadMaxHttpJson -Client $httpClient -Path $route
        if ($result.StatusCode -ne 200 -or $result.Json.status -ne 'OK' -or
            -not $result.Json.data.developmentHost -or
            $result.Json.data.autocadConnected) {
            throw [InvalidOperationException]::new('DEVELOPMENT_HOST_ENDPOINT_FAILED')
        }
        [void]$instanceIds.Add([string]$result.Json.data.instanceId)
    }
    if ($instanceIds.Count -ne 1) {
        throw [InvalidOperationException]::new('DEVELOPMENT_HOST_INSTANCE_MISMATCH')
    }

    $heartbeatOne = Read-CadMaxHttpJson -Client $httpClient -Path '/v1/heartbeat'
    $heartbeatTwo = Read-CadMaxHttpJson -Client $httpClient -Path '/v1/heartbeat'
    if ([long]$heartbeatTwo.Json.data.heartbeatSequence -le
        [long]$heartbeatOne.Json.data.heartbeatSequence) {
        throw [InvalidOperationException]::new('DEVELOPMENT_HOST_HEARTBEAT_INVALID')
    }

    $missingClient = [Net.Http.HttpClient]::new()
    $missingClient.BaseAddress = $httpClient.BaseAddress
    $wrongClient = [Net.Http.HttpClient]::new()
    $wrongClient.BaseAddress = $httpClient.BaseAddress
    $wrongClient.DefaultRequestHeaders.Authorization = `
        [Net.Http.Headers.AuthenticationHeaderValue]::new(
            'Bearer',
            'AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA')
    try {
        $missing = Read-CadMaxHttpJson -Client $missingClient -Path '/v1/health'
        $wrong = Read-CadMaxHttpJson -Client $wrongClient -Path '/v1/health'
        if ($missing.StatusCode -ne 401 -or $wrong.StatusCode -ne 401 -or
            $missing.Json.errorCode -ne 'UNAUTHORIZED' -or
            $wrong.Json.errorCode -ne 'UNAUTHORIZED' -or
            $missing.Body.Contains($token) -or $wrong.Body.Contains($token)) {
            throw [InvalidOperationException]::new('DEVELOPMENT_HOST_AUTH_NEGATIVE_FAILED')
        }
    }
    finally {
        $missingClient.Dispose()
        $wrongClient.Dispose()
    }

    $uv = Join-Path $repositoryRoot '.tools\uv\bin\uv.exe'
    if (-not (Test-Path -LiteralPath $uv -PathType Leaf)) {
        throw [InvalidOperationException]::new('UV_EXECUTABLE_MISSING')
    }
    $doctorInfo = [Diagnostics.ProcessStartInfo]::new()
    $doctorInfo.FileName = $uv
    [void]$doctorInfo.ArgumentList.Add('run')
    [void]$doctorInfo.ArgumentList.Add('cad-max-mcp')
    [void]$doctorInfo.ArgumentList.Add('bridge-doctor')
    $doctorInfo.WorkingDirectory = $repositoryRoot
    $doctorInfo.UseShellExecute = $false
    $doctorInfo.CreateNoWindow = $true
    $doctorInfo.RedirectStandardOutput = $true
    $doctorInfo.RedirectStandardError = $true
    $doctorInfo.Environment['CAD_MAX_BRIDGE_URL'] = 'http://127.0.0.1:47779'
    $doctorInfo.Environment['CAD_MAX_BRIDGE_TOKEN_FILE'] = $tokenFile
    $doctor = [Diagnostics.Process]::Start($doctorInfo)
    $doctorOutput = $doctor.StandardOutput.ReadToEnd()
    $doctorError = $doctor.StandardError.ReadToEnd()
    [void]$doctor.WaitForExit(10000)
    if (-not $doctor.HasExited) {
        $doctor.Kill($true)
        throw [InvalidOperationException]::new('BRIDGE_DOCTOR_TIMEOUT')
    }
    if ($doctor.ExitCode -eq 0 -or
        ($doctorOutput | ConvertFrom-Json).connectionState -ne 'DEVELOPMENT_HOST' -or
        $doctorOutput.Contains($token) -or $doctorError.Contains($token)) {
        throw [InvalidOperationException]::new('DEVELOPMENT_HOST_CLASSIFICATION_FAILED')
    }

    $httpClient.Dispose()
    $httpClient = $null
    $process.Kill($true)
    [void]$process.WaitForExit(5000)
    $hostOutput = $outputTask.GetAwaiter().GetResult()
    $hostError = $errorTask.GetAwaiter().GetResult()
    if ($hostOutput.Contains($token) -or $hostError.Contains($token)) {
        throw [InvalidOperationException]::new('DEVELOPMENT_HOST_TOKEN_LOGGED')
    }
    Wait-CadMaxPort -Port 47779 -ExpectedListening $false

    [pscustomobject]@{
        result = 'PASS'
        server = 'SDK_FREE_DEVELOPMENT_HOST'
        bind = '127.0.0.1:47779'
        endpoints = 4
        authentication = 'PASS'
        bridgeDoctor = 'DEVELOPMENT_HOST_REJECTED_AS_AUTOCAD'
        portRelease = 'PASS'
        token = 'REDACTED'
        paths = 'REDACTED'
    } | ConvertTo-Json -Compress
    exit 0
}
catch {
    if (Get-Command Write-CadMaxSafeError -ErrorAction SilentlyContinue) {
        Write-CadMaxSafeError -Exception $_.Exception
    }
    else {
        [Console]::Error.WriteLine('ERROR / INTERNAL_ERROR')
    }
    exit 1
}
finally {
    if ($null -ne $httpClient) {
        $httpClient.Dispose()
    }
    if ($null -ne $process) {
        try {
            if (-not $process.HasExited) {
                $process.Kill($true)
                [void]$process.WaitForExit(5000)
            }
        }
        catch {
            # Cleanup continues to exact fixture removal.
        }
        $process.Dispose()
    }
    foreach ($root in @($fixtureRoot, $tokenFixtureRoot)) {
        if ($null -ne $root -and (Test-Path -LiteralPath $root)) {
            $allowedRoot = if ($root -eq $fixtureRoot) {
                Join-Path (Get-CadMaxRepositoryRoot) '.tools\autocad\loopback-tests'
            }
            else {
                Join-Path (Get-CadMaxRepositoryRoot) '.tools\autocad\bridge-token-tests'
            }
            if (Test-CadMaxPathWithin -Candidate $root -Root $allowedRoot) {
                Remove-Item -LiteralPath $root -Recurse -Force
            }
        }
    }
}
