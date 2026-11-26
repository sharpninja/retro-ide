function Add-IdeEditor($Svg, $Frame, $Mode) {
    Add-IdeText $Svg 62 180 'A1200 OS  /  kernel  /  src  /  mmu.rs' 'label' '#a8b5bb'
    Add-IdeRect $Svg 43 197 971 458 '#141e23'
    Add-IdeRect $Svg 43 197 76 458 '#192429'
    $lines = @(
        'use crate::arch::m68k::mmu;',
        ' ',
        'pub fn activate_domain(domain: &Domain) -> Result<()> {',
        '    let root = domain.page_table_root();',
        '    mmu::write_root(root);',
        '    mmu::flush_atc();',
        '    domain.activate()?;',
        '    Ok(())',
        '}'
    )
    if ($Mode -in @('draft','error')) { $lines[6] = '    domain.acitvate()?;' }
    $hit = $Mode -in @('break','step','floating','dock-target','redocked','autohide')
    $focus = if ($Mode -eq 'error' -or $Mode -eq 'step') { 6 } else { 5 }
    for ($n = 0; $n -lt $lines.Count; $n++) {
        $y = 233 + 27 * $n
        if ($n -eq $focus -and ($hit -or $Mode -eq 'error')) {
            Add-IdeRect $Svg 119 ($y-19) 892 26 $(if ($hit) { '#3e362a' } else { '#40292a' })
        }
        if ($n -eq 5 -and ($hit -or $Mode -in @('draft','error','fixed','build'))) {
            [void]$Svg.AppendLine(('<circle cx="61" cy="{0}" r="5" fill="#f18482"/>' -f ($y-5)))
        }
        if ($n -eq $focus -and $hit) { Add-IdeText $Svg 73 $y '▶' 'label' '#edb77b' }
        Add-IdeText $Svg 88 $y (213+$n) 'mono' '#83949b'
        $color = if ($n -in @(0,2,3)) { '#9bc7cb' } else { '#e1e7e2' }
        Add-IdeText $Svg 135 $y $lines[$n] 'mono' $color
        if ($n -eq 6 -and $Mode -eq 'error') {
            [void]$Svg.AppendLine(('<path d="M232 {0}l4 3 4-3 4 3 4-3 4 3 4-3 4 3 4-3 4 3 4-3 4 3 4-3 4 3 4-3 4 3 4-3 4 3 4-3" fill="none" stroke="#f18482"/>' -f ($y+4)))
            Add-IdeText $Svg 385 $y 'E0599: no method named acitvate' 'label' '#f39a97'
        }
    }
}

function Add-IdeDisplay($Svg, $Frame, $Mode, $Instance, $Preset) {
    Add-IdeText $Svg 62 179 $Instance 'label'
    $actions = if ($Frame.player -eq 'Replay') { 'Fit  ▾       Capture frame' } else { 'Fit  ▾       Capture frame       Send input' }
    Add-IdeText $Svg 235 179 $actions 'ui' '#b4c2c7'
    $inputHint = if ($Frame.player -eq 'Replay') { 'Recorded guest display' } else { 'Release input: Ctrl+Alt' }
    Add-IdeText $Svg 800 179 $inputHint 'label' '#a8b5bb'
    Add-IdeSvgComponent $Svg 'ide-guest-viewport' 43 195
    Add-IdeText $Svg 227 225 "$($Preset.machine.video) • guest framebuffer" 'label' '#a2c7c7'
    $output = @('A1200 OS  /  UAE40', '', '68040 MMU enabled', 'Starting guest…')
    if ($Preset.id -ne 'uae') {
        $output = $Frame.c
    } elseif ($Frame.player -eq 'Replay') {
        $output = @('A1200 OS  /  UAE40','') + $Frame.c
    } elseif ($Mode -eq 'boot' -or $Frame.p -match 'running-marker|docked-tools|layout-restored') {
        $output = @('A1200 OS  /  UAE40','', 'ROM      verified', 'MMU      enabled', 'Kernel   ready', '', 'uae40 login: _')
    } elseif ($Frame.p -match 'display-active|input-directed|media-degraded') {
        $line = if ($Frame.p -match 'input-directed') { 'guest$ a_' } else { 'guest$ _' }
        $output = @('A1200 OS  /  UAE40','', 'guest console ready', '', $line)
    }
    $y = 267
    foreach ($line in $output) {
        Add-IdeText $Svg 234 $y $line 'mono' '#a9d8d0'
        $y += 30
    }
}

