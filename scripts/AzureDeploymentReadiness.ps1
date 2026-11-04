. "$PSScriptRoot\DeploymentReadiness.ps1"

function Get-AzureCutoverState {
    param([string]$Scm, [hashtable]$Headers, [string]$DeploymentId, [int]$TimeoutSeconds = 15)
    $clock = [Diagnostics.Stopwatch]::StartNew()
    if ($DeploymentId -notmatch '^[a-fA-F0-9]+$') { throw 'Invalid deployment identifier.' }
    $deployment = Invoke-RestMethod "$Scm/api/deployments/$DeploymentId" -Headers $Headers -TimeoutSec (Get-ReadinessTimeout $clock $TimeoutSeconds)
    if ($deployment.status -ne 4 -or !$deployment.complete -or !$deployment.active) { return $deployment }
    $script = @'
$ErrorActionPreference='Stop'
$base='C:\home\site\wwwroot'
foreach($path in @('C:\home\data\SitePackages\packagename.txt',"$base\TeamsGeneratorWebAPI.runtimeconfig.json","$base\TeamsGeneratorWebAPI.dll")) {
 if (!(Test-Path -LiteralPath $path)) { @{ready=$false;reason='mount_pending'} | ConvertTo-Json -Compress; return }
}
$pointer=[string](Get-Content 'C:\home\data\SitePackages\packagename.txt' -Raw)
$pointer=$pointer.Trim()
if (!$pointer) { @{ready=$false;reason='mount_pending'} | ConvertTo-Json -Compress; return }
if ($pointer -notmatch '^[a-zA-Z0-9._-]+\.zip$') { throw 'Invalid active package pointer.' }
if (!(Test-Path -LiteralPath "C:\home\data\SitePackages\$pointer")) { @{ready=$false;reason='mount_pending'} | ConvertTo-Json -Compress; return }
$runtime=(Get-Content "$base\TeamsGeneratorWebAPI.runtimeconfig.json" -Raw | ConvertFrom-Json).runtimeOptions
@{
 packageSha256=(Get-FileHash "C:\home\data\SitePackages\$pointer" -Algorithm SHA256).Hash.ToLowerInvariant()
 assemblySha256=(Get-FileHash "$base\TeamsGeneratorWebAPI.dll" -Algorithm SHA256).Hash.ToLowerInvariant()
 tfm=$runtime.tfm;rollForward=$runtime.rollForward
} | ConvertTo-Json -Compress
'@
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($script))
    $result = Invoke-RestMethod "$Scm/api/command" -Method Post -Headers $Headers -ContentType application/json -TimeoutSec (Get-ReadinessTimeout $clock $TimeoutSeconds) -Body (@{
        command="powershell.exe -NoProfile -NonInteractive -EncodedCommand $encoded";dir='C:\home\site\wwwroot'
    } | ConvertTo-Json -Compress)
    if ($result.ExitCode -ne 0) { throw 'Read-only package inspection failed; no success may be inferred.' }
    $files = $result.Output | ConvertFrom-Json
    [pscustomobject]@{
        id=$deployment.id;status=$deployment.status;complete=$deployment.complete;active=$deployment.active
        packageSha256=$files.packageSha256;assemblySha256=$files.assemblySha256;tfm=$files.tfm;rollForward=$files.rollForward
    }
}

function Get-ApplicationRevisionHealth {
    param([string]$Origin, [int]$TimeoutSeconds = 15)
    $response = Invoke-WebRequest "$Origin/health?readiness=$([guid]::NewGuid().ToString('N'))" -TimeoutSec $TimeoutSeconds -MaximumRedirection 0 -SkipHttpErrorCheck -Headers @{
        'Cache-Control'='no-cache, no-store';'Pragma'='no-cache'
    }
    $healthy = $false
    if ($response.StatusCode -eq 200 -and $response.Headers.'Content-Type' -match 'application/json') {
        $body = $response.Content | ConvertFrom-Json
        $healthy = $body.status -ceq 'healthy'
    }
    [pscustomobject]@{
        status=[int]$response.StatusCode;redirected=[bool]$response.Headers.Location;healthy=$healthy
        revision=[string]($response.Headers.'X-Teamify-Revision' | Select-Object -First 1)
        runtime=[string]($response.Headers.'X-Teamify-Runtime' | Select-Object -First 1)
        noStore=[bool]($response.Headers.'Cache-Control' -match 'no-store')
    }
}

function Get-AzureControlPlaneState {
    param([string]$Scm, [hashtable]$KuduHeaders, [string]$Site, [string]$Api, [hashtable]$ArmHeaders, [int]$TimeoutSeconds = 30)
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $web = Invoke-RestMethod "$Site/config/web$Api" -Headers $ArmHeaders -TimeoutSec (Get-ReadinessTimeout $clock $TimeoutSeconds)
    $all = @(Invoke-RestMethod "$Scm/api/deployments" -Headers $KuduHeaders -TimeoutSec (Get-ReadinessTimeout $clock $TimeoutSeconds) | ForEach-Object { $_ })
    $processes = @(Invoke-RestMethod "$Scm/api/processes" -Headers $KuduHeaders -TimeoutSec (Get-ReadinessTimeout $clock $TimeoutSeconds) | ForEach-Object { $_ } | Where-Object name -eq 'w3wp')
    # Kudu's process list omits is_scm_site; only the individual detail includes it.
    $scmProcesses = @(
        foreach ($candidate in $processes) {
            $detail = Invoke-RestMethod "$Scm/api/processes/$($candidate.id)" -Headers $KuduHeaders -TimeoutSec (Get-ReadinessTimeout $clock $TimeoutSeconds)
            if ($detail.is_scm_site -eq $true) { $detail }
        }
    )
    $identity = ''
    if ($scmProcesses.Count -eq 1) {
        $process = $scmProcesses[0]
        if ($process.start_time) { $identity = "$($process.id):$($process.start_time)" }
    }
    [pscustomobject]@{
        framework=$web.properties.netFrameworkVersion;scmIdentity=$identity
        pending=[bool]@($all | Where-Object { !$_.complete -or $_.status -in @(0,1,2) }).Count
    }
}

