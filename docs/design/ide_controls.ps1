function Import-IdeSvgComponents([string]$ComponentRoot) {
    $components = @{}
    foreach ($file in Get-ChildItem -LiteralPath $ComponentRoot -File -Filter '*.svg' | Sort-Object Name) {
        $document = [xml](Get-Content -LiteralPath $file.FullName -Raw)
        foreach ($node in $document.SelectNodes("/*[local-name()='svg']/*[local-name()='defs']/*")) {
            $id = $node.GetAttribute('id')
            if (!$id -or $components.ContainsKey($id)) {
                throw "Missing or duplicate component ID '$id' in $($file.Name)."
            }
            $components[$id] = $node.OuterXml
        }
    }
    return $components
}

function Add-IdeSvgComponent($Svg, [string]$Id, [int]$X = 0, [int]$Y = 0, [int]$Width = 0, [int]$Height = 0) {
    $size = if ($Width -gt 0 -and $Height -gt 0) { ' width="{0}" height="{1}"' -f $Width,$Height } else { '' }
    [void]$Svg.AppendLine(('<use href="#{0}" x="{1}" y="{2}"{3}/>' -f $Id,$X,$Y,$size))
}

function Get-IdeSvgDefinitions([string]$Screen, [hashtable]$Components) {
    $emitted = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $pending = [System.Collections.Generic.Queue[string]]::new()
    $pending.Enqueue('ide-typography')
    $references = [regex]'<use\s[^>]*\bhref="#([^"]+)"'
    foreach ($match in $references.Matches($Screen)) { $pending.Enqueue($match.Groups[1].Value) }

    $definitions = [System.Text.StringBuilder]::new()
    [void]$definitions.AppendLine('<defs>')
    while ($pending.Count -gt 0) {
        $id = $pending.Dequeue()
        if (!$emitted.Add($id)) { continue }
        if (!$Components.ContainsKey($id)) { throw "Unknown SVG component '$id'." }
        $markup = $Components[$id]
        [void]$definitions.AppendLine($markup)
        foreach ($match in $references.Matches($markup)) { $pending.Enqueue($match.Groups[1].Value) }
    }
    [void]$definitions.AppendLine('</defs>')
    return $definitions.ToString()
}

function ConvertTo-IdeText([string]$Value) {
    [System.Security.SecurityElement]::Escape($Value)
}

function Add-IdeRect($Svg, $X, $Y, $W, $H, $Fill, $Stroke = 'none', $Radius = 0) {
    [void]$Svg.AppendLine(('<rect x="{0}" y="{1}" width="{2}" height="{3}" fill="{4}" stroke="{5}" rx="{6}"/>' -f $X,$Y,$W,$H,$Fill,$Stroke,$Radius))
}

function Add-IdeText($Svg, $X, $Y, $Value, $Class = 'ui', $Color = '#e6eaed') {
    [void]$Svg.AppendLine(('<text x="{0}" y="{1}" class="{2}" fill="{3}" xml:space="preserve">{4}</text>' -f $X,$Y,$Class,$Color,(ConvertTo-IdeText $Value)))
}

function Add-IdeIcon($Svg, $X, $Y, $Name, $Color = '#a8b5bb') {
    [void]$Svg.AppendLine(('<use href="#ide-icon-{0}" x="{1}" y="{2}" fill="none" stroke="{3}" stroke-width="1.3" stroke-linecap="round" stroke-linejoin="round"/>' -f $Name,$X,$Y,$Color))
}

function Add-IdeToolHeader($Svg, $X, $Y, $Width, $Title, [switch]$Floating) {
    Add-IdeSvgComponent $Svg 'ide-tool-header-fill' $X $Y $Width 32
    Add-IdeSvgComponent $Svg 'ide-tool-header-accent' $X $Y
    Add-IdeText $Svg ($X+13) ($Y+21) $Title 'label'
    Add-IdeSvgComponent $Svg 'ide-tool-header-actions' ($X+$Width-75) ($Y+8)
    if ($Floating) { Add-IdeIcon $Svg ($X+$Width-50) ($Y+8) 'float' }
    else { Add-IdeIcon $Svg ($X+$Width-50) ($Y+8) 'pin' }
}

