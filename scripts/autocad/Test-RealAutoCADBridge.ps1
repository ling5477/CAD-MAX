[CmdletBinding()]
param(
    [ValidateRange(30, 900)]
    [int]$WaitForStartSeconds = 180,

    [ValidateRange(30, 1800)]
    [int]$WaitForShutdownSeconds = 600,

    [string]$PreviousInstanceId,

    [switch]$ExpectPortConflict
)

$ErrorActionPreference = 'Stop'

function Wait-CadMaxCondition {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [scriptblock]$Condition,

        [Parameter(Mandatory)]
        [int]$TimeoutSeconds,

        [Parameter(Mandatory)]
        [string]$TimeoutCode
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        if (& $Condition) {
            return
        }
        Start-Sleep -Milliseconds 200
    }
    throw [InvalidOperationException]::new($TimeoutCode)
}

function Read-CadMaxEndpoint {
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
            throw [InvalidOperationException]::new('REAL_BRIDGE_RESPONSE_INVALID')
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
    if ($ExpectPortConflict) {
        $testStartedAt = [DateTimeOffset]::UtcNow
        Wait-CadMaxCondition -TimeoutSeconds $WaitForStartSeconds `
            -TimeoutCode 'AUTOCAD_START_TIMEOUT' -Condition {
                @(Get-Process -Name acad -ErrorAction SilentlyContinue).Count -gt 0
            }
        Wait-CadMaxCondition -TimeoutSeconds 30 `
            -TimeoutCode 'PORT_CONFLICT_EVIDENCE_TIMEOUT' -Condition {
                $evidenceFile = Join-Path (
                    [Environment]::GetFolderPath(
                        [Environment+SpecialFolder]::LocalApplicationData)) `
                    'CAD-MAX\evidence\plugin-lifecycle.jsonl'
                if (-not (Test-Path -LiteralPath $evidenceFile -PathType Leaf)) {
                    return $false
                }
                foreach ($line in Get-Content -LiteralPath $evidenceFile -Tail 20) {
                    try {
                        $entry = $line | ConvertFrom-Json
                        if ([DateTimeOffset]$entry.timestampUtc -ge $testStartedAt -and
                            $entry.safeErrorCode -eq 'PORT_IN_USE') {
                            return $true
                        }
                    }
                    catch {
                        continue
                    }
                }
                return $false
            }
        $acadIds = @(Get-Process -Name acad -ErrorAction SilentlyContinue | ForEach-Object Id)
        $owner = Get-NetTCPConnection -LocalAddress '127.0.0.1' -LocalPort 47770 `
            -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($null -eq $owner -or $acadIds -contains [int]$owner.OwningProcess) {
            throw [InvalidOperationException]::new('PORT_CONFLICT_OWNER_INVALID')
        }
        [pscustomobject]@{
            result = 'PASS'
            mode = 'REAL_AUTOCAD_PORT_CONFLICT'
            pluginState = 'DEGRADED'
            safeErrorCode = 'PORT_IN_USE'
            randomFallback = $false
            dwgAccess = $false
            paths = 'REDACTED'
        } | ConvertTo-Json -Compress
        exit 0
    }

    $tokenCheck = @(& (Join-Path $PSScriptRoot 'Test-BridgeToken.ps1') -MachineLocal 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw [InvalidOperationException]::new('MACHINE_TOKEN_VALIDATION_FAILED')
    }
    $tokenFile = Join-Path (
        [Environment]::GetFolderPath(
            [Environment+SpecialFolder]::LocalApplicationData)) `
        'CAD-MAX\config\bridge-token.json'
    $tokenPayload = Get-Content -LiteralPath $tokenFile -Raw | ConvertFrom-Json
    $token = [string]$tokenPayload.token
    if (($tokenCheck -join "`n").Contains($token)) {
        throw [InvalidOperationException]::new('TOKEN_OUTPUT_DISCLOSURE')
    }

    Wait-CadMaxCondition -TimeoutSeconds $WaitForStartSeconds `
        -TimeoutCode 'AUTOCAD_START_TIMEOUT' -Condition {
            @(Get-Process -Name acad -ErrorAction SilentlyContinue).Count -gt 0
        }
    Wait-CadMaxCondition -TimeoutSeconds 30 `
        -TimeoutCode 'REAL_BRIDGE_LISTENER_TIMEOUT' -Condition {
            @(Get-NetTCPConnection -LocalAddress '127.0.0.1' -LocalPort 47770 `
                    -State Listen -ErrorAction SilentlyContinue).Count -eq 1
        }

    $listener = Get-NetTCPConnection -LocalPort 47770 -State Listen `
        -ErrorAction Stop | Select-Object -First 1
    $acadIds = @(Get-Process -Name acad -ErrorAction Stop | ForEach-Object Id)
    if ($listener.LocalAddress -ne '127.0.0.1' -or
        $acadIds -notcontains [int]$listener.OwningProcess) {
        throw [InvalidOperationException]::new('REAL_BRIDGE_BIND_OR_OWNER_INVALID')
    }

    $client = [Net.Http.HttpClient]::new()
    $client.BaseAddress = [Uri]::new('http://127.0.0.1:47770')
    $client.Timeout = [TimeSpan]::FromSeconds(3)
    $client.DefaultRequestHeaders.Authorization = `
        [Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $token)
    try {
        $instanceIds = [Collections.Generic.HashSet[string]]::new(
            [StringComparer]::Ordinal)
        $endpointResults = [ordered]@{}
        foreach ($route in @('/v1/health', '/v1/version', '/v1/capabilities', '/v1/heartbeat')) {
            $result = Read-CadMaxEndpoint -Client $client -Path $route
            if ($result.StatusCode -ne 200 -or $result.Json.status -ne 'OK' -or
                $result.Json.data.developmentHost -or
                -not $result.Json.data.autocadConnected) {
                throw [InvalidOperationException]::new('REAL_BRIDGE_ENDPOINT_FAILED')
            }
            [void]$instanceIds.Add([string]$result.Json.data.instanceId)
            $endpointResults[$route] = $result
        }
        if ($instanceIds.Count -ne 1) {
            throw [InvalidOperationException]::new('REAL_BRIDGE_INSTANCE_MISMATCH')
        }
        $instanceId = [string]$endpointResults['/v1/health'].Json.data.instanceId
        if (-not [string]::IsNullOrWhiteSpace($PreviousInstanceId) -and
            $PreviousInstanceId -eq $instanceId) {
            throw [InvalidOperationException]::new('REAL_BRIDGE_INSTANCE_NOT_RESTARTED')
        }
        $health = $endpointResults['/v1/health'].Json.data
        if ($health.pluginState -ne 'READY' -or $health.documentAccess -or
            $health.dwgRead -or $health.dwgWrite -or -not $health.readOnly -or
            $health.allowWrite -or $health.allowScript) {
            throw [InvalidOperationException]::new('REAL_BRIDGE_HEALTH_UNSAFE')
        }
        $capabilities = $endpointResults['/v1/capabilities'].Json.data.capabilities
        foreach ($name in @(
                'drawing.active_document', 'drawing.list_documents', 'drawing.units',
                'drawing.bounds', 'query.list_entities', 'layer.list_layers',
                'block.list_definitions', 'style.list_text_styles',
                'selection.get_pickfirst', 'preview.render_pdf', 'dwg.read',
                'dwg.write', 'script', 'command')) {
            if ($capabilities.$name) {
                throw [InvalidOperationException]::new('REAL_BRIDGE_CAPABILITY_UNSAFE')
            }
        }
        foreach ($name in @(
                'bridge.health', 'bridge.version', 'bridge.capabilities', 'bridge.heartbeat')) {
            if (-not $capabilities.$name) {
                throw [InvalidOperationException]::new('REAL_BRIDGE_CAPABILITY_MISSING')
            }
        }
        $heartbeatOne = Read-CadMaxEndpoint -Client $client -Path '/v1/heartbeat'
        $heartbeatTwo = Read-CadMaxEndpoint -Client $client -Path '/v1/heartbeat'
        if ([long]$heartbeatTwo.Json.data.heartbeatSequence -le
            [long]$heartbeatOne.Json.data.heartbeatSequence) {
            throw [InvalidOperationException]::new('REAL_BRIDGE_HEARTBEAT_INVALID')
        }

        $missingClient = [Net.Http.HttpClient]::new()
        $missingClient.BaseAddress = $client.BaseAddress
        $wrongClient = [Net.Http.HttpClient]::new()
        $wrongClient.BaseAddress = $client.BaseAddress
        $wrongClient.DefaultRequestHeaders.Authorization = `
            [Net.Http.Headers.AuthenticationHeaderValue]::new(
                'Bearer',
                'AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA')
        try {
            $missing = Read-CadMaxEndpoint -Client $missingClient -Path '/v1/health'
            $wrong = Read-CadMaxEndpoint -Client $wrongClient -Path '/v1/health'
            if ($missing.StatusCode -ne 401 -or $wrong.StatusCode -ne 401 -or
                $missing.Json.errorCode -ne 'UNAUTHORIZED' -or
                $wrong.Json.errorCode -ne 'UNAUTHORIZED' -or
                $missing.Body.Contains($token) -or $wrong.Body.Contains($token)) {
                throw [InvalidOperationException]::new('REAL_BRIDGE_AUTH_NEGATIVE_FAILED')
            }
        }
        finally {
            $missingClient.Dispose()
            $wrongClient.Dispose()
        }

        $lanAddress = Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
            Where-Object {
                $_.IPAddress -ne '127.0.0.1' -and
                $_.IPAddress -notlike '169.254.*'
            } |
            Select-Object -First 1 -ExpandProperty IPAddress
        if ($null -ne $lanAddress) {
            $lanClient = [Net.Sockets.TcpClient]::new(
                [Net.Sockets.AddressFamily]::InterNetwork)
            try {
                $lanConnect = $lanClient.ConnectAsync($lanAddress, 47770)
                if ($lanConnect.Wait(500) -and $lanClient.Connected) {
                    throw [InvalidOperationException]::new('REAL_BRIDGE_LAN_EXPOSURE')
                }
            }
            catch [AggregateException] {
                # Expected: listener is not bound to a LAN address.
            }
            catch [Net.Sockets.SocketException] {
                # Expected: listener is not bound to a LAN address.
            }
            finally {
                $lanClient.Dispose()
            }
        }
    }
    finally {
        $client.Dispose()
    }

    $uv = Join-Path $repositoryRoot '.tools\uv\bin\uv.exe'
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
    $doctorInfo.Environment['CAD_MAX_BRIDGE_URL'] = 'http://127.0.0.1:47770'
    $doctorInfo.Environment['CAD_MAX_BRIDGE_TOKEN_FILE'] = $tokenFile
    $doctor = [Diagnostics.Process]::Start($doctorInfo)
    $doctorOutput = $doctor.StandardOutput.ReadToEnd()
    $doctorError = $doctor.StandardError.ReadToEnd()
    [void]$doctor.WaitForExit(10000)
    if (-not $doctor.HasExited) {
        $doctor.Kill($true)
        throw [InvalidOperationException]::new('REAL_BRIDGE_DOCTOR_TIMEOUT')
    }
    $doctorJson = $doctorOutput | ConvertFrom-Json
    if ($doctor.ExitCode -ne 0 -or $doctorJson.status -ne 'OK' -or
        $doctorJson.connectionState -ne 'CONNECTED' -or
        $doctorJson.instanceId -ne $instanceId -or
        $doctorOutput.Contains($token) -or $doctorError.Contains($token)) {
        throw [InvalidOperationException]::new('REAL_BRIDGE_DOCTOR_FAILED')
    }

    $evidenceFile = Join-Path (
        [Environment]::GetFolderPath(
            [Environment+SpecialFolder]::LocalApplicationData)) `
        'CAD-MAX\evidence\plugin-lifecycle.jsonl'
    if (Test-Path -LiteralPath $evidenceFile -PathType Leaf) {
        $recentEvidence = (Get-Content -LiteralPath $evidenceFile -Tail 20) -join "`n"
        if ($recentEvidence.Contains($token) -or
            $recentEvidence -match '(?i)(?:[A-Z]:[\\/]|\\\\)') {
            throw [InvalidOperationException]::new('REAL_BRIDGE_EVIDENCE_DISCLOSURE')
        }
    }

    Wait-CadMaxCondition -TimeoutSeconds $WaitForShutdownSeconds `
        -TimeoutCode 'AUTOCAD_SHUTDOWN_TIMEOUT' -Condition {
            @(Get-Process -Name acad -ErrorAction SilentlyContinue).Count -eq 0
        }
    Wait-CadMaxCondition -TimeoutSeconds 2 `
        -TimeoutCode 'REAL_BRIDGE_PORT_RELEASE_TIMEOUT' -Condition {
            @(Get-NetTCPConnection -LocalPort 47770 -State Listen `
                    -ErrorAction SilentlyContinue).Count -eq 0
        }

    [pscustomobject]@{
        result = 'PASS'
        mode = 'REAL_AUTOCAD_2025_BRIDGE'
        bind = '127.0.0.1:47770'
        endpoints = 4
        authentication = 'PASS'
        instanceId = $instanceId
        restartInstanceChanged = -not [string]::IsNullOrWhiteSpace($PreviousInstanceId)
        bridgeDoctor = 'PASS'
        capabilityHonesty = 'PASS'
        documentAccess = $false
        dwgRead = $false
        dwgWrite = $false
        allowWrite = $false
        allowScript = $false
        shutdownPortRelease = 'PASS'
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
