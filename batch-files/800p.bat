@echo off
REM ============================================================
REM  SteamDeck.bat
REM  Sets the primary display to 1280 x 800  --  Steam Deck
REM  Created by Claude, 2026-09-16.
REM
REM  Self-contained: no QRes / NirCmd needed. The PowerShell
REM  section below (after #PSCODE) is read back out of this file
REM  and executed; it calls the Win32 ChangeDisplaySettings API.
REM  Refresh rate and colour depth are left as-is.
REM
REM  Set RES_TESTONLY=1 before running to check that the mode is
REM  supported without actually switching resolution.
REM ============================================================

set "RES_W=1280"
set "RES_H=800"
set "RES_LABEL=Steam Deck"

powershell -NoProfile -ExecutionPolicy Bypass -Command "$s=[IO.File]::ReadAllText('%~f0'); Invoke-Expression $s.Substring($s.LastIndexOf('#PSCODE'))"
exit /b %errorlevel%

#PSCODE
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public class ResSwitch
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int   dmFields;
        public int   dmPositionX;
        public int   dmPositionY;
        public int   dmDisplayOrientation;
        public int   dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int   dmBitsPerPel;
        public int   dmPelsWidth;
        public int   dmPelsHeight;
        public int   dmDisplayFlags;
        public int   dmDisplayFrequency;
        public int   dmICMMethod;
        public int   dmICMIntent;
        public int   dmMediaType;
        public int   dmDitherType;
        public int   dmReserved1;
        public int   dmReserved2;
        public int   dmPanningWidth;
        public int   dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    static extern int EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE dm);

    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    static extern int ChangeDisplaySettings(ref DEVMODE dm, int flags);

    const int ENUM_CURRENT_SETTINGS = -1;
    const int DM_PELSWIDTH          = 0x00080000;
    const int DM_PELSHEIGHT         = 0x00100000;
    const int CDS_TEST              = 0x00000002;

    // Status: 0 ok, 1 restart needed, -1 change failed, -2 mode not supported,
    //         -100 could not read current mode, -101 already at target.
    public class Result
    {
        public int  Status;
        public int  FromW;
        public int  FromH;
        public int  Hz;
        public bool Applied;
    }

    public static Result Apply(int w, int h, bool testOnly)
    {
        Result res = new Result();

        DEVMODE dm = new DEVMODE();
        dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
        if (EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref dm) == 0)
        {
            res.Status = -100;
            return res;
        }

        res.FromW = dm.dmPelsWidth;
        res.FromH = dm.dmPelsHeight;
        res.Hz    = dm.dmDisplayFrequency;

        if (dm.dmPelsWidth == w && dm.dmPelsHeight == h)
        {
            res.Status = -101;
            return res;
        }

        dm.dmPelsWidth  = w;
        dm.dmPelsHeight = h;
        dm.dmFields     = DM_PELSWIDTH | DM_PELSHEIGHT;

        int test = ChangeDisplaySettings(ref dm, CDS_TEST);
        if (test != 0)
        {
            res.Status = (test == -2) ? -2 : test;
            return res;
        }

        if (testOnly)
        {
            res.Status = 0;
            return res;
        }

        res.Status  = ChangeDisplaySettings(ref dm, 0);
        res.Applied = (res.Status == 0);
        return res;
    }
}
'@

$w     = [int]$env:RES_W
$h     = [int]$env:RES_H
$label = $env:RES_LABEL
$dry   = ($env:RES_TESTONLY -eq '1')

$r = [ResSwitch]::Apply($w, $h, $dry)

function Hold { cmd /c pause }

switch ($r.Status) {
    0 {
        if ($dry) {
            Write-Host "TEST ONLY: $w x $h ($label) is supported. Currently $($r.FromW) x $($r.FromH); not changed."
        } else {
            Write-Host "Display set to $w x $h  ($label).   Was $($r.FromW) x $($r.FromH) @ $($r.Hz)Hz." -ForegroundColor Green
            Start-Sleep -Milliseconds 900
        }
        exit 0
    }
    -101 {
        Write-Host "Already at $w x $h ($label). Nothing to do."
        Start-Sleep -Milliseconds 900
        exit 0
    }
    -100 {
        Write-Host "ERROR: could not read the current display settings." -ForegroundColor Red
        Hold; exit 1
    }
    -2 {
        Write-Host "ERROR: $w x $h ($label) is not supported by this display / adapter." -ForegroundColor Red
        Write-Host "Display left at $($r.FromW) x $($r.FromH)."
        Hold; exit 1
    }
    1 {
        Write-Host "Mode accepted, but Windows needs a restart to apply $w x $h." -ForegroundColor Yellow
        Hold; exit 1
    }
    default {
        Write-Host "ERROR: failed to set $w x $h (code $($r.Status)). Display left at $($r.FromW) x $($r.FromH)." -ForegroundColor Red
        Hold; exit 1
    }
}