function Add-IdeTabs($Svg, $X, $Y, $Width, [string[]]$Names, $Active, [switch]$Tools) {
    Add-IdeSvgComponent $Svg 'ide-tab-strip' $X $Y $Width 36
    $tabX = $X+1
    foreach ($name in $Names) {
        $tabWidth = 30 + $name.Length * 8
        if (!$Tools) { $tabWidth += 27 }
        $selected = $name -eq $Active
        $fg = if ($selected) { '#fff0df' } else { '#a6b5bc' }
        if ($selected) {
            Add-IdeSvgComponent $Svg 'ide-tab-active' $tabX $Y $tabWidth 36
        }
        if (!$Tools) {
            $icon = if ($name -eq 'Emulation') { 'screen' } else { 'file' }
            Add-IdeIcon $Svg ($tabX+9) ($Y+10) $icon $fg
            Add-IdeText $Svg ($tabX+31) ($Y+23) $name 'label' $fg
            if ($selected) { Add-IdeIcon $Svg ($tabX+$tabWidth-22) ($Y+10) 'close' $fg }
        } else { Add-IdeText $Svg ($tabX+12) ($Y+23) $name 'label' $fg }
        $tabX += $tabWidth+1
    }
    if (!$Tools) { Add-IdeIcon $Svg ($X+$Width-25) ($Y+10) 'down' }
}

function Add-IdeSlider($Svg, $X, $Y, $Width, [double]$Fraction, [switch]$Disabled, [switch]$Focused) {
    $offset = [int][math]::Round($Width * [math]::Clamp($Fraction, 0.0, 1.0))
    $fill = if ($Disabled) { '#829498' } else { '#edb77b' }
    Add-IdeRect $Svg $X ($Y-3) $Width 6 '#46585f' 'none' 3
    if ($offset -gt 0) { Add-IdeRect $Svg $X ($Y-3) $offset 6 $fill 'none' 3 }
    if (!$Disabled) {
        $thumbX = $X + $offset
        if ($Focused) {
            Add-IdeSvgComponent $Svg 'ide-slider-focus' $thumbX $Y
        }
        Add-IdeSvgComponent $Svg 'ide-slider-thumb' $thumbX $Y
    }
}

function Add-IdePlayer($Svg, $X, $Y, [string]$Mode, [int]$PositionSeconds, [int]$DurationSeconds, [int]$Volume, [switch]$Seeking, [switch]$VolumeFocused) {
    $isReplay = $Mode -eq 'Replay'
    if ($isReplay -and $DurationSeconds -le 0) { throw 'A replay player requires a positive duration.' }
    $playing = $Mode -eq 'Live'
    $seekX = $X + 68
    $seekWidth = 631
    $audioX = $X + 752
    $progress = if ($isReplay) { $PositionSeconds / $DurationSeconds } elseif ($playing) { 1.0 } else { 0.0 }
    $position = [timespan]::FromSeconds($PositionSeconds).ToString('hh\:mm\:ss')
    $time = if ($isReplay) { "$position / $([timespan]::FromSeconds($DurationSeconds).ToString('hh\:mm\:ss'))" } else { $position }

    [void]$Svg.AppendLine('<g id="emulation-player"><title>Emulation player</title>')
    Add-IdeSvgComponent $Svg 'ide-player-shell' $X $Y
    $transportIcon = if ($playing) { 'pause' } else { 'play' }
    $transportColor = if ($Mode -in @('Starting','Faulted')) { '#768990' } else { '#e6eaed' }
    Add-IdeIcon $Svg ($X+25) ($Y+32) $transportIcon $transportColor

    [void]$Svg.AppendLine('<g id="player-timeline"><title>Playback time and seek track</title>')
    Add-IdeText $Svg $seekX ($Y+26) $time 'log'
    $modeLabel = if ($Seeking) { 'Seeking' } else { $Mode }
    Add-IdeText $Svg ($seekX+205) ($Y+26) $modeLabel 'label' '#9bc7cb'
    Add-IdeSlider $Svg $seekX ($Y+48) $seekWidth $progress -Disabled:(!$isReplay) -Focused:$Seeking
    if ($Seeking) {
        $thumbX = $seekX + [int][math]::Round($seekWidth * $progress)
        Add-IdeText $Svg ($thumbX-34) ($Y+72) $position 'log' '#edb77b'
    }
    [void]$Svg.AppendLine('</g>')

    [void]$Svg.AppendLine('<g id="player-volume"><title>Guest audio volume</title>')
    Add-IdeText $Svg ($audioX+146) ($Y+26) "$Volume%" 'log'
    Add-IdeSlider $Svg ($audioX+29) ($Y+48) 144 ($Volume / 100.0) -Focused:$VolumeFocused
    [void]$Svg.AppendLine('</g></g>')
}
