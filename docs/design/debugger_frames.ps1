$c64Machine = @{title='Machine';lines=@('Target      C64-vice','CPU         MOS 6510 (6502 family)','Descriptor  v1 / map r17','Space       CPU, 16-bit','Native      little-endian')}
$c64Registers = @{title='Registers';lines=@('PC  $C000    SP  $F8','A    $2A    X   $00    Y  $00','P    $24    N V - B D I Z C','             0 0 1 0 0 1 0 0')}
$disassemblyColumns = @(
    @{x=78;label='Address'}, @{x=231;label='Opcode bytes'}, @{x=400;label='Instruction'},
    @{x=656;label='Reference cycles'}, @{x=822;label='Symbol'}
)
$memoryColumns = @(
    @{x=78;label='Address'}, @{x=231;label='Raw bytes'}, @{x=536;label='16-bit values'}, @{x=823;label='Text'}
)
$c64Memory = @{
    target='vice'; i='C64-vice'; s='Paused'; tab='Memory'; h='Memory'
    summary='MOS 6510 | CPU view | RAM region $C000-$CFFF | map r17'
    controls=@(@{label='Address  $C000';width=152},@{label='Bank: CPU view';width=165},@{label='Group: 16-bit';width=145})
    columns=$memoryColumns
    memory=@(
        @{Address='$C000';Bytes=@(0x12,0x34,0x41,0x42,0x43,0x44,0x00,0xFF)},
        @{Address='$C008';Bytes=@(0x41,0x42,0x55,0xAA,0x68,0x65,0x6C,0x6C)},
        @{Address='$C010';Bytes=@(0x6F,0x00,0x12,0x34,0x0D,0x0A,0x02,0x01)}
    )
    emulatorOrder='Little-endian'
    sidebar=@($c64Machine,@{title='Memory map';lines=@('$C000-$CFFF  RAM, read/write','$D000-$DFFF  I/O in CPU view','$D000-$DFFF  RAM in physical bank','I/O requires explicit device access')})
    b=@('Paused inspection. RAM data is available in the selected address space.')
}
$emulatorDefault = $c64Memory.Clone()
$emulatorDefault.p = 'STB17/04-memory-default.svg'
$emulatorDefault.t = 'Memory uses emulator byte order'
$emulatorDefault.byteOrder = 'Emulator default'
$emulatorDefault.notice = 'The emulator supplies the little-endian default for this address space.'
$bigEndian = $c64Memory.Clone()
$bigEndian.p = 'STB17/05-memory-big-endian.svg'
$bigEndian.t = 'Memory uses saved Big-endian preference'
$bigEndian.byteOrder = 'Big-endian'
$bigEndian.notice = 'Big-endian saved as your debugger display preference.'
$littleEndian = $c64Memory.Clone()
$littleEndian.p = 'STB17/09-memory-little-endian.svg'
$littleEndian.t = 'Memory uses saved Little-endian preference'
$littleEndian.byteOrder = 'Little-endian'
$littleEndian.notice = 'Little-endian saved as your debugger display preference.'

