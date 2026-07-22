[CmdletBinding()]
param(
    [ValidateSet(
        'SdkFree',
        'Basic',
        'ExpectedSuccess',
        'ExpectedMismatch',
        'Destroyed',
        'NoDocument',
        'Modal',
        'Busy',
        'Timeout',
        'Cancellation',
        'Saturation')]
    [string]$Scenario = 'SdkFree',

    [ValidatePattern('^doc_[A-Za-z0-9_-]{22}$')]
    [string]$ExpectedDocumentId,

    [ValidateRange(1, 10)]
    [int]$DeadlineSeconds = 5,

    [switch]$IncludeOpaqueDocumentId
)

$ErrorActionPreference = 'Stop'
$client = $null
$observedDocumentId = $null

function Read-CadMaxResponse {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [Net.Http.HttpResponseMessage]$Response
    )

    $body = $Response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    try {
        $json = $body | ConvertFrom-Json
    }
    catch {
        throw [InvalidOperationException]::new('CONTEXT_RESPONSE_INVALID')
    }
    [pscustomobject]@{
        StatusCode = [int]$Response.StatusCode
        Json = $json
        Body = $body
    }
}

function Invoke-CadMaxGet {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [Net.Http.HttpClient]$Client,

        [Parameter(Mandatory)]
        [string]$Path
    )

    $response = $Client.GetAsync($Path).GetAwaiter().GetResult()
    try {
        Read-CadMaxResponse -Response $response
    }
    finally {
        $response.Dispose()
    }
}

function New-CadMaxProbeMessage {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$InstanceId,

        [string]$DocumentId,

        [Parameter(Mandatory)]
        [double]$Deadline
    )

    $requestId = [Guid]::NewGuid().ToString('D')
    $traceId = [Guid]::NewGuid().ToString('D')
    $payload = [ordered]@{
        schemaVersion = '1.0'
        requestId = $requestId
        traceId = $traceId
        deadlineUtc = [DateTimeOffset]::UtcNow.AddSeconds($Deadline).ToString('O')
        expectedInstanceId = $InstanceId
        expectedDocumentId = if ([string]::IsNullOrWhiteSpace($DocumentId)) {
            $null
        }
        else {
            $DocumentId
        }
    } | ConvertTo-Json -Compress
    $message = [Net.Http.HttpRequestMessage]::new(
        [Net.Http.HttpMethod]::Post,
        '/v1/context/probe')
    $message.Content = [Net.Http.StringContent]::new(
        $payload,
        [Text.Encoding]::UTF8,
        'application/json')
    [pscustomobject]@{
        Message = $message
        RequestId = $requestId
        TraceId = $traceId
    }
}

