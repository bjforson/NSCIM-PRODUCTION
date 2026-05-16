<# 
.SYNOPSIS
Builds a non-mutating route and WebApp /api callsite inventory.

.DESCRIPTION
Scans source files for ASP.NET controller actions, C# minimal API mappings,
FastAPI decorators, and literal WebApp /api callsites. The script prints a
repeatable Phase 0 safety-net summary and can emit the full inventory as JSON.

The script does not call live services and does not write output files.

.EXAMPLE
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\diagnostics\Invoke-RouteCallsiteInventory.ps1 -Detailed

.EXAMPLE
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\diagnostics\Invoke-RouteCallsiteInventory.ps1 -AsJson
#>

#requires -Version 5.1

[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Detailed,
    [switch]$AsJson
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($Root)) {
    $scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
    $Root = (Resolve-Path (Join-Path $scriptDirectory '..\..')).Path
}

$InventoryScriptPath = [System.IO.Path]::GetFullPath($MyInvocation.MyCommand.Path)
$RepoRoot = [System.IO.Path]::GetFullPath($Root).TrimEnd([char[]]@('\', '/'))
$KnownExternalServiceOnlyPaths = @(
    '/api/BOEScanData',
    '/api/rm/scan',
    '/api/tags',
    '/api/generate'
)

$IgnoredDirectoryNames = @(
    '.git',
    '.vs',
    'artifacts',
    'bin',
    'deploy-backups',
    'dist',
    'logs',
    'node_modules',
    'obj',
    'packages',
    'publish',
    'TestResults',
    'wwwroot\lib'
)
$SourceFileCache = $null

function Get-RelativePath {
    param([Parameter(Mandatory = $true)][string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    if ($fullPath.StartsWith($RepoRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $fullPath.Substring($RepoRoot.Length).TrimStart([char[]]@('\', '/'))
    }

    return $fullPath
}

function Test-IsIgnoredPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    $relativePath = Get-RelativePath -Path $Path
    $parts = $relativePath -split '[\\/]'
    foreach ($ignored in $IgnoredDirectoryNames) {
        if ($ignored -like '*\*') {
            if ($relativePath -like "*$ignored*") {
                return $true
            }

            continue
        }

        if ($parts -contains $ignored) {
            return $true
        }
    }

    return $false
}

function Get-SourceFiles {
    param([Parameter(Mandatory = $true)][string[]]$Extensions)

    if ($null -eq $script:SourceFileCache) {
        $script:SourceFileCache = @(
            Get-ChildItem -LiteralPath $RepoRoot -Recurse -File -ErrorAction SilentlyContinue |
                Where-Object { -not (Test-IsIgnoredPath -Path $_.FullName) }
        )
    }

    $extensionLookup = @{}
    foreach ($extension in $Extensions) {
        $extensionLookup[$extension.ToLowerInvariant()] = $true
    }

    $script:SourceFileCache |
        Where-Object { $extensionLookup.ContainsKey($_.Extension.ToLowerInvariant()) }
}

function Get-FirstQuotedValue {
    param([AllowNull()][string]$Text)

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return ''
    }

    if ($Text -match '"([^"]*)"') {
        return $Matches[1]
    }

    if ($Text -match "'([^']*)'") {
        return $Matches[1]
    }

    return ''
}

function Expand-RouteTokens {
    param(
        [AllowNull()][string]$Template,
        [Parameter(Mandatory = $true)][string]$ControllerName,
        [AllowNull()][string]$ActionName
    )

    if ($null -eq $Template) {
        return ''
    }

    $controllerSegment = $ControllerName -replace 'Controller$', ''
    $expanded = $Template -replace '\[controller\]', $controllerSegment
    $expanded = $expanded -replace '\[action\]', $ActionName
    return $expanded
}

function Join-RouteParts {
    param(
        [AllowNull()][string]$BaseRoute,
        [AllowNull()][string]$ChildRoute
    )

    $base = if ($null -eq $BaseRoute) { '' } else { $BaseRoute.Trim() }
    $child = if ($null -eq $ChildRoute) { '' } else { $ChildRoute.Trim() }

    if ($child.StartsWith('~/')) {
        $combined = $child.Substring(2)
    }
    elseif ($child.StartsWith('/')) {
        $combined = $child
    }
    elseif ([string]::IsNullOrWhiteSpace($base)) {
        $combined = $child
    }
    elseif ([string]::IsNullOrWhiteSpace($child)) {
        $combined = $base
    }
    else {
        $combined = "$($base.TrimEnd('/'))/$($child.TrimStart('/'))"
    }

    if ([string]::IsNullOrWhiteSpace($combined)) {
        return '/'
    }

    $normalized = '/' + $combined.TrimStart('/')
    $normalized = $normalized -replace '\\', '/'
    $normalized = $normalized -replace '/+', '/'
    return $normalized
}

function Get-FirstApiSegment {
    param([AllowNull()][string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return ''
    }

    $pathOnly = ($Path -split '\?')[0].Trim()
    if ($pathOnly -match '^/api/([^/\?]+)') {
        return $Matches[1]
    }

    return ''
}

function Get-KnownExternalPrefix {
    param([AllowNull()][string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return ''
    }

    $lowerPath = $Path.ToLowerInvariant()
    foreach ($prefix in $KnownExternalServiceOnlyPaths) {
        $lowerPrefix = $prefix.ToLowerInvariant()
        if (
            $lowerPath -eq $lowerPrefix -or
            $lowerPath.StartsWith("${lowerPrefix}/", [System.StringComparison]::OrdinalIgnoreCase) -or
            $lowerPath.StartsWith("${lowerPrefix}?", [System.StringComparison]::OrdinalIgnoreCase)
        ) {
            return $prefix
        }
    }

    return ''
}

function Get-RouteTemplatesFromAttributes {
    param([string[]]$Attributes)

    $templates = New-Object System.Collections.Generic.List[string]
    foreach ($attribute in $Attributes) {
        if ($attribute -match '^\s*\[Route(?:Attribute)?\((?<args>.*)\)\]') {
            $template = Get-FirstQuotedValue -Text $Matches['args']
            $templates.Add($template)
        }
    }

    return $templates.ToArray()
}

function Get-ActionRouteAttributes {
    param([string[]]$Attributes)

    $actions = New-Object System.Collections.Generic.List[object]
    foreach ($attribute in $Attributes) {
        $httpMatches = [regex]::Matches(
            $attribute,
            '\[Http(?<verb>Get|Post|Put|Delete|Patch|Head|Options)(?:Attribute)?(?:\((?<args>[^\)]*)\))?\]'
        )

        foreach ($match in $httpMatches) {
            $template = Get-FirstQuotedValue -Text $match.Groups['args'].Value
            $actions.Add([pscustomobject]@{
                Verb = $match.Groups['verb'].Value.ToUpperInvariant()
                Template = $template
            })
        }

        if ($attribute -match '^\s*\[Route(?:Attribute)?\((?<args>.*)\)\]') {
            $template = Get-FirstQuotedValue -Text $Matches['args']
            $actions.Add([pscustomobject]@{
                Verb = 'ANY'
                Template = $template
            })
        }

        if ($attribute -match '^\s*\[AcceptVerbs(?:Attribute)?\((?<args>.*)\)\]') {
            $args = $Matches['args']
            $verbs = [regex]::Matches($args, '"(GET|POST|PUT|DELETE|PATCH|HEAD|OPTIONS)"') |
                ForEach-Object { $_.Groups[1].Value }
            $template = ''
            if ($args -match 'Route\s*=\s*"(?<route>[^"]*)"') {
                $template = $Matches['route']
            }

            foreach ($verb in $verbs) {
                $actions.Add([pscustomobject]@{
                    Verb = $verb.ToUpperInvariant()
                    Template = $template
                })
            }
        }
    }

    return $actions.ToArray()
}

function Get-MethodNameFromLine {
    param([AllowEmptyString()][string]$Line)

    if ($Line -match '^\s*(?:public|private|protected|internal)\s+(?:static\s+|async\s+|virtual\s+|override\s+|sealed\s+|new\s+)*[A-Za-z0-9_<>,\[\]\?\.]+\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(') {
        return $Matches['name']
    }

    return ''
}

function Get-ControllerRoutes {
    $routes = New-Object System.Collections.Generic.List[object]
    $controllerFiles = Get-SourceFiles -Extensions @('.cs') |
        Where-Object { $_.Name -like '*Controller.cs' }

    foreach ($file in $controllerFiles) {
        $relativePath = Get-RelativePath -Path $file.FullName
        $lines = @(Get-Content -LiteralPath $file.FullName)
        $attributeBuffer = New-Object System.Collections.Generic.List[string]
        $controllerName = ''
        $classRoutes = @('')
        $insideAttributeBlock = $false

        for ($i = 0; $i -lt $lines.Count; $i++) {
            $line = $lines[$i]
            $trimmed = $line.Trim()

            if ($insideAttributeBlock) {
                if ($trimmed -match '^\s*\)\]\s*$' -or $trimmed -match '^\s*\]\s*$') {
                    $insideAttributeBlock = $false
                }

                continue
            }

            if ($trimmed -match '^\[[A-Za-z]') {
                $attributeBuffer.Add($trimmed)
                if ($trimmed -notmatch '\]') {
                    $insideAttributeBlock = $true
                }

                continue
            }

            if ($line -match '\bclass\s+(?<class>[A-Za-z_][A-Za-z0-9_]*Controller)\b') {
                $controllerName = $Matches['class']
                $classRoutes = @(Get-RouteTemplatesFromAttributes -Attributes $attributeBuffer.ToArray())
                if ($classRoutes.Count -eq 0) {
                    $classRoutes = @('')
                }

                $attributeBuffer.Clear()
                continue
            }

            $methodName = Get-MethodNameFromLine -Line $line
            if (-not [string]::IsNullOrWhiteSpace($controllerName) -and -not [string]::IsNullOrWhiteSpace($methodName)) {
                $actionRoutes = @(Get-ActionRouteAttributes -Attributes $attributeBuffer.ToArray())
                foreach ($actionRoute in $actionRoutes) {
                    foreach ($classRoute in $classRoutes) {
                        $expandedBase = Expand-RouteTokens -Template $classRoute -ControllerName $controllerName -ActionName $methodName
                        $expandedChild = Expand-RouteTokens -Template $actionRoute.Template -ControllerName $controllerName -ActionName $methodName
                        $routePath = Join-RouteParts -BaseRoute $expandedBase -ChildRoute $expandedChild

                        $routes.Add([pscustomobject]@{
                            Source = 'Controller'
                            Verb = $actionRoute.Verb
                            Route = $routePath
                            FirstApiSegment = Get-FirstApiSegment -Path $routePath
                            Controller = $controllerName
                            Action = $methodName
                            File = $relativePath
                            Line = $i + 1
                        })
                    }
                }

                $attributeBuffer.Clear()
                continue
            }

            if (
                $trimmed.Length -gt 0 -and
                -not $trimmed.StartsWith('//') -and
                -not $trimmed.StartsWith('///')
            ) {
                $attributeBuffer.Clear()
            }
        }
    }

    return $routes.ToArray()
}

function Get-MinimalApiRoutes {
    $routes = New-Object System.Collections.Generic.List[object]
    $files = Get-SourceFiles -Extensions @('.cs')

    foreach ($file in $files) {
        $relativePath = Get-RelativePath -Path $file.FullName
        $lines = @(Get-Content -LiteralPath $file.FullName)
        $routeGroups = @{}

        for ($i = 0; $i -lt $lines.Count; $i++) {
            $line = $lines[$i]
            if ($line -match '(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?:[A-Za-z_][A-Za-z0-9_]*\.)?MapGroup\s*\(\s*"(?<route>[^"]+)"') {
                $routeGroups[$Matches['name']] = $Matches['route']
                $routePath = Join-RouteParts -BaseRoute '' -ChildRoute $Matches['route']
                $routes.Add([pscustomobject]@{
                    Source = 'MinimalApi'
                    Verb = 'GROUP'
                    Route = $routePath
                    FirstApiSegment = Get-FirstApiSegment -Path $routePath
                    Handler = $Matches['name']
                    File = $relativePath
                    Line = $i + 1
                })
            }
        }

        for ($i = 0; $i -lt $lines.Count; $i++) {
            $line = $lines[$i]
            $directMatches = [regex]::Matches(
                $line,
                '\bMap(?<verb>Get|Post|Put|Delete|Patch|Methods|HealthChecks|Hub)(?:<[^>]+>)?\s*\(\s*"(?<route>[^"]+)"'
            )

            foreach ($match in $directMatches) {
                $routePath = Join-RouteParts -BaseRoute '' -ChildRoute $match.Groups['route'].Value
                $routes.Add([pscustomobject]@{
                    Source = 'MinimalApi'
                    Verb = $match.Groups['verb'].Value.ToUpperInvariant()
                    Route = $routePath
                    FirstApiSegment = Get-FirstApiSegment -Path $routePath
                    Handler = "Map$($match.Groups['verb'].Value)"
                    File = $relativePath
                    Line = $i + 1
                })
            }

            $groupMatches = [regex]::Matches(
                $line,
                '(?<group>[A-Za-z_][A-Za-z0-9_]*)\.Map(?<verb>Get|Post|Put|Delete|Patch|Methods)(?:<[^>]+>)?\s*\(\s*"(?<route>[^"]+)"'
            )

            foreach ($match in $groupMatches) {
                $groupName = $match.Groups['group'].Value
                if (-not $routeGroups.ContainsKey($groupName)) {
                    continue
                }

                $routePath = Join-RouteParts -BaseRoute $routeGroups[$groupName] -ChildRoute $match.Groups['route'].Value
                $routes.Add([pscustomobject]@{
                    Source = 'MinimalApi'
                    Verb = $match.Groups['verb'].Value.ToUpperInvariant()
                    Route = $routePath
                    FirstApiSegment = Get-FirstApiSegment -Path $routePath
                    Handler = "$groupName.Map$($match.Groups['verb'].Value)"
                    File = $relativePath
                    Line = $i + 1
                })
            }
        }
    }

    return $routes.ToArray()
}

function Get-NextPythonFunctionName {
    param(
        [AllowEmptyString()][AllowEmptyCollection()][string[]]$Lines,
        [Parameter(Mandatory = $true)][int]$StartIndex
    )

    $max = [Math]::Min($Lines.Count - 1, $StartIndex + 8)
    for ($i = $StartIndex; $i -le $max; $i++) {
        if ($Lines[$i] -match '^\s*(?:async\s+)?def\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(') {
            return $Matches['name']
        }
    }

    return ''
}

function Get-FastApiRoutes {
    $routes = New-Object System.Collections.Generic.List[object]
    $files = Get-SourceFiles -Extensions @('.py')

    foreach ($file in $files) {
        $relativePath = Get-RelativePath -Path $file.FullName
        $lines = @(Get-Content -LiteralPath $file.FullName)
        $routerPrefixes = @{}

        for ($i = 0; $i -lt $lines.Count; $i++) {
            $line = $lines[$i]
            if ($line -match '(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*APIRouter\s*\((?<args>.*)') {
                $routerName = $Matches['name']
                $routerArgs = $Matches['args']
                if ($routerArgs -match 'prefix\s*=\s*["''](?<prefix>[^"'']+)["'']') {
                    $routerPrefixes[$routerName] = $Matches['prefix']
                }
            }
        }

        for ($i = 0; $i -lt $lines.Count; $i++) {
            $line = $lines[$i]
            if ($line -match '^\s*@(?<receiver>[A-Za-z_][A-Za-z0-9_]*)\.(?<method>get|post|put|delete|patch|api_route)\s*\(\s*["''](?<route>[^"'']+)["'']') {
                $receiver = $Matches['receiver']
                $method = $Matches['method']
                $routeTemplate = $Matches['route']
                $baseRoute = ''
                if ($routerPrefixes.ContainsKey($receiver)) {
                    $baseRoute = $routerPrefixes[$receiver]
                }

                $routePath = Join-RouteParts -BaseRoute $baseRoute -ChildRoute $routeTemplate
                $verb = $method.ToUpperInvariant()
                if ($verb -eq 'API_ROUTE' -and $line -match 'methods\s*=\s*\[(?<methods>[^\]]+)') {
                    $verb = ([regex]::Matches($Matches['methods'], '["''](?<verb>[A-Za-z]+)["'']') |
                        ForEach-Object { $_.Groups['verb'].Value.ToUpperInvariant() }) -join ','
                }

                $routes.Add([pscustomobject]@{
                    Source = 'FastAPI'
                    Verb = $verb
                    Route = $routePath
                    FirstApiSegment = Get-FirstApiSegment -Path $routePath
                    Handler = Get-NextPythonFunctionName -Lines $lines -StartIndex ($i + 1)
                    File = $relativePath
                    Line = $i + 1
                })
            }
        }
    }

    return $routes.ToArray()
}

function Get-WebAppApiCallsites {
    param([hashtable]$ProviderSegmentSet)

    $callsites = New-Object System.Collections.Generic.List[object]
    $apiLiteralPattern = [regex]'(?i)(?<path>/api/[^"''`\s<>)]+)'
    $webExtensions = @('.cs', '.razor', '.cshtml', '.js', '.jsx', '.ts', '.tsx')
    $files = Get-SourceFiles -Extensions $webExtensions |
        Where-Object {
            $_.FullName -match '[\\/]NickScanWebApp(\.|[\\/])' -or
            $_.FullName -match '[\\/]NickHR\.WebApp[\\/]' -or
            $_.FullName -match '[\\/]NickFinance\.WebApp[\\/]' -or
            $_.FullName -match '[\\/]NickERP\.Portal[\\/]'
        }

    foreach ($file in $files) {
        $relativePath = Get-RelativePath -Path $file.FullName
        $lines = @(Get-Content -LiteralPath $file.FullName)

        for ($i = 0; $i -lt $lines.Count; $i++) {
            $line = $lines[$i]
            $trimmed = $line.Trim()
            if (
                $trimmed.StartsWith('//') -or
                $trimmed.StartsWith('///') -or
                $trimmed.StartsWith('*') -or
                $trimmed.StartsWith('<!--') -or
                $trimmed.StartsWith('@*')
            ) {
                continue
            }

            foreach ($match in $apiLiteralPattern.Matches($line)) {
                $path = $match.Groups['path'].Value.TrimEnd([char[]]@('.', ',', ';'))
                $segment = Get-FirstApiSegment -Path $path
                $knownExternalPrefix = Get-KnownExternalPrefix -Path $path
                $category = 'UnmatchedLocalConsumerSegment'

                if (-not [string]::IsNullOrWhiteSpace($knownExternalPrefix)) {
                    $category = 'ExternalServiceOnly'
                }
                elseif ($ProviderSegmentSet.ContainsKey($segment.ToLowerInvariant())) {
                    $category = 'LocalSegmentMatched'
                }

                $callsites.Add([pscustomobject]@{
                    Source = 'WebAppCallsite'
                    Path = $path
                    FirstApiSegment = $segment
                    Category = $category
                    ExternalServiceOnlyPrefix = $knownExternalPrefix
                    File = $relativePath
                    Line = $i + 1
                    Snippet = $trimmed
                })
            }
        }
    }

    return $callsites.ToArray()
}

function Get-KnownExternalServiceOnlySourceReferences {
    $references = New-Object System.Collections.Generic.List[object]
    $apiLiteralPattern = [regex]'(?i)(?<path>/api/[^"''`\s<>)]+)'
    $sourceExtensions = @('.cs', '.razor', '.cshtml', '.js', '.jsx', '.ts', '.tsx', '.py', '.ps1', '.psm1')
    $files = Get-SourceFiles -Extensions $sourceExtensions

    foreach ($file in $files) {
        if ($file.FullName.Equals($InventoryScriptPath, [System.StringComparison]::OrdinalIgnoreCase)) {
            continue
        }

        $relativePath = Get-RelativePath -Path $file.FullName
        $lines = @(Get-Content -LiteralPath $file.FullName)

        for ($i = 0; $i -lt $lines.Count; $i++) {
            $line = $lines[$i]
            $trimmed = $line.Trim()
            if (
                $trimmed.StartsWith('//') -or
                $trimmed.StartsWith('///') -or
                $trimmed.StartsWith('*') -or
                $trimmed.StartsWith('<!--') -or
                $trimmed.StartsWith('@*')
            ) {
                continue
            }

            foreach ($match in $apiLiteralPattern.Matches($line)) {
                $path = $match.Groups['path'].Value.TrimEnd([char[]]@('.', ',', ';'))
                $knownExternalPrefix = Get-KnownExternalPrefix -Path $path
                if ([string]::IsNullOrWhiteSpace($knownExternalPrefix)) {
                    continue
                }

                $references.Add([pscustomobject]@{
                    Source = 'KnownExternalServiceOnlySourceReference'
                    Path = $path
                    FirstApiSegment = Get-FirstApiSegment -Path $path
                    ExternalServiceOnlyPrefix = $knownExternalPrefix
                    File = $relativePath
                    Line = $i + 1
                    Snippet = $trimmed
                })
            }
        }
    }

    return $references.ToArray()
}

function Convert-ToSegmentRows {
    param(
        [object[]]$Items,
        [string]$SegmentProperty,
        [string]$PathProperty
    )

    $Items |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_.$SegmentProperty) } |
        Group-Object -Property $SegmentProperty |
        Sort-Object -Property @{ Expression = 'Count'; Descending = $true }, @{ Expression = 'Name'; Ascending = $true } |
        ForEach-Object {
            $examples = $_.Group |
                Select-Object -First 3 |
                ForEach-Object { $_.$PathProperty } |
                Sort-Object -Unique

            [pscustomobject]@{
                Segment = $_.Name
                Count = $_.Count
                Examples = $examples -join '; '
            }
        }
}

function Write-TableSection {
    param(
        [Parameter(Mandatory = $true)][string]$Title,
        [AllowEmptyCollection()][object[]]$Rows
    )

    Write-Output ''
    Write-Output $Title
    $rowsArray = @($Rows)
    if ($rowsArray.Count -eq 0) {
        Write-Output '  (none)'
        return
    }

    $rowsArray | Format-Table -AutoSize | Out-String -Width 220 | ForEach-Object { $_.TrimEnd() } | Write-Output
}

$controllerRoutes = @(Get-ControllerRoutes)
$minimalApiRoutes = @(Get-MinimalApiRoutes)
$fastApiRoutes = @(Get-FastApiRoutes)
$providerRoutes = @($controllerRoutes + $minimalApiRoutes + $fastApiRoutes)

$providerSegmentSet = @{}
foreach ($route in $providerRoutes) {
    if (-not [string]::IsNullOrWhiteSpace($route.FirstApiSegment)) {
        $providerSegmentSet[$route.FirstApiSegment.ToLowerInvariant()] = $true
    }
}

$webAppCallsites = @(Get-WebAppApiCallsites -ProviderSegmentSet $providerSegmentSet)
$knownExternalSourceReferences = @(Get-KnownExternalServiceOnlySourceReferences)

$providerFirstSegments = @(Convert-ToSegmentRows -Items $providerRoutes -SegmentProperty 'FirstApiSegment' -PathProperty 'Route')
$consumerFirstSegments = @(Convert-ToSegmentRows -Items $webAppCallsites -SegmentProperty 'FirstApiSegment' -PathProperty 'Path')
$unmatchedLocalConsumerSegments = @(
    $webAppCallsites |
        Where-Object { $_.Category -eq 'UnmatchedLocalConsumerSegment' } |
        Group-Object -Property FirstApiSegment |
        Sort-Object -Property @{ Expression = 'Count'; Descending = $true }, @{ Expression = 'Name'; Ascending = $true } |
        ForEach-Object {
            $segmentName = if ([string]::IsNullOrWhiteSpace($_.Name)) { '(empty)' } else { $_.Name }
            $examples = $_.Group |
                Select-Object -First 5 |
                ForEach-Object { "$($_.Path) ($($_.File):$($_.Line))" }

            [pscustomobject]@{
                Segment = $segmentName
                CallsiteCount = $_.Count
                Examples = $examples -join '; '
            }
        }
)
$externalServiceOnlyCallsites = @(
    $webAppCallsites |
        Where-Object { $_.Category -eq 'ExternalServiceOnly' } |
        Group-Object -Property ExternalServiceOnlyPrefix |
        Sort-Object -Property @{ Expression = 'Count'; Descending = $true }, @{ Expression = 'Name'; Ascending = $true } |
        ForEach-Object {
            $examples = $_.Group |
                Select-Object -First 5 |
                ForEach-Object { "$($_.Path) ($($_.File):$($_.Line))" }

            [pscustomobject]@{
                Prefix = $_.Name
                CallsiteCount = $_.Count
                Examples = $examples -join '; '
            }
        }
)
$externalServiceOnlySourceReferences = @(
    $knownExternalSourceReferences |
        Group-Object -Property ExternalServiceOnlyPrefix |
        Sort-Object -Property @{ Expression = 'Count'; Descending = $true }, @{ Expression = 'Name'; Ascending = $true } |
        ForEach-Object {
            $examples = $_.Group |
                Select-Object -First 5 |
                ForEach-Object { "$($_.Path) ($($_.File):$($_.Line))" }

            [pscustomobject]@{
                Prefix = $_.Name
                ReferenceCount = $_.Count
                Examples = $examples -join '; '
            }
        }
)

$result = [pscustomobject]@{
    GeneratedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
    Root = $RepoRoot
    KnownExternalServiceOnlyPaths = $KnownExternalServiceOnlyPaths
    Counts = [pscustomobject]@{
        BackendControllerRouteActions = $controllerRoutes.Count
        BackendControllerFiles = @($controllerRoutes | Select-Object -ExpandProperty File -Unique).Count
        MinimalApiRoutes = $minimalApiRoutes.Count
        FastApiRoutes = $fastApiRoutes.Count
        WebAppApiCallsites = $webAppCallsites.Count
        WebAppCallsiteFiles = @($webAppCallsites | Select-Object -ExpandProperty File -Unique).Count
        ProviderFirstSegments = $providerFirstSegments.Count
        ConsumerFirstSegments = $consumerFirstSegments.Count
        ExternalServiceOnlyCallsites = @($webAppCallsites | Where-Object { $_.Category -eq 'ExternalServiceOnly' }).Count
        ExternalServiceOnlySourceReferences = $knownExternalSourceReferences.Count
        UnmatchedLocalConsumerSegments = $unmatchedLocalConsumerSegments.Count
    }
    BackendControllerRoutes = $controllerRoutes
    MinimalApiRoutes = $minimalApiRoutes
    FastApiRoutes = $fastApiRoutes
    WebAppApiCallsites = $webAppCallsites
    ProviderFirstSegments = $providerFirstSegments
    ConsumerFirstSegments = $consumerFirstSegments
    UnmatchedLocalConsumerSegments = $unmatchedLocalConsumerSegments
    ExternalServiceOnlyCallsites = $externalServiceOnlyCallsites
    ExternalServiceOnlySourceReferences = $externalServiceOnlySourceReferences
}

if ($AsJson) {
    $result | ConvertTo-Json -Depth 8
    return
}

Write-Output 'Route/callsite inventory'
Write-Output "Root: $($result.Root)"
Write-Output "Generated UTC: $($result.GeneratedAtUtc)"
Write-Output ''
Write-Output 'Counts'
Write-Output "  Backend controller route actions: $($result.Counts.BackendControllerRouteActions) across $($result.Counts.BackendControllerFiles) controller files"
Write-Output "  Minimal API routes: $($result.Counts.MinimalApiRoutes)"
Write-Output "  FastAPI routes: $($result.Counts.FastApiRoutes)"
Write-Output "  WebApp /api callsites: $($result.Counts.WebAppApiCallsites) across $($result.Counts.WebAppCallsiteFiles) files"
Write-Output "  Provider /api first segments: $($result.Counts.ProviderFirstSegments)"
Write-Output "  Consumer /api first segments: $($result.Counts.ConsumerFirstSegments)"
Write-Output "  Known external/service-only WebApp callsites: $($result.Counts.ExternalServiceOnlyCallsites)"
Write-Output "  Known external/service-only source references: $($result.Counts.ExternalServiceOnlySourceReferences)"
Write-Output "  Unmatched local consumer segments: $($result.Counts.UnmatchedLocalConsumerSegments)"

Write-TableSection -Title 'Unmatched local consumer segments' -Rows $unmatchedLocalConsumerSegments
Write-TableSection -Title 'Known external/service-only WebApp callsites' -Rows $externalServiceOnlyCallsites
Write-TableSection -Title 'Known external/service-only source references' -Rows $externalServiceOnlySourceReferences

if ($Detailed) {
    Write-TableSection -Title 'Provider /api first segments (top 50)' -Rows @($providerFirstSegments | Select-Object -First 50)
    Write-TableSection -Title 'Consumer /api first segments (top 50)' -Rows @($consumerFirstSegments | Select-Object -First 50)
    Write-TableSection -Title 'Backend controller route samples (first 80)' -Rows @($controllerRoutes | Select-Object -First 80 Verb, Route, Controller, Action, File, Line)
    Write-TableSection -Title 'Minimal API route samples (first 80)' -Rows @($minimalApiRoutes | Select-Object -First 80 Verb, Route, Handler, File, Line)
    Write-TableSection -Title 'FastAPI route samples (first 80)' -Rows @($fastApiRoutes | Select-Object -First 80 Verb, Route, Handler, File, Line)
}

Write-Output ''
Write-Output 'Use -AsJson for the full machine-readable inventory.'