@(
    @{
        p='STB17/01-emulator-package.svg'; target='vice'; i='No instance'; s='Ready'; tab='Emulators'
        t='Emulator package and debugger profiles'; h='Emulators'; summary='Installed packages in the configured bucket'
        controls=@(@{label='Installed';width=120},@{label='Refresh bucket';width=150},@{label='Open pinned target';width=176})
        columns=@(@{x=78;label='Installed package'},@{x=310;label='Target preset'},@{x=503;label='Version pin'},@{x=655;label='Debugger profile'})
        rows=@(
            @{active=$true;cells=@('VICE','Commodore 64','3.9 (fixture)','c64.monitor / v1')},
            @{cells=@('UAE Developer','UAE40','dev.4 (fixture)','amiga.m68k / v1')},
            @{cells=@('Generic monitor','Attach to existing host','1.0 (fixture)','Raw inspection')}
        )
        sidebar=@(
            @{title='Package details';lines=@('Installed archive verified','Manifest revision pinned','Binding: VICE binary monitor','c64.monitor profile data')},
            @{title='Debugger descriptors';lines=@('Machine config is read at attach.','CPU reference families:','6502, 6809, Z80, 8080, 8086,','6800, and 68000')}
        )
        notice='Select a pinned target to load its live machine description.'
        b=@('Package and version are pinned by the project.')
    },
    @{
        p='STB17/02-c64-disassembly.svg'; target='vice'; i='C64-vice'; s='Paused'; tab='Disassembly'
        t='C64 disassembly and registers'; h='Disassembly'; summary='MOS 6510 | 6502 family | CPU view | c64.monitor v1'
        controls=@(@{label='CPU: Main 6510';width=160},@{label='Address  $C000';width=150},@{label='Open Memory';width=146},@{label='Clock phase unavailable';width=205;state='disabled'})
        columns=$disassemblyColumns
        rows=@(
            @{active=$true;cells=@('$C000','A9 2A','LDA #$2A','2','start')},
            @{cells=@('$C002','8D 00 C1','STA $C100','4','')},
            @{cells=@('$C005','EA','NOP','2','')}
        )
        sidebar=@($c64Machine,$c64Registers)
        notice='Cycles are instruction reference values. This session does not expose live phase events.'
        b=@('Bound descriptor v1 and map r17 to the selected C64 session.')
    },
    @{
        p='STB17/03-clock-phase.svg'; target='vice'; project='Clock Fixture'; connection='W65C02S fixture'; machineLine='W65C02S | 6502 family'
        i='Clock fixture'; s='Paused'; tab='Clock'; t='Instruction timing and live clock phase'; h='Clock and bus trace'
        summary='W65C02S fixture | CPU clock domain | LDA #$2A: nominal 2 cycles'
        controls=@(@{label='Step instruction';width=160},@{label='Step cycle';width=134},@{label='Step phase';width=133},@{label='Trace source: Live events';width=211})
        columns=@(@{x=78;label='Observed cycle'},@{x=235;label='Clock phase'},@{x=421;label='Bus event'},@{x=667;label='Address'},@{x=822;label='Data'})
        rows=@(
            @{cells=@('9040','PHI2 high','Opcode fetch','$2000','A9')},
            @{active=$true;cells=@('9041','PHI2 high','Operand read','$2001','2A')}
        )
        sidebar=@(
            @{title='Machine';lines=@('Target      Clock fixture','CPU         W65C02S','Descriptor  v1 / map r3','Clock       CPU domain','Native      little-endian')},
            @{title='Timing';lines=@('Reference: LDA immediate, 2 cycles','Observed: cycle 9041, PHI2 high','Advertised: cycle and phase stepping')}
        )
        notice='Reference cycles and the selected live bus event have separate sources.'
        b=@('The fixture advertised phase stepping and supplied the displayed bus events.')
    },
    $emulatorDefault,
    $bigEndian,
    @{
        p='STB17/06-memory-bank.svg'; target='vice'; i='C64-vice'; s='Paused'; tab='Memory'; h='Memory'
        t='Inspect RAM under the C64 I/O window'; summary='MOS 6510 | physical RAM bank | selected window $D000-$DFFF | map r18'
        controls=@(@{label='Address  $D000';width=152},@{label='Bank: RAM under I/O';width=190},@{label='Group: 16-bit';width=145})
        columns=$memoryColumns; byteOrder='Big-endian'; emulatorOrder='Little-endian'
        memory=@(
            @{Address='$D000';Bytes=@(0x12,0x34,0x41,0x42,0x43,0x44,0x00,0xFF)},
            @{Address='$D008';Bytes=@(0x45,0x46,0x47,0x48,0x00,0x01,0x02,0x03)}
        )
        sidebar=@(
            @{title='Machine';lines=@('Target      C64-vice','CPU         MOS 6510 (6502 family)','Descriptor  v1 / map r18','Space       RAM bank, 16-bit','Native      little-endian')},
            @{title='Memory map';lines=@('$D000-$DFFF  RAM, inspected bank','$D000-$DFFF  I/O in CPU view','Reads are scoped to the RAM bank.')}
        )
        notice='The selected bank resolves $D000 to RAM. Device access is separate.'
        b=@('Map r18 refreshed. The debugger applied the saved display preference to the new bank read.')
    },
    @{
        p='STB17/07-uae-disassembly.svg'; target='uae'; i='UAE40-dev'; s='Paused'; tab='Disassembly'
        t='Switch to UAE40 disassembly'; h='Disassembly'; summary='Motorola 68040 | 68000 family | supervisor space | amiga.m68k v1'
        controls=@(@{label='CPU: Main 68040';width=168},@{label='Address  $00F80000';width=178},@{label='Open Memory';width=146},@{label='Clock phase unavailable';width=205;state='disabled'})
        columns=$disassemblyColumns
        rows=@(
            @{active=$true;cells=@('$00F80000','4E 71','NOP','Unavailable','rom_entry')},
            @{cells=@('$00F80002','70 01','MOVEQ #1,D0','Unavailable','')},
            @{cells=@('$00F80004','4E 75','RTS','Unavailable','')}
        )
        sidebar=@(
            @{title='Machine';lines=@('Target      UAE40-dev','CPU         68040 (68000 family)','Descriptor  v1 / map r6','Space       supervisor, 32-bit','Native      big-endian')},
            @{title='Registers';lines=@('PC   $00F80000  SR  $2700','D0   $00000001','A7   $07FFFEC0')}
        )
        notice='The new target supplies its own registers, address width, map revision, and byte order.'
        b=@('Switched debugger context from C64-vice to UAE40-dev.')
    },
    @{
        p='STB17/08-raw-inspection.svg'; target='vice'; project='Legacy Target'; connection='Legacy monitor'; machineLine='Raw inspection | legacy monitor'
        i='Legacy monitor'; s='Paused'; tab='Disassembly'; t='Raw inspection without a machine descriptor'; h='Raw inspection'
        summary='Machine descriptor unavailable | legacy monitor supports bounded raw reads'
        controls=@(@{label='Read raw bytes';width=160},@{label='Disassemble';width=145;state='disabled'},@{label='Step phase';width=133;state='disabled'})
        columns=@(@{x=78;label='Raw address'},@{x=301;label='Returned bytes'},@{x=591;label='Availability'})
        rows=@(@{active=$true;cells=@('C000','12 34 41 42','Read succeeded')})
        sidebar=@(
            @{title='Machine';lines=@('Target      Legacy monitor','CPU         Unknown','Descriptor  Unavailable','Byte order  Unknown')},
            @{title='Available inspection';lines=@('Bounded raw memory reads','CPU-specific decode unavailable','Clock-phase events unavailable')}
        )
        notice='Connect a compatible descriptor to enable the platform debugger views.'
        b=@('The active session continues to expose its supported raw reads.')
    },
    $littleEndian
)
