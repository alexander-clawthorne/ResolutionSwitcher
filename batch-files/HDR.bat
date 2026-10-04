@echo off
REM ============================================================
REM  HDR.bat
REM  Turns HDR (Advanced Color) and Auto HDR on/off on demand.
REM  Created by Claude, 2026-09-16.
REM
REM  Usage:   HDR.bat [on / off / toggle / status]
REM           HDR.bat auto-on / auto-off         (Auto HDR only)
REM           HDR.bat on+auto / off+auto         (both together)
REM           no argument = toggle
REM
REM  HDR is switched live through the Win32 DisplayConfig API
REM  (SET_ADVANCED_COLOR_STATE) - the same thing Win+Alt+B does,
REM  but set to an explicit state instead of blind-toggled.
REM
REM  Auto HDR is a registry setting (HKCU\Software\Microsoft\
REM  DirectX\UserGpuPreferences\DirectXUserGlobalSettings). It
REM  applies to games launched AFTER the change, and only does
REM  anything while HDR itself is on. The previous value is saved
REM  under %LOCALAPPDATA%\hdr-res-scripts\autohdr.last so turning
REM  it back on restores exactly what Windows had rather than a
REM  guessed value.
REM
REM  Set HDR_QUIET=1 to suppress output (used when another script
REM  calls this one).
REM ============================================================

set "HDR_ACTION=%~1"
if "%HDR_ACTION%"=="" set "HDR_ACTION=toggle"

powershell -NoProfile -ExecutionPolicy Bypass -Command "$s=[IO.File]::ReadAllText('%~f0'); Invoke-Expression $s.Substring($s.LastIndexOf('#PSCODE'))"
exit /b %errorlevel%