function Get-CompiledApiRevision {
    param([Parameter(Mandatory)][string]$AssemblyPath)
    (Get-CompiledApiIdentity $AssemblyPath).revision
}

function Get-CompiledApiIdentity {
    param([string]$AssemblyPath, [IO.Stream]$Stream)
    if ($AssemblyPath) { $Stream = [IO.File]::OpenRead($AssemblyPath) }
    if (!$Stream) { throw 'Compiled API bytes are required.' }
    $reader = [System.Reflection.PortableExecutable.PEReader]::new($Stream)
    try {
        $metadata = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($reader)
        $tokens = @()
        foreach ($handle in $metadata.GetAssemblyDefinition().GetCustomAttributes()) {
            $attribute = $metadata.GetCustomAttribute($handle)
            if ($attribute.Constructor.Kind -ne [Reflection.Metadata.HandleKind]::MemberReference) { continue }
            $member = $metadata.GetMemberReference([Reflection.Metadata.MemberReferenceHandle]$attribute.Constructor)
            if ($member.Parent.Kind -ne [Reflection.Metadata.HandleKind]::TypeReference) { continue }
            $type = $metadata.GetTypeReference([Reflection.Metadata.TypeReferenceHandle]$member.Parent)
            if ($metadata.GetString($type.Namespace) -cne 'System.Reflection' -or $metadata.GetString($type.Name) -cne 'AssemblyMetadataAttribute') { continue }
            $blob = $metadata.GetBlobReader($attribute.Value)
            if ($blob.ReadUInt16() -ne 1) { throw 'Invalid assembly metadata.' }
            if ($blob.ReadSerializedString() -ceq 'TeamifyDeploymentRevision') { $tokens += $blob.ReadSerializedString() }
        }
        if ($tokens.Count -ne 1 -or $tokens[0] -cnotmatch '^[a-f0-9]{32}$') { throw 'A fresh explicit immutable build token is required for deployable API identity.' }
        [pscustomobject]@{
            revision=$metadata.GetGuid($metadata.GetModuleDefinition().Mvid).ToString('N')
            buildToken=$tokens[0]
            assemblySha256=if($AssemblyPath){(Get-FileHash $AssemblyPath -Algorithm SHA256).Hash.ToLowerInvariant()}else{$null}
        }
    } finally { $reader.Dispose(); $Stream.Dispose() }
}

function Get-DeploymentPackageIdentity {
    param([Parameter(Mandatory)][string]$PackagePath)
    $zip = [IO.Compression.ZipFile]::OpenRead($PackagePath)
    try {
        $apiEntries = @($zip.Entries | Where-Object FullName -CEQ 'TeamsGeneratorWebAPI.dll')
        if ($apiEntries.Count -ne 1) { throw 'Package must contain exactly one root API assembly.' }
        $memory = [IO.MemoryStream]::new()
        $inputStream = $apiEntries[0].Open()
        try { $inputStream.CopyTo($memory) } finally { $inputStream.Dispose() }
        $bytes = $memory.ToArray()
        $memory.Position = 0
        $identity = Get-CompiledApiIdentity -Stream $memory
        $runtimeEntry = $zip.GetEntry('TeamsGeneratorWebAPI.runtimeconfig.json')
        if (!$runtimeEntry) { throw 'Root runtime configuration is required.' }
        $reader = [IO.StreamReader]::new($runtimeEntry.Open())
        try { $runtime = ($reader.ReadToEnd() | ConvertFrom-Json).runtimeOptions } finally { $reader.Dispose() }
        if ($runtime.tfm -cne 'net10.0' -or $runtime.rollForward) { throw 'Package runtime must be net10.0 without roll-forward overrides.' }
        $depsEntry = $zip.GetEntry('TeamsGeneratorWebAPI.deps.json')
        if (!$depsEntry) { throw 'Root API dependency manifest is required.' }
        $reader = [IO.StreamReader]::new($depsEntry.Open())
        try { $deps = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
        foreach ($name in @('Microsoft.AspNetCore.AzureAppServices.HostingStartup','Microsoft.AspNetCore.AzureAppServicesIntegration','Microsoft.Extensions.Logging.AzureAppServices')) {
            if (@($zip.Entries | Where-Object FullName -CEQ "$name.dll").Count -ne 1 -or
                "$name/10.0.12" -notin $deps.libraries.PSObject.Properties.Name) { throw 'Required aligned Azure startup dependency is missing from the package.' }
        }
        [pscustomobject]@{
            apiRevision=$identity.revision;buildToken=$identity.buildToken
            apiAssemblySha256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
            artifactSha256=(Get-FileHash $PackagePath).Hash.ToLowerInvariant()
        }
    } finally { $zip.Dispose() }
}