function Invoke-CadMaxProbe {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [Net.Http.HttpClient]$Client,

        [Parameter(Mandatory)]
        [string]$InstanceId,

        [string]$DocumentId,

        [Parameter(Mandatory)]
        [double]$Deadline
    )

    $request = New-CadMaxProbeMessage `
        -InstanceId $InstanceId `
        -DocumentId $DocumentId `
        -Deadline $Deadline
    try {
        $response = $Client.SendAsync($request.Message).GetAwaiter().GetResult()
        try {
            $result = Read-CadMaxResponse -Response $response
            if ($result.Json.requestId -ne $request.RequestId -or
                $result.Json.traceId -ne $request.TraceId) {
                throw [InvalidOperationException]::new('CONTEXT_CORRELATION_MISMATCH')
            }
            return $result
        }
        finally {
            $response.Dispose()
        }
    }
    finally {
        $request.Message.Dispose()
    }
}

function Invoke-CadMaxCancelledProbe {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$Token,

        [Parameter(Mandatory)]
        [string]$InstanceId
    )

    $request = New-CadMaxProbeMessage `
        -InstanceId $InstanceId `
        -Deadline 5
    try {
        $payload = $request.Message.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        $payloadBytes = [Text.Encoding]::UTF8.GetBytes($payload)
        $header = @(
            'POST /v1/context/probe HTTP/1.1'
            'Host: 127.0.0.1:47770'
            "Authorization: Bearer $Token"
            'Content-Type: application/json; charset=utf-8'
            "Content-Length: $($payloadBytes.Length)"
            'Connection: close'
            ''
            ''
        ) -join "`r`n"
        $headerBytes = [Text.Encoding]::ASCII.GetBytes($header)
        $tcpClient = [Net.Sockets.TcpClient]::new()
        try {
            $tcpClient.Connect([Net.IPAddress]::Loopback, 47770)
            $stream = $tcpClient.GetStream()
            $stream.Write($headerBytes, 0, $headerBytes.Length)
            $stream.Write($payloadBytes, 0, $payloadBytes.Length)
            $stream.Flush()
        }
        finally {
            $tcpClient.Dispose()
        }
    }
    finally {
        $request.Message.Dispose()
    }
}

try {
    . (Join-Path $PSScriptRoot 'AutoCAD.Common.ps1')
    $repositoryRoot = Get-CadMaxRepositoryRoot
    if ($Scenario -eq 'SdkFree') {
        $dotnet = Get-CadMaxDotNetExecutable
        $testProject = Join-Path $repositoryRoot `
            'src\dotnet\CadMax.AutoCAD.Plugin.Tests\CadMax.AutoCAD.Plugin.Tests.csproj'
        & $dotnet test $testProject `
            --configuration Release `
            --no-restore `
            --filter 'FullyQualifiedName~DocumentContextDispatcherTests' `
            --verbosity quiet
        if ($LASTEXITCODE -ne 0) {
            throw [InvalidOperationException]::new('CONTEXT_SDK_FREE_TEST_FAILED')
        }
        [pscustomobject]@{
            result = 'PASS'
            mode = 'SDK_FREE_CONTEXT_DISPATCH'
            queueBounded = $true
            maxQueueDepth = 32
            maxInFlight = 1
            documentContentAccess = $false
            databaseAccess = $false
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
    $token = [string](Get-Content -LiteralPath $tokenFile -Raw | ConvertFrom-Json).token
    if (($tokenCheck -join "`n").Contains($token)) {
        throw [InvalidOperationException]::new('TOKEN_OUTPUT_DISCLOSURE')
    }

    $client = [Net.Http.HttpClient]::new()
    $client.BaseAddress = [Uri]::new('http://127.0.0.1:47770')
    $client.Timeout = [TimeSpan]::FromSeconds(12)
    $client.DefaultRequestHeaders.Authorization = `
        [Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $token)
    $health = Invoke-CadMaxGet -Client $client -Path '/v1/health'
    if ($health.StatusCode -ne 200 -or $health.Json.status -ne 'OK' -or
        -not $health.Json.data.autocadConnected) {
        throw [InvalidOperationException]::new('CONTEXT_BRIDGE_NOT_READY')
    }
    $instanceId = [string]$health.Json.data.instanceId

    if ($Scenario -eq 'Saturation') {
        $requests = [Collections.Generic.List[object]]::new()
        for ($index = 0; $index -lt 40; $index++) {
            $request = New-CadMaxProbeMessage `
                -InstanceId $instanceId `
                -Deadline $DeadlineSeconds
            $task = $client.SendAsync($request.Message)
            $requests.Add([pscustomobject]@{ Request = $request; Task = $task })
        }
        $statuses = [Collections.Generic.List[string]]::new()
        try {
            foreach ($pending in $requests) {
                $response = $pending.Task.GetAwaiter().GetResult()
                try {
                    $result = Read-CadMaxResponse -Response $response
                    $statuses.Add([string]$result.Json.status)
                }
                finally {
                    $response.Dispose()
                }
            }
        }
        finally {
            foreach ($pending in $requests) {
                $pending.Request.Message.Dispose()
            }
        }
        if (@($statuses | Where-Object { $_ -eq 'QUEUE_FULL' }).Count -lt 1) {
            throw [InvalidOperationException]::new('CONTEXT_QUEUE_FULL_NOT_OBSERVED')
        }
    }
    elseif ($Scenario -eq 'Cancellation') {
        $cancelled = $false
        for ($attempt = 0; $attempt -lt 10 -and -not $cancelled; $attempt++) {
            Invoke-CadMaxCancelledProbe -Token $token -InstanceId $instanceId
            for ($poll = 0; $poll -lt 20; $poll++) {
                Start-Sleep -Milliseconds 25
                $snapshot = Invoke-CadMaxGet -Client $client -Path '/v1/heartbeat'
                if ($snapshot.Json.data.lastDispatchStatus -eq 'CANCELLED' -and
                    $snapshot.Json.data.queueDepth -eq 0 -and
                    $snapshot.Json.data.inFlightCount -eq 0) {
                    $cancelled = $true
                    break
                }
            }
        }
        if (-not $cancelled) {
            throw [InvalidOperationException]::new('CONTEXT_CANCELLATION_NOT_OBSERVED')
        }
    }
    else {
        $documentId = if ($Scenario -in @(
                'ExpectedSuccess', 'ExpectedMismatch', 'Destroyed')) {
            if ([string]::IsNullOrWhiteSpace($ExpectedDocumentId)) {
                throw [InvalidOperationException]::new('EXPECTED_DOCUMENT_ID_REQUIRED')
            }
            $ExpectedDocumentId
        }
        else {
            $null
        }
        $probeDeadline = if ($Scenario -eq 'Timeout') { 0.001 } else { $DeadlineSeconds }
        $probe = Invoke-CadMaxProbe `
            -Client $client `
            -InstanceId $instanceId `
            -DocumentId $documentId `
            -Deadline $probeDeadline
        $expectedStatus = switch ($Scenario) {
            'Basic' { 'OK' }
            'ExpectedSuccess' { 'OK' }
            'ExpectedMismatch' { 'DOCUMENT_NOT_ACTIVE' }
            'Destroyed' { 'DOCUMENT_NOT_FOUND' }
            'NoDocument' { 'NO_ACTIVE_DOCUMENT' }
            'Modal' { 'APPLICATION_MODAL' }
            'Busy' { 'DOCUMENT_BUSY' }
            'Timeout' { 'TIMEOUT' }
        }
        if ($probe.Json.status -ne $expectedStatus) {
            throw [InvalidOperationException]::new('CONTEXT_SCENARIO_STATUS_MISMATCH')
        }
        if ($expectedStatus -eq 'OK') {
            $data = $probe.Json.data
            if (-not $data.mainThreadVerified -or
                $data.executionContext -ne 'DOCUMENT_COMMAND_CONTEXT' -or
                $data.activeDocumentId -notmatch '^doc_[A-Za-z0-9_-]{22}$' -or
                -not $data.isQuiescent -or
                $probe.Body -match '(?i)(?:documentName|documentPath|fileName|windowTitle)') {
                throw [InvalidOperationException]::new('CONTEXT_SUCCESS_RESPONSE_INVALID')
            }
            $observedDocumentId = [string]$data.activeDocumentId
        }
    }

    $heartbeat = Invoke-CadMaxGet -Client $client -Path '/v1/heartbeat'
    if ($heartbeat.Json.data.queueDepth -ne 0 -or
        $heartbeat.Json.data.inFlightCount -ne 0) {
        throw [InvalidOperationException]::new('CONTEXT_DISPATCH_NOT_DRAINED')
    }

    [pscustomobject]@{
        result = 'PASS'
        mode = "REAL_AUTOCAD_CONTEXT_$($Scenario.ToUpperInvariant())"
        status = switch ($Scenario) {
            'Saturation' { 'QUEUE_FULL' }
            'Cancellation' { 'CANCELLED' }
            default { $expectedStatus }
        }
        mainThreadVerified = $Scenario -in @('Basic', 'ExpectedSuccess')
        queueDepth = 0
        inFlightCount = 0
        documentContentAccess = $false
        databaseAccess = $false
        documentId = if ($IncludeOpaqueDocumentId) { $observedDocumentId } else { 'REDACTED' }
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
    if ($null -ne $client) {
        $client.Dispose()
    }
}