#PSCODE
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public class HdrCtl
{
    [StructLayout(LayoutKind.Sequential)] public struct LUID { public uint Low; public int High; }
    [StructLayout(LayoutKind.Sequential)] struct RATIONAL { public uint Num; public uint Den; }

    // SOURCE_INFO ends with statusFlags -> 20 bytes, which makes PATH_INFO 72.
    [StructLayout(LayoutKind.Sequential)]
    struct SOURCE_INFO { public LUID adapterId; public uint id; public uint modeInfoIdx; public uint statusFlags; }

    [StructLayout(LayoutKind.Sequential)]
    struct TARGET_INFO {
        public LUID adapterId; public uint id; public uint modeInfoIdx;
        public uint outputTechnology; public uint rotation; public uint scaling;
        public RATIONAL refreshRate; public uint scanLineOrdering;
        public int targetAvailable; public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PATH_INFO { public SOURCE_INFO sourceInfo; public TARGET_INFO targetInfo; public uint flags; }

    [StructLayout(LayoutKind.Sequential)]
    struct MODE_INFO {
        public uint infoType; public uint id; public LUID adapterId;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 48)] public byte[] data;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct HEADER { public uint type; public uint size; public LUID adapterId; public uint id; }

    [StructLayout(LayoutKind.Sequential)]
    struct ADVCOLORINFO { public HEADER header; public uint value; public uint colorEncoding; public uint bitsPerColorChannel; }

    [StructLayout(LayoutKind.Sequential)]
    struct SETADVCOLOR { public HEADER header; public uint value; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct TARGETNAME {
        public HEADER header; public uint flags; public uint outputTechnology;
        public ushort edidManufactureId; public ushort edidProductCodeId; public uint connectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]  public string monitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string monitorDevicePath;
    }

    [DllImport("user32.dll")] static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPath, out uint numMode);
    [DllImport("user32.dll")] static extern int QueryDisplayConfig(uint flags, ref uint numPath, [Out] PATH_INFO[] paths, ref uint numMode, [Out] MODE_INFO[] modes, IntPtr cur);
    [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(ref ADVCOLORINFO req);
    [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(ref TARGETNAME req);
    [DllImport("user32.dll")] static extern int DisplayConfigSetDeviceInfo(ref SETADVCOLOR req);

    const uint QDC_ONLY_ACTIVE_PATHS    = 2;
    const uint GET_TARGET_NAME          = 2;
    const uint GET_ADVANCED_COLOR_INFO  = 9;
    const uint SET_ADVANCED_COLOR_STATE = 10;

    public class Display
    {
        public string Name;
        public bool   Supported;
        public bool   Enabled;
        public uint   Bpc;
        public int    SetResult = -9999;   // -9999 = no change attempted
        internal LUID Adapter;
        internal uint TargetId;
    }

    static List<Display> Enumerate()
    {
        List<Display> list = new List<Display>();
        uint np, nm;
        if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out np, out nm) != 0) return list;

        PATH_INFO[] paths = new PATH_INFO[np];
        MODE_INFO[] modes = new MODE_INFO[nm];
        if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref np, paths, ref nm, modes, IntPtr.Zero) != 0) return list;

        for (int i = 0; i < np; i++)
        {
            Display d = new Display();
            d.Adapter  = paths[i].targetInfo.adapterId;
            d.TargetId = paths[i].targetInfo.id;

            TARGETNAME tn = new TARGETNAME();
            tn.header.type = GET_TARGET_NAME;
            tn.header.size = (uint)Marshal.SizeOf(typeof(TARGETNAME));
            tn.header.adapterId = d.Adapter;
            tn.header.id = d.TargetId;
            bool gotName = DisplayConfigGetDeviceInfo(ref tn) == 0
                           && tn.monitorFriendlyDeviceName != null
                           && tn.monitorFriendlyDeviceName.Length > 0;
            d.Name = gotName ? tn.monitorFriendlyDeviceName : ("Display " + (i + 1));

            ADVCOLORINFO ac = new ADVCOLORINFO();
            ac.header.type = GET_ADVANCED_COLOR_INFO;
            ac.header.size = (uint)Marshal.SizeOf(typeof(ADVCOLORINFO));
            ac.header.adapterId = d.Adapter;
            ac.header.id = d.TargetId;
            if (DisplayConfigGetDeviceInfo(ref ac) == 0)
            {
                d.Supported = (ac.value & 1) != 0;
                d.Enabled   = (ac.value & 2) != 0;
                d.Bpc       = ac.bitsPerColorChannel;
            }
            list.Add(d);
        }
        return list;
    }

    public static Display[] Status()
    {
        return Enumerate().ToArray();
    }

    // mode: 1 = on, 0 = off, -1 = toggle each display from its own current state
    public static Display[] Set(int mode)
    {
        List<Display> list = Enumerate();
        foreach (Display d in list)
        {
            if (!d.Supported) continue;

            bool want = (mode == -1) ? !d.Enabled : (mode == 1);
            if (want == d.Enabled) { d.SetResult = 0; continue; }

            SETADVCOLOR s = new SETADVCOLOR();
            s.header.type = SET_ADVANCED_COLOR_STATE;
            s.header.size = (uint)Marshal.SizeOf(typeof(SETADVCOLOR));
            s.header.adapterId = d.Adapter;
            s.header.id = d.TargetId;
            s.value = want ? 1u : 0u;

            d.SetResult = DisplayConfigSetDeviceInfo(ref s);
            if (d.SetResult == 0) d.Enabled = want;
        }
        return list.ToArray();
    }
}
'@

$action = ($env:HDR_ACTION).Trim().ToLower()
$quiet  = ($env:HDR_QUIET -eq '1')

function Say($msg, $colour) {
    if (-not $quiet) {
        if ($colour) { Write-Host $msg -ForegroundColor $colour } else { Write-Host $msg }
    }
}

# ---------- Auto HDR lives in the registry ----------
$gpuKey    = 'HKCU:\Software\Microsoft\DirectX\UserGpuPreferences'
$gpuValue  = 'DirectXUserGlobalSettings'
$stateDir  = Join-Path $env:LOCALAPPDATA 'hdr-res-scripts'
$stateFile = Join-Path $stateDir 'autohdr.last'

function Get-GlobalSettings {
    if (-not (Test-Path $gpuKey)) { return '' }
    $p = Get-ItemProperty -Path $gpuKey -Name $gpuValue -ErrorAction SilentlyContinue
    if ($null -eq $p) { return '' }
    return [string]$p.$gpuValue
}

function Get-AutoHdrValue {
    $s = Get-GlobalSettings
    if ($s -match 'AutoHDREnable=(\d+)') { return [int]$matches[1] }
    return $null
}

