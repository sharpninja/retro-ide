function Format-IdeMemoryTable($Frame) {
    $order = if ($Frame.byteOrder -eq 'Emulator default') { $Frame.emulatorOrder } else { $Frame.byteOrder }
    foreach ($line in $Frame.memory) {
        $bytes = ($line.Bytes | ForEach-Object { '{0:X2}' -f $_ }) -join ' '
        $words = for ($index = 0; $index -lt $line.Bytes.Count; $index += 2) {
            $first = [int]$line.Bytes[$index]
            $second = [int]$line.Bytes[$index + 1]
            $word = if ($order -eq 'Little-endian') { $first + ($second -shl 8) } else { ($first -shl 8) + $second }
            '{0:X4}' -f $word
        }
        $text = ($line.Bytes | ForEach-Object { if ($_ -ge 32 -and $_ -le 126) { [char]$_ } else { '.' } }) -join ''
        @{cells=@($line.Address,$bytes,($words -join ' '),$text)}
    }
}

function Add-IdeDebugger($Svg, $Frame) {
    Add-IdeText $Svg 66 195 $Frame.h 'strong'
    Add-IdeText $Svg 66 221 $Frame.summary 'label' '#b4c2c7'
    $controlX = 65
    foreach ($control in @($Frame.controls)) {
        $style = if ($control.state -eq 'disabled') { 'ide-debug-control-disabled' } else { 'ide-debug-control' }
        $color = if ($control.state -eq 'disabled') { '#9aabb2' } else { '#e6eaed' }
        Add-IdeSvgComponent $Svg $style $controlX 238 $control.width 30
        Add-IdeText $Svg ($controlX + 10) 258 $control.label 'label' $color
        $controlX += $control.width + 9
    }

    $rowTop = 284
    $rows = $Frame.rows
    if ($Frame.memory) {
        Add-IdeText $Svg 66 301 'Display byte order' 'label' '#b4c2c7'
        $orderX = 203
        foreach ($option in 'Emulator default','Little-endian','Big-endian') {
            $style = if ($option -eq $Frame.byteOrder) { 'ide-debug-control-selected' } else { 'ide-debug-control' }
            Add-IdeSvgComponent $Svg $style $orderX 281 145 30
            Add-IdeText $Svg ($orderX + 11) 301 $option 'label'
            $orderX += 151
        }
        $effective = if ($Frame.byteOrder -eq 'Emulator default') { $Frame.emulatorOrder } else { $Frame.byteOrder }
        Add-IdeText $Svg 675 301 "Effective: $effective" 'label' '#a7dad0'
        $rowTop = 326
        $rows = @(Format-IdeMemoryTable $Frame)
    }

    Add-IdeSvgComponent $Svg 'ide-debug-table-heading' 65 $rowTop 923 32
    foreach ($column in $Frame.columns) { Add-IdeText $Svg $column.x ($rowTop + 21) $column.label 'label' '#bfced3' }
    $rowTop += 32
    foreach ($row in $rows) {
        $style = if ($row.active) { 'ide-debug-table-active-row' } else { 'ide-debug-table-row' }
        Add-IdeSvgComponent $Svg $style 65 $rowTop 923 32
        for ($index = 0; $index -lt $Frame.columns.Count; $index++) {
            $style = if ($Frame.tab -eq 'Emulators') { 'label' } else { 'log' }
            Add-IdeText $Svg $Frame.columns[$index].x ($rowTop + 21) $row.cells[$index] $style
        }
        $rowTop += 32
    }
    Add-IdeText $Svg 66 611 $Frame.notice 'label' '#b4c2c7'
}

function Add-IdeDebuggerSidebar($Svg, $Frame) {
    $headerY = 127
    foreach ($section in $Frame.sidebar) {
        Add-IdeToolHeader $Svg 1023 $headerY 407 $section.title
        $lineY = $headerY + 58
        foreach ($line in $section.lines) {
            Add-IdeText $Svg 1040 $lineY $line 'log' '#ccdadf'
            $lineY += 28
        }
        $headerY += 241
    }
}