function New-Frame {
    param([hashtable]$Frame)

    $path = Join-Path $designRoot ("frames/{0}" -f $Frame.p)
    [void][System.IO.Directory]::CreateDirectory((Split-Path -Parent $path))
    $svg = [System.Text.StringBuilder]::new()
    [void]$svg.AppendLine('<svg xmlns="http://www.w3.org/2000/svg" width="1440" height="900" viewBox="0 0 1440 900" role="img" aria-labelledby="scene">')
    [void]$svg.AppendLine(('<title id="scene">Retro IDE · {0}</title>' -f (ConvertTo-IdeText $Frame.t)))
    $definitionOffset = $svg.Length
    $story = $Frame.p.Split('/')[0]
    $instance = if ($Frame.i) { $Frame.i } else { 'UAE40-dev' }
    $status = if ($Frame.s) { $Frame.s } else { 'Paused · rev 18' }
    $mode = $Frame.mode
    $target = if ($Frame.target) { $Frame.target } else { 'uae' }
    $preset = $targetPresets[$target]
    if (!$preset) { throw "Unknown wireframe target '$target'." }
    $isUae = $target -eq 'uae'
    $projectLabel = if ($Frame.project) { $Frame.project } elseif ($isUae) { 'A1200 OS' } else { 'C64 Demo' }
    $sourceTab = if ($isUae) { 'mmu.rs' } else { 'main.asm' }
    $hasDebuggerDesign = $story -eq 'STB17'
    $isDebug = $story -in @('STB06','STB11') -or ($hasDebuggerDesign -and $Frame.tab -ne 'Emulators') -or $mode -in @('break','step','floating','dock-target','redocked','autohide') -or $Frame.p -match 'layout-restored'
    $tab = switch ($story) {
        STB01 { if ($Frame.p -match 'empty') {'Start Page'} elseif ($Frame.p -match 'selected') {'uae40.dev'} elseif ($Frame.p -match 'restored') {'Emulation'} else {'mmu.rs'} }
        STB02 { if ($Frame.p -match 'starting|running-marker|worker-fault') {'Emulation'} else {$preset.machine.profileFile} }
        STB03 {'Sessions'}
        STB04 {'Emulation'}
        STB05 {'Emulation'}
        STB06 { if ($Frame.p -match 'memory') {'Memory'} else {'Disassembly'} }
        STB07 {'Media'}
        STB08 {'Agent Transcript'}
        STB09 {'Snapshots'}
        STB10 { if ($Frame.p -match 'bundle') {'Diagnostics'} else {'Test Results'} }
        STB11 {'mmu.rs'}
        STB12 {'Guest Explorer'}
        STB13 {'Agent Transcript'}
        STB14 { if ($mode -in @('launch','boot')) {'Emulation'} else {'mmu.rs'} }
        STB15 {'Emulation'}
        STB16 { if ($Frame.p -match 'select-target') {'Launch Configurations'} else {'Emulation'} }
        STB17 {$Frame.tab}
    }
    $tabNames = if ($tab -eq 'Start Page') { @('Start Page') } else { @($sourceTab,$preset.machine.profileFile,'Emulation','Disassembly','Test Results') }
    if ($tab -notin $tabNames) { $tabNames += $tab }
    $output = switch ($story) {
        STB05 {'Serial Console'}
        STB08 {'MCP Activity'}
        STB13 {'MCP Activity'}
        STB14 { switch ($mode) { error {'Problems'} launch {'Serial Console'} boot {'Serial Console'} break {'Breakpoints'} step {'Breakpoints'} default {'Output'} } }
        STB15 {'Serial Console'}
        STB17 {'Output'}
        default { if ($isDebug) {'Breakpoints'} elseif ($Frame.p -match 'running-marker|reconnected') {'Serial Console'} else {'Output'} }
    }

    Add-IdeSvgComponent $svg 'ide-workbench'
    Add-IdeText $svg 208 28 $projectLabel 'ui' '#a8b5bb'
    $connection = if ($Frame.connection) { $Frame.connection } else { "$($preset.label) / $($preset.machine.id)" }
    Add-IdeText $svg 181 105 $connection 'label'
    $running = $status -match 'Running|Starting'
    $runVerb = if ($isDebug -and $status -match 'Paused') { 'Continue' } else { 'Debug' }
    Add-IdeText $svg 361 105 $runVerb 'label'
    $debugColor = if ($running -or $isDebug) { '#e3e9e9' } else { '#77888f' }
    Add-IdeIcon $svg 428 92 'pause' $debugColor
    Add-IdeIcon $svg 458 92 'stop' $debugColor
    Add-IdeIcon $svg 488 92 'step' $debugColor
    Add-IdeText $svg 513 105 'Step into' 'label' $debugColor
    Add-IdeText $svg 1190 105 $instance 'label' '#e9c396'
    Add-IdeText $svg 1303 105 ($status.Split('·')[0].Trim()) 'label'
    Add-IdeTabs $svg 43 127 971 $tabNames $tab
    if ($hasDebuggerDesign) {
        Add-IdeDebuggerSidebar $svg $Frame
    } else {
        $window = if ($isDebug) { 'Registers' } elseif ($story -eq 'STB10') { 'Test Explorer' } else { 'Project Explorer' }
        if ($mode -in @('floating','autohide','dock-target')) { $window = 'Project Explorer' }
        Add-IdeToolHeader $svg 1023 127 407 $window
        if ($window -eq 'Registers') {
            Add-IdeText $svg 1040 183 '68040   •   Supervisor   •   Hex' 'label' '#a8b5bb'
            $regs = if ($mode -eq 'step') {@('PC     00F82004','SR     2700','D0     00000032','A7     07FFFEC0')} elseif ($mode -in @('break','redocked') -or $Frame.p -match 'layout-restored') {@('PC     00F82000','SR     2700','D0     00000031','A7     07FFFEC0')} else {@($Frame.r)}
            $y = 218
            foreach ($line in $regs) {
                Add-IdeRect $svg 1035 ($y-19) 382 27 '#202d32'
                Add-IdeText $svg 1046 $y $line 'log'
                $y += 29
            }
            Add-IdeTabs $svg 1023 330 407 @('Registers','Call Stack','Watch') 'Registers' -Tools
            Add-IdeToolHeader $svg 1023 377 407 'Project Explorer'
        }
        $start = if ($window -eq 'Registers') { 430 } else { 188 }
        if ($window -eq 'Test Explorer') {
            Add-IdeText $svg 1041 184 '▷ Run selected        ↻ Rerun last        Filter ▾' 'label' '#a8b5bb'
            Add-IdeRect $svg 1034 205 386 29 '#344344'
            Add-IdeText $svg 1041 225 '▾  kernel-smoke' 'label'
            Add-IdeText $svg 1060 256 'Serial boot marker' 'label'
            Add-IdeText $svg 1060 281 'Breakpoint at kernel_main' 'label'
            Add-IdeText $svg 1060 306 'Memory: boot_status' 'label'
            Add-IdeText $svg 1060 331 'Screen region hash' 'label'
        } elseif ($Frame.p -match 'empty-workspace') {
            Add-IdeText $svg 1041 $start 'Open a project or folder to browse files.' 'label' '#a8b5bb'
        } else {
            $tree = if ($isUae) {
                @('▾  A1200 OS','    ▾  kernel','         ▾  src','              mmu.rs','              main.rs','    ▸  bootrom','    ▸  artifacts','    ▾  profiles','         uae40.dev','    ▸  scenarios')
            } else {
                @('▾  C64 Demo','    ▾  src','         main.asm','    ▸  artifacts','    ▾  profiles',("         " + $preset.machine.profileFile))
            }
            for ($n = 0; $n -lt $tree.Count; $n++) {
                $y = $start + 19*$n
                if ($tree[$n].Trim() -eq $tab) { Add-IdeRect $svg 1028 ($y-15) 395 18 '#354344' }
                Add-IdeText $svg 1041 $y $tree[$n] 'log'
            }
        }
        if ($window -ne 'Registers' -and $Frame.r -and $mode -notin @('floating','dock-target','autohide')) {
            Add-IdeToolHeader $svg 1023 410 407 'Session Properties'
            $y = 466
            foreach ($line in @($Frame.r)) {
                Add-IdeText $svg 1041 $y $line 'log' '#bccbd0'
                $y += 24
            }
        }
    }

    if ($hasDebuggerDesign) {
        Add-IdeDebugger $svg $Frame
    } elseif ($mode -in @('draft','error','fixed','build','break','step','floating','dock-target','redocked','autohide')) {
        Add-IdeEditor $svg $Frame $mode
    } elseif ($tab -eq 'Emulation' -and $Frame.p -notmatch 'worker-fault') {
        Add-IdeDisplay $svg $Frame $mode $instance $preset
    } else {
        Add-IdeText $svg 64 196 $Frame.h 'strong'
        $y = 249
        foreach ($line in @($Frame.c)) {
            if ($line) {
                Add-IdeRect $svg 59 ($y-19) 928 31 '#1c292e'
                Add-IdeText $svg 71 $y $line 'mono'
            }
            $y += 33
        }
        if ($tab -eq 'Start Page') { Add-IdeText $svg 71 374 'Open Project…       Connect to Service…' 'strong' '#edb77b' }
    }
    if ($tab -eq 'Emulation') {
        $playerMode = if ($Frame.player) { $Frame.player } else {
            switch -Regex ($status) {
                '^Running' {'Live'}
                '^Starting' {'Starting'}
                '^Faulted' {'Faulted'}
                default {'Paused'}
            }
        }
        $volume = if ($Frame.ContainsKey('volume')) { $Frame.volume } else { 65 }
        Add-IdePlayer $svg 43 577 $playerMode $Frame.elapsed $Frame.duration $volume -Seeking:([bool]$Frame.seeking) -VolumeFocused:([bool]$Frame.volumeFocused)
    }

    Add-IdeToolHeader $svg 43 667 1387 $output
    Add-IdeText $svg 61 722 $(switch ($output) { 'Problems' {'1 error    0 warnings    •    Current project'} 'Serial Console' {"Port: guest serial     •     $instance"} 'Breakpoints' {'Enabled   Location                       Condition'} 'MCP Activity' {"Session activity   •   $instance"} default {'Show output from: Build / Debug    ▾'} }) 'label' '#a8b5bb'
    if ($output -eq 'Problems') { Add-IdeRect $svg 54 732 1363 27 '#3b292b' }
    $logs = @($Frame.b)
    if ($isDebug -and $story -eq 'STB14') { $logs = @('●         mmu.rs:218                    always') + $logs }
    if ($story -eq 'STB05' -and $Frame.p -notmatch 'early-boot') { $logs = @($Frame.c) | Select-Object -First 3 }
    $y = 752
    foreach ($line in $logs) {
        Add-IdeText $svg 61 $y $line 'log' $(if ($output -eq 'Problems') {'#f39a97'} else {'#c4d0d4'})
        $y += 21
    }
    Add-IdeTabs $svg 43 827 1387 @('Output','Problems','Serial Console','Breakpoints','MCP Activity') $output -Tools
    Add-IdeText $svg 16 891 $status 'label'
    Add-IdeText $svg 530 891 $Frame.t 'label' '#d8e1df'
    $machineLine = if ($Frame.machineLine) { $Frame.machineLine } else { "$($preset.machine.id)   •   $($preset.machine.cpu) / $($preset.machine.video)" }
    Add-IdeText $svg 1120 891 $machineLine 'label'

    if ($mode -in @('floating','dock-target','autohide')) {
        if ($mode -eq 'autohide') {
            Add-IdeRect $svg 1397 165 33 127 '#35474b' '#edb77b'
            [void]$svg.AppendLine('<text class="label" fill="#fff0df" transform="translate(1418 271) rotate(-90)">Registers</text>')
        } else {
            Add-IdeRect $svg 745 263 407 228 '#1c292e' '#edb77b' 4
            Add-IdeToolHeader $svg 745 263 407 'Registers' -Floating
            Add-IdeText $svg 767 323 'PC    00F82000' 'mono'
            Add-IdeText $svg 767 356 'SR    2700' 'mono'
            Add-IdeText $svg 767 389 'D0    00000031' 'mono'
            Add-IdeText $svg 767 448 'UAE40-dev   •   Paused' 'label' '#a8b5bb'
            if ($mode -eq 'dock-target') {
                Add-IdeSvgComponent $svg 'ide-docking-guide'
            }
        }
    }
    if ($Frame.m) {
        Add-IdeSvgComponent $svg 'ide-confirmation-dialog'
        Add-IdeText $svg 455 354 $Frame.m 'strong'
        $y = 389
        foreach ($line in @($Frame.mc)) { Add-IdeText $svg 455 $y $line 'ui'; $y += 27 }
        Add-IdeText $svg 841 534 $Frame.a 'label' '#211b16'
    }
    [void]$svg.AppendLine('</svg>')
    [void]$svg.Insert($definitionOffset, (Get-IdeSvgDefinitions $svg.ToString() $ideComponents))
    [System.IO.File]::WriteAllText($path, $svg.ToString(), [System.Text.UTF8Encoding]::new($false))
}