function Set-AutoHdrValue($newVal) {
    $s = Get-GlobalSettings
    if ($s -match 'AutoHDREnable=\d+') {
        $s = $s -replace 'AutoHDREnable=\d+', ('AutoHDREnable=' + $newVal)
    } else {
        if ($s.Length -gt 0 -and -not $s.EndsWith(';')) { $s += ';' }
        $s += ('AutoHDREnable=' + $newVal + ';')
    }
    if (-not (Test-Path $gpuKey)) { New-Item -Path $gpuKey -Force | Out-Null }
    New-ItemProperty -Path $gpuKey -Name $gpuValue -Value $s -PropertyType String -Force | Out-Null
}

function Enable-AutoHdr {
    $cur = Get-AutoHdrValue
    if ($null -ne $cur -and $cur -ne 0) { Say "Auto HDR already on (AutoHDREnable=$cur)."; return }
    # Prefer the exact value Windows last had, so no value is invented.
    $restore = 2048
    if (Test-Path $stateFile) {
        $saved = Get-Content $stateFile -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($saved -match '^\d+$' -and [int]$saved -ne 0) { $restore = [int]$saved }
    }
    Set-AutoHdrValue $restore
    Say "Auto HDR ON  (AutoHDREnable=$restore) - applies to games started from now on." Green
}

function Disable-AutoHdr {
    $cur = Get-AutoHdrValue
    if ($null -ne $cur -and $cur -ne 0) {
        if (-not (Test-Path $stateDir)) { New-Item -ItemType Directory -Path $stateDir -Force | Out-Null }
        Set-Content -Path $stateFile -Value $cur -Encoding ASCII
    }
    Set-AutoHdrValue 0
    Say "Auto HDR OFF." Yellow
}

function Show($displays) {
    foreach ($d in $displays) {
        if (-not $d.Supported) {
            Say ('  {0,-28} HDR not supported by this display' -f $d.Name) DarkGray
        } elseif ($d.SetResult -ne -9999 -and $d.SetResult -ne 0) {
            Say ('  {0,-28} FAILED to change HDR (code {1})' -f $d.Name, $d.SetResult) Red
        } else {
            $state = 'OFF'
            $col   = 'Gray'
            if ($d.Enabled) { $state = 'ON '; $col = 'Green' }
            Say ('  {0,-28} HDR {1}  ({2} bits per channel)' -f $d.Name, $state, $d.Bpc) $col
        }
    }
}

$failed = $false

switch -regex ($action) {
    '^status$' {
        Say 'HDR status:'
        Show ([HdrCtl]::Status())
        $a = Get-AutoHdrValue
        if ($null -eq $a)  { Say '  Auto HDR                     not configured' }
        elseif ($a -eq 0)  { Say '  Auto HDR                     OFF' }
        else               { Say ('  Auto HDR                     ON  (AutoHDREnable={0})' -f $a) Green }
        if (-not $quiet) { cmd /c pause }
        exit 0
    }
    '^(on|off|toggle)(\+auto)?$' {
        $mode = -1
        if ($action.StartsWith('on'))  { $mode = 1 }
        if ($action.StartsWith('off')) { $mode = 0 }

        $res = [HdrCtl]::Set($mode)
        Say 'HDR:'
        Show $res
        foreach ($d in $res) { if ($d.Supported -and $d.SetResult -ne 0) { $failed = $true } }

        if ($action -like '*+auto*') {
            $nowOn = $mode -eq 1
            if ($mode -eq -1) { $nowOn = @($res | Where-Object { $_.Enabled }).Count -gt 0 }
            if ($nowOn) { Enable-AutoHdr } else { Disable-AutoHdr }
        }
    }
    '^auto-on$'  { Enable-AutoHdr }
    '^auto-off$' { Disable-AutoHdr }
    default {
        Say "Unknown action '$action'." Red
        Say 'Usage: HDR.bat [on | off | toggle | status | auto-on | auto-off | on+auto | off+auto]'
        if (-not $quiet) { cmd /c pause }
        exit 2
    }
}

if ($failed) {
    if (-not $quiet) { cmd /c pause }
    exit 1
}
if (-not $quiet) { Start-Sleep -Milliseconds 900 }
exit 0
