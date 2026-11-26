param(
    [ValidateSet('Release', 'Debug')][string]$Configuration = 'Release',
    [string]$QBrainExport,
    [ValidateSet('01', '02', '03')][string]$ThroughPhase = '01'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$productProjects = @(
    'src/RetroIde.Application'
    'src/RetroIde.Mcp'
    'src/RetroIde.Mcp.Stdio'
    'src/RetroIde.Workbench'
)
$nativeEntryPoints = @(
    'M:RetroIde.NativeTests.NativeDockingTests.RunAsync'
    'M:RetroIde.NativeTests.ProductionAttachmentTests.RunAsync'
    'M:RetroIde.NativeTests.RemoteControlDockingTests.RunAsync'
    'M:RetroIde.NativeTests.RemoteControlGitTests.RunAsync'
    'M:RetroIde.NativeTests.RemoteControlWorkflowTests.RunAsync'
)
$loaded = @{}
function Get-ProjectXml([string]$ProjectPath) {
    if (!$loaded.ContainsKey($ProjectPath)) {
        $name = Split-Path $ProjectPath -Leaf
        $path = Join-Path $root "$ProjectPath/bin/$Configuration/net10.0/$name.xml"
        [xml]$loaded[$ProjectPath] = Get-Content -LiteralPath $path -Raw
    }
    return $loaded[$ProjectPath]
}

function Get-PublicTypeName($Metadata, $Type) {
    $visibility = $Type.Attributes -band [System.Reflection.TypeAttributes]::VisibilityMask
    $name = $Metadata.GetString($Type.Name)
    if ($visibility -eq [System.Reflection.TypeAttributes]::Public) {
        return "$($Metadata.GetString($Type.Namespace)).$name"
    }
    if ($visibility -eq [System.Reflection.TypeAttributes]::NestedPublic) {
        $parent = Get-PublicTypeName $Metadata ($Metadata.GetTypeDefinition($Type.GetDeclaringType()))
        if ($parent) { return "$parent.$name" }
    }
}

function Get-CompiledTraceSymbols([string]$ProjectPath, [switch]$ManagedTests, [switch]$NativeTests) {
    $name = Split-Path $ProjectPath -Leaf
    $path = Join-Path $root "$ProjectPath/bin/$Configuration/net10.0/$name.dll"
    $stream = [System.IO.File]::OpenRead($path)
    $pe = [System.Reflection.PortableExecutable.PEReader]::new($stream)
    try {
        # Read metadata without loading application dependencies into PowerShell.
        $metadata = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
        foreach ($typeHandle in $metadata.TypeDefinitions) {
            $type = $metadata.GetTypeDefinition($typeHandle)
            $typeName = if ($NativeTests) {
                "$($metadata.GetString($type.Namespace)).$($metadata.GetString($type.Name))"
            } else {
                Get-PublicTypeName $metadata $type
            }
            if (!$typeName) { continue }
            if (!$ManagedTests -and !$NativeTests) {
                "T:$typeName"
                continue
            }

            $methods = foreach ($methodHandle in $type.GetMethods()) {
                $metadata.GetMethodDefinition($methodHandle)
            }
            $counts = @{}
            foreach ($method in $methods) {
                $methodName = $metadata.GetString($method.Name)
                $counts[$methodName] = 1 + [int]$counts[$methodName]
            }
            foreach ($method in $methods) {
                $methodName = $metadata.GetString($method.Name)
                # Exclude ambiguous overload identities from trace matching.
                if ($counts[$methodName] -ne 1) { continue }
                if ($NativeTests) {
                    "M:$typeName.$methodName"
                    continue
                }
                foreach ($attributeHandle in $method.GetCustomAttributes()) {
                    $customAttribute = $metadata.GetCustomAttribute($attributeHandle)
                    $constructor = $customAttribute.Constructor
                    if ($constructor.Kind -ne [System.Reflection.Metadata.HandleKind]::MemberReference) { continue }
                    $declaring = $metadata.GetMemberReference([System.Reflection.Metadata.MemberReferenceHandle]$constructor).Parent
                    if ($declaring.Kind -ne [System.Reflection.Metadata.HandleKind]::TypeReference) { continue }
                    $attribute = $metadata.GetTypeReference([System.Reflection.Metadata.TypeReferenceHandle]$declaring)
                    $attributeName = "$($metadata.GetString($attribute.Namespace)).$($metadata.GetString($attribute.Name))"
                    if ($attributeName -cin @('Xunit.FactAttribute', 'Xunit.TheoryAttribute',
                        'Avalonia.Headless.XUnit.AvaloniaFactAttribute', 'Avalonia.Headless.XUnit.AvaloniaTheoryAttribute')) {
                        # ECMA-335 named property, string type, serialized name "Skip".
                        $skipProperty = [string][char]0x54 + [char]0x0e + [char]0x04 + 'Skip'
                        $arguments = [System.Text.Encoding]::UTF8.GetString($metadata.GetBlobBytes($customAttribute.Value))
                        if (!$arguments.Contains($skipProperty)) { "M:$typeName.$methodName" }
                        break
                    }
                }
            }
        }
    }
    finally {
        $pe.Dispose()
        $stream.Dispose()
    }
}

$criterionPattern = 'TEST-RETRO-\d+#[a-z0-9-]+'
$evidence = foreach ($suite in 'Integration', 'Native', 'Unit') {
    $projectPath = "tests/RetroIde.${suite}Tests"
    $xml = Get-ProjectXml $projectPath
    $entryPoints = if ($suite -eq 'Native') {
        $nativeSymbols = @(Get-CompiledTraceSymbols $projectPath -NativeTests)
        foreach ($entryPoint in $nativeEntryPoints) {
            if ($entryPoint -cnotin $nativeSymbols) { throw "Missing compiled native test entry point $entryPoint." }
            $entryPoint
        }
    } else {
        @(Get-CompiledTraceSymbols $projectPath -ManagedTests)
    }
    foreach ($member in $xml.SelectNodes('doc/members/member[starts-with(@name, "M:")]')) {
        $criteria = [regex]::Matches($member.InnerText, $criterionPattern).Value | Sort-Object -Unique
        if (!$criteria) { continue }
        $method = $member.GetAttribute('name') -replace '\(.*$', ''
        if ($method -cnotin $entryPoints) { throw "$method has trace annotations but is not a runnable test entry point." }
        $criteria
    }
}
if (!$evidence) { throw 'No test annotations were found in compiled XML documentation.' }

$contractCriteria = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$contractCount = 0
foreach ($projectPath in $productProjects) {
    $xml = Get-ProjectXml $projectPath
    foreach ($contract in Get-CompiledTraceSymbols $projectPath) {
        $member = $xml.SelectSingleNode("doc/members/member[@name='$contract']")
        if ($null -eq $member) { throw "Missing compiled XML documentation for $contract." }
        $matchesFound = [regex]::Matches($member.InnerText, $criterionPattern)
        if ($matchesFound.Count -eq 0) { throw "No criterion on public contract $contract." }
        foreach ($match in $matchesFound) { [void]$contractCriteria.Add($match.Value) }
        $contractCount++
    }
}
$export = $null
if ($PSBoundParameters.ContainsKey('QBrainExport')) {
    if ([string]::IsNullOrWhiteSpace($QBrainExport)) { throw 'Supply a path to the QBrain export.' }
    $export = Get-Content -LiteralPath $QBrainExport -Raw
    if ([string]::IsNullOrWhiteSpace($export)) { throw 'The supplied QBrain export is empty.' }
}
$knownCriteria = @{}
foreach ($phase in '01', '02', '03') {
    $projection = Get-Content -LiteralPath (Join-Path $root "docs/requirements/phase-$phase-test-criteria.yaml") -Raw
    $projected = foreach ($requirement in [regex]::Matches($projection, '(?ms)^    - id: (TEST-RETRO-\d+)\r?\n(.*?)(?=^    - id:|^  \w+:|\z)')) {
        foreach ($criterion in [regex]::Matches($requirement.Groups[2].Value, '(?m)^      - id: ([a-z0-9-]+)\r?$')) {
            "$($requirement.Groups[1].Value)#$($criterion.Groups[1].Value)"
        }
    }
    if (!$projected) { throw "The Phase $phase QBrain projection contains no test criteria." }
    foreach ($criterion in $projected) {
        if ($knownCriteria.ContainsKey($criterion)) { throw "$criterion appears in more than one phase projection." }
        $knownCriteria[$criterion] = $phase
    }
}

$allLinkedCriteria = @($evidence | Sort-Object -Unique)
foreach ($criterion in $allLinkedCriteria) {
    if (!$knownCriteria.ContainsKey($criterion)) { throw "$criterion is outside the known QBrain projections." }
    [void]$contractCriteria.Remove($criterion)
}
$scopedCriteria = @($knownCriteria.Keys | Where-Object { $knownCriteria[$_] -le $ThroughPhase })
$scopedEvidence = @($evidence | Where-Object { $knownCriteria[$_] -le $ThroughPhase })
$uniqueCriteria = @($scopedEvidence | Sort-Object -Unique)
if ($missingCriteria = @($scopedCriteria | Where-Object { $_ -cnotin $uniqueCriteria } | Sort-Object)) {
    throw "Missing compiled test links through Phase ${ThroughPhase}: $($missingCriteria -join ', ')."
}
foreach ($criterion in $uniqueCriteria) {
    if ($export) {
        $requirementId, $criterionId = $criterion -split '#'
        $records = [regex]::Match($export, "(?ms)^    - id: $requirementId\r?\n.*?(?=^    - id:|^  \w+:|\z)")
        if (!$records.Success -or $records.Value -cnotmatch "(?m)^      - id: $criterionId\r?$") {
            throw "$criterion is missing from the supplied QBrain export."
        }
    }
}
if ($contractCriteria.Count -ne 0) { throw "Public contracts without a mapped test: $($contractCriteria -join ', ')." }

$scope = if ($ThroughPhase -eq '01') { 'Phase 01' } else { "through Phase $ThroughPhase" }
Write-Output "Verified $($scopedEvidence.Count) test links $scope and $contractCount documented public contracts."
if ($null -ne $export) {
    $hash = (Get-FileHash -LiteralPath $QBrainExport -Algorithm SHA256).Hash
    Write-Output "Verified $($uniqueCriteria.Count) distinct criteria in the supplied QBrain export (SHA256 $hash)."
}
else {
    Write-Output 'Compiled documentation and trace links verified without a QBrain export.'
}
