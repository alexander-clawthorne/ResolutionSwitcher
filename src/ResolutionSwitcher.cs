// ============================================================
//  ResolutionSwitcher.cs
//  Controller-driven resolution picker, built to be added to
//  Steam as a non-Steam game.
//  Created by Claude, 2026-09-17.
//
//  Build:  build.bat   (uses the C# compiler shipped with
//                       Windows - no SDK or downloads needed)
//
//  Controls:  D-pad / left stick  - move
//             A  or  Enter        - apply the highlighted mode
//             B  / Back / Start / Esc - quit
//
//  "Add a resolution..." opens an on-screen numpad: enter width,
//  press OK, enter height, press OK. The mode is validated, applied
//  and saved to
//      %LOCALAPPDATA%\ResolutionSwitcher\custom-resolutions.txt
//  so it appears in the menu from then on.
//
//  X (or Delete) on a Custom row removes it, confirmed by a second
//  press. Only rows you added can be deleted; the built-in modes
//  are fixed. That file is also plain text, so a line can be removed
//  by hand.
//
//  Resolution is changed through the Win32 ChangeDisplaySettings
//  API. Every mode is validated with CDS_TEST first, so an
//  unsupported mode is greyed out and can never blank the panel.
//  Refresh rate and colour depth are left untouched.
//
//  Gamepad input is read straight from XInput rather than relying
//  on Steam Input's keyboard emulation, so it works whether it is
//  launched from Steam, Big Picture, or the desktop.
// ============================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

// ---------- display mode changing ----------
static class Native
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct DEVMODE
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

    [DllImport("user32.dll")]
    public static extern bool SetProcessDPIAware();

    const int ENUM_CURRENT_SETTINGS = -1;
    const int DM_PELSWIDTH          = 0x00080000;
    const int DM_PELSHEIGHT         = 0x00100000;
    const int CDS_TEST              = 0x00000002;

    public static bool GetCurrent(out int w, out int h, out int hz)
    {
        w = h = hz = 0;
        DEVMODE dm = new DEVMODE();
        dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
        if (EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref dm) == 0) return false;
        w  = dm.dmPelsWidth;
        h  = dm.dmPelsHeight;
        hz = dm.dmDisplayFrequency;
        return true;
    }

    // flags == CDS_TEST only validates; 0 actually applies.
    static int Change(int w, int h, int flags)
    {
        DEVMODE dm = new DEVMODE();
        dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
        if (EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref dm) == 0) return -100;

        dm.dmPelsWidth  = w;
        dm.dmPelsHeight = h;
        dm.dmFields     = DM_PELSWIDTH | DM_PELSHEIGHT;
        return ChangeDisplaySettings(ref dm, flags);
    }

    public static bool IsSupported(int w, int h) { return Change(w, h, CDS_TEST) == 0; }

    // 0 = applied, 1 = needs restart, anything else = failed.
    public static int Apply(int w, int h)
    {
        int test = Change(w, h, CDS_TEST);
        if (test != 0) return test;
        return Change(w, h, 0);
    }
}

// ---------- gamepad ----------
static class Pad
{
    [StructLayout(LayoutKind.Sequential)]
    public struct GAMEPAD
    {
        public ushort wButtons;
        public byte   bLeftTrigger;
        public byte   bRightTrigger;
        public short  sThumbLX, sThumbLY, sThumbRX, sThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct STATE { public uint dwPacketNumber; public GAMEPAD Gamepad; }

    [DllImport("xinput1_4.dll",   EntryPoint = "XInputGetState")] static extern uint Get14 (uint i, ref STATE s);
    [DllImport("xinput1_3.dll",   EntryPoint = "XInputGetState")] static extern uint Get13 (uint i, ref STATE s);
    [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")] static extern uint Get910(uint i, ref STATE s);

    public const ushort DPAD_UP = 0x0001, DPAD_DOWN = 0x0002, DPAD_LEFT = 0x0004, DPAD_RIGHT = 0x0008;
    public const ushort START = 0x0010, BACK = 0x0020;
    public const ushort A = 0x1000, B = 0x2000, X = 0x4000;

    static int which = -1;   // 0 = 1_4, 1 = 1_3, 2 = 9_1_0, 3 = none available

    public static string BackendName()
    {
        Probe();
        switch (which) { case 0: return "xinput1_4"; case 1: return "xinput1_3"; case 2: return "xinput9_1_0"; }
        return "none";
    }

    static void Probe()
    {
        if (which != -1) return;
        STATE s = new STATE();
        try { Get14(0, ref s);  which = 0; return; } catch (DllNotFoundException) { } catch (EntryPointNotFoundException) { }
        try { Get13(0, ref s);  which = 1; return; } catch (DllNotFoundException) { } catch (EntryPointNotFoundException) { }
        try { Get910(0, ref s); which = 2; return; } catch (DllNotFoundException) { } catch (EntryPointNotFoundException) { }
        which = 3;
    }

    // Merges all four XInput slots, so it does not matter which one Steam hands us.
    public static bool Poll(out GAMEPAD merged)
    {
        merged = new GAMEPAD();
        Probe();
        if (which == 3) return false;

        bool any = false;
        for (uint i = 0; i < 4; i++)
        {
            STATE s = new STATE();
            uint rc;
            switch (which)
            {
                case 0:  rc = Get14(i, ref s);  break;
                case 1:  rc = Get13(i, ref s);  break;
                default: rc = Get910(i, ref s); break;
            }
            if (rc != 0) continue;   // 1167 = ERROR_DEVICE_NOT_CONNECTED
            any = true;
            merged.wButtons |= s.Gamepad.wButtons;
            if (Math.Abs((int)s.Gamepad.sThumbLY) > Math.Abs((int)merged.sThumbLY)) merged.sThumbLY = s.Gamepad.sThumbLY;
            if (Math.Abs((int)s.Gamepad.sThumbLX) > Math.Abs((int)merged.sThumbLX)) merged.sThumbLX = s.Gamepad.sThumbLX;
        }
        return any;
    }
}

// ---------- saved custom resolutions ----------
static class Store
{
    static string Folder()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "ResolutionSwitcher");
    }

    public static string FilePath() { return Path.Combine(Folder(), "custom-resolutions.txt"); }

    // Anything unreadable is ignored rather than fatal - a corrupt file
    // should never stop the menu from opening.
    public static List<int[]> Load()
    {
        List<int[]> list = new List<int[]>();
        try
        {
            string f = FilePath();
            if (!File.Exists(f)) return list;

            foreach (string raw in File.ReadAllLines(f))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;

                string[] parts = line.Split('x', 'X', '*');
                if (parts.Length != 2) continue;

                int w, h;
                if (!int.TryParse(parts[0].Trim(), out w)) continue;
                if (!int.TryParse(parts[1].Trim(), out h)) continue;
                if (w <= 0 || h <= 0) continue;

                bool dup = false;
                foreach (int[] e in list) if (e[0] == w && e[1] == h) dup = true;
                if (!dup) list.Add(new int[] { w, h });
            }
        }
        catch { }
        return list;
    }

    static void Save(List<int[]> list)
    {
        Directory.CreateDirectory(Folder());
        List<string> lines = new List<string>();
        lines.Add("# Custom resolutions for ResolutionSwitcher.exe");
        lines.Add("# One WIDTHxHEIGHT per line. Delete a line to drop it from the menu.");
        foreach (int[] e in list) lines.Add(e[0] + "x" + e[1]);
        File.WriteAllLines(FilePath(), lines.ToArray());
    }

    public static void Add(int w, int h)
    {
        try
        {
            List<int[]> list = Load();
            foreach (int[] e in list) if (e[0] == w && e[1] == h) return;
            list.Add(new int[] { w, h });
            Save(list);
        }
        catch { }
    }

    public static void Remove(int w, int h)
    {
        try
        {
            List<int[]> kept = new List<int[]>();
            foreach (int[] e in Load()) if (!(e[0] == w && e[1] == h)) kept.Add(e);
            Save(kept);
        }
        catch { }
    }
}

// ---------- the menu ----------
class Mode
{
    public string Label;
    public int    W, H;
    public bool   Supported = true;
    public bool   IsExit;
    public bool   IsAdd;
    public bool   IsCustom;
}

class MenuForm : Form
{
    readonly List<Mode> modes;
    int    index;
    public string status = "";
    public Color  statusColour = Color.Gainsboro;

    readonly Timer poll = new Timer();
    ushort prevButtons;
    int    stickHeld;          // ticks the stick has been held in one direction
    int    prevStickDir;

    // ----- "Add a resolution" numpad page -----
    enum Page { Menu, Numpad }
    Page   page = Page.Menu;

    static readonly string[,] KEYS = new string[4, 3] {
        { "7", "8", "9"   },
        { "4", "5", "6"   },
        { "1", "2", "3"   },
        { "0", "DEL", "OK" }
    };

    string padW = "", padH = "";
    int    padField;           // 0 = width, 1 = height
    int    padRow, padCol;
    string padStatus = "";
    Color  padStatusColour = Color.Gainsboro;

    int    pendingDelete = -1;   // index awaiting a second X press

    public MenuForm(List<Mode> m)
    {
        modes = m;

        FormBorderStyle = FormBorderStyle.None;
        WindowState     = FormWindowState.Maximized;
        TopMost         = true;
        BackColor       = Color.FromArgb(16, 16, 20);
        DoubleBuffered  = true;
        KeyPreview      = true;
        Text            = "Resolution Switcher";

        // Start on whatever the display is already set to.
        int cw, ch, chz;
        if (Native.GetCurrent(out cw, out ch, out chz))
            for (int i = 0; i < modes.Count; i++)
                if (modes[i].W == cw && modes[i].H == ch) { index = i; break; }

        poll.Interval = 33;
        poll.Tick += OnPoll;
        poll.Start();

        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
    }

    void OnDisplayChanged(object sender, EventArgs e)
    {
        Bounds = Screen.PrimaryScreen.Bounds;
        Invalidate();
    }

    protected override void OnLoad(EventArgs e)
    {
        Cursor.Hide();
        base.OnLoad(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        poll.Stop();
        Cursor.Show();
        base.OnFormClosed(e);
    }

    void MoveSel(int delta)
    {
        index = (index + delta + modes.Count) % modes.Count;
        status = "";
        pendingDelete = -1;      // moving away cancels a pending delete
        Invalidate();
    }

    // Deleting needs two presses, so a stray X never silently drops an entry.
    void DeleteSelected()
    {
        Mode m = modes[index];

        if (!m.IsCustom)
        {
            pendingDelete = -1;
            status = "Only resolutions you added can be deleted";
            statusColour = Color.FromArgb(235, 200, 110);
            Invalidate();
            return;
        }

        if (pendingDelete != index)
        {
            pendingDelete = index;
            status = "Press X again to delete " + m.W + " x " + m.H;
            statusColour = Color.FromArgb(235, 200, 110);
            Invalidate();
            return;
        }

        Store.Remove(m.W, m.H);
        modes.RemoveAt(index);
        if (index >= modes.Count) index = modes.Count - 1;

        pendingDelete = -1;
        status = "Deleted " + m.W + " x " + m.H;
        statusColour = Color.FromArgb(170, 170, 180);
        Invalidate();
    }

    // Used by --shot to preview the delete confirmation.
    public void PreviewDelete()
    {
        for (int i = 0; i < modes.Count; i++)
            if (modes[i].IsCustom) { index = i; DeleteSelected(); return; }
    }

    // ---------- numpad page ----------
    void OpenNumpad()
    {
        page      = Page.Numpad;
        padW      = "";
        padH      = "";
        padField  = 0;
        padRow    = 0;
        padCol    = 0;
        padStatus = "";
        status    = "";
        Invalidate();
    }

    void PadMove(int dRow, int dCol)
    {
        padRow = (padRow + dRow + 4) % 4;
        padCol = (padCol + dCol + 3) % 3;
        Invalidate();
    }

    void PadDigit(char c)
    {
        if (padField == 0) { if (padW.Length < 5) padW += c; }
        else               { if (padH.Length < 5) padH += c; }
        padStatus = "";
        Invalidate();
    }

    void PadDelete()
    {
        if (padField == 0)
        {
            if (padW.Length > 0) padW = padW.Substring(0, padW.Length - 1);
        }
        else if (padH.Length > 0) padH = padH.Substring(0, padH.Length - 1);
        else padField = 0;               // empty height -> step back to width
        padStatus = "";
        Invalidate();
    }

    void PadFail(string msg)
    {
        padStatus = msg;
        padStatusColour = Color.FromArgb(235, 110, 110);
        Invalidate();
    }

    void PadOk()
    {
        int w, h;

        if (padField == 0)
        {
            if (!int.TryParse(padW, out w) || w < 320 || w > 16384) { PadFail("Width must be between 320 and 16384"); return; }
            padField = 1;
            padStatus = "";
            Invalidate();
            return;
        }

        if (!int.TryParse(padW, out w) || w < 320 || w > 16384) { PadFail("Width must be between 320 and 16384"); return; }
        if (!int.TryParse(padH, out h) || h < 200 || h > 16384) { PadFail("Height must be between 200 and 16384"); return; }

        if (!Native.IsSupported(w, h)) { PadFail(w + " x " + h + " is not supported by this display"); return; }

        // Already in the list? Just jump to it rather than adding a duplicate.
        for (int i = 0; i < modes.Count; i++)
        {
            Mode e = modes[i];
            if (!e.IsExit && !e.IsAdd && e.W == w && e.H == h)
            {
                page  = Page.Menu;
                index = i;
                Activate(e);
                return;
            }
        }

        Store.Add(w, h);

        int at = modes.Count;
        for (int i = 0; i < modes.Count; i++) if (modes[i].IsAdd) { at = i; break; }

        Mode nm = new Mode { Label = "Custom", W = w, H = h, Supported = true, IsCustom = true };
        modes.Insert(at, nm);

        page  = Page.Menu;
        index = at;
        Activate(nm);
    }

    void PadPress()
    {
        string k = KEYS[padRow, padCol];
        if (k == "OK")       PadOk();
        else if (k == "DEL") PadDelete();
        else                 PadDigit(k[0]);
    }

    void Activate(Mode m)
    {
        pendingDelete = -1;
        if (m.IsExit) { Close(); return; }
        if (m.IsAdd)  { OpenNumpad(); return; }

        if (!m.Supported)
        {
            status = m.Label + " is not supported by this display";
            statusColour = Color.FromArgb(235, 110, 110);
            Invalidate();
            return;
        }

        int rc = Native.Apply(m.W, m.H);
        if (rc == 0)
        {
            status = "Applied  " + m.W + " x " + m.H;
            statusColour = Color.FromArgb(120, 220, 140);
        }
        else if (rc == 1)
        {
            status = "Windows needs a restart to apply that mode";
            statusColour = Color.FromArgb(235, 200, 110);
        }
        else
        {
            status = "Failed to apply " + m.W + " x " + m.H + "  (code " + rc + ")";
            statusColour = Color.FromArgb(235, 110, 110);
        }
        Bounds = Screen.PrimaryScreen.Bounds;
        Invalidate();
    }

    void OnPoll(object sender, EventArgs e)
    {
        Pad.GAMEPAD g;
        if (!Pad.Poll(out g)) { prevButtons = 0; return; }

        ushort pressed = (ushort)(g.wButtons & ~prevButtons);
        prevButtons = g.wButtons;

        if (page == Page.Numpad)
        {
            if ((pressed & Pad.DPAD_UP)    != 0) PadMove(-1, 0);
            if ((pressed & Pad.DPAD_DOWN)  != 0) PadMove(+1, 0);
            if ((pressed & Pad.DPAD_LEFT)  != 0) PadMove(0, -1);
            if ((pressed & Pad.DPAD_RIGHT) != 0) PadMove(0, +1);
            if ((pressed & Pad.A)          != 0) PadPress();
            if ((pressed & (Pad.B | Pad.BACK)) != 0) { page = Page.Menu; Invalidate(); }
        }
        else
        {
            if ((pressed & Pad.DPAD_UP)   != 0) MoveSel(-1);
            if ((pressed & Pad.DPAD_DOWN) != 0) MoveSel(+1);
            if ((pressed & Pad.A)         != 0) Activate(modes[index]);
            if ((pressed & Pad.X)         != 0) DeleteSelected();
            if ((pressed & (Pad.B | Pad.BACK | Pad.START)) != 0) { Close(); return; }
        }

        // Left stick, with an initial delay then repeat.
        // code: 1 = up, 2 = down, 3 = left, 4 = right.
        const short DEAD = 16000;
        int code = 0;
        if      (g.sThumbLY >  DEAD) code = 1;
        else if (g.sThumbLY < -DEAD) code = 2;
        else if (page == Page.Numpad && g.sThumbLX < -DEAD) code = 3;
        else if (page == Page.Numpad && g.sThumbLX >  DEAD) code = 4;

        if (code == 0) { stickHeld = 0; prevStickDir = 0; }
        else
        {
            bool fire = false;
            if (code != prevStickDir) { fire = true; stickHeld = 0; prevStickDir = code; }
            else { stickHeld++; if (stickHeld > 12 && stickHeld % 4 == 0) fire = true; }
            if (fire) StickStep(code);
        }
    }

    void StickStep(int code)
    {
        if (page == Page.Numpad)
        {
            if      (code == 1) PadMove(-1, 0);
            else if (code == 2) PadMove(+1, 0);
            else if (code == 3) PadMove(0, -1);
            else                PadMove(0, +1);
        }
        else
        {
            if      (code == 1) MoveSel(-1);
            else if (code == 2) MoveSel(+1);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (page == Page.Numpad)
        {
            int digit = -1;
            if (e.KeyCode >= Keys.D0 && e.KeyCode <= Keys.D9)                digit = e.KeyCode - Keys.D0;
            if (e.KeyCode >= Keys.NumPad0 && e.KeyCode <= Keys.NumPad9)      digit = e.KeyCode - Keys.NumPad0;

            if (digit >= 0)                       PadDigit((char)('0' + digit));
            else if (e.KeyCode == Keys.Back)      PadDelete();
            else if (e.KeyCode == Keys.Enter)     PadOk();
            else if (e.KeyCode == Keys.Space)     PadPress();
            else if (e.KeyCode == Keys.Tab)       { padField = 1 - padField; Invalidate(); }
            else if (e.KeyCode == Keys.Up)        PadMove(-1, 0);
            else if (e.KeyCode == Keys.Down)      PadMove(+1, 0);
            else if (e.KeyCode == Keys.Left)      PadMove(0, -1);
            else if (e.KeyCode == Keys.Right)     PadMove(0, +1);
            else if (e.KeyCode == Keys.Escape)    { page = Page.Menu; Invalidate(); }
            base.OnKeyDown(e);
            return;
        }

        if (e.KeyCode == Keys.Up)                                   MoveSel(-1);
        else if (e.KeyCode == Keys.Down)                            MoveSel(+1);
        else if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) Activate(modes[index]);
        else if (e.KeyCode == Keys.Delete)                          DeleteSelected();
        else if (e.KeyCode == Keys.Escape)                          Close();
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Draw(e.Graphics, ClientSize.Width, ClientSize.Height);
    }

    // Shared with --shot, which renders the menu without opening a window.
    public void Draw(Graphics g, int w, int h)
    {
        g.Clear(BackColor);
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        if (page == Page.Numpad) DrawNumpad(g, w, h); else DrawMenu(g, w, h);
    }

    void DrawMenu(Graphics g, int w, int h)
    {
        float scale = Math.Min(w / 1280f, h / 800f);
        if (scale < 0.5f) scale = 0.5f;

        using (Font titleFont  = new Font("Segoe UI", 26f * scale, FontStyle.Bold))
        using (Font smallFont  = new Font("Segoe UI", 12f * scale, FontStyle.Regular))
        using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
        {
            int cw, ch, chz;
            Native.GetCurrent(out cw, out ch, out chz);

            g.DrawString("Resolution", titleFont, Brushes.White, new RectangleF(0, h * 0.08f, w, titleFont.Height * 1.4f), sf);
            using (Brush dim = new SolidBrush(Color.FromArgb(150, 150, 160)))
                g.DrawString("currently " + cw + " x " + ch + "  @ " + chz + " Hz", smallFont,
                             dim, new RectangleF(0, h * 0.08f + titleFont.Height * 1.35f, w, smallFont.Height * 1.6f), sf);

            // The list grows as custom resolutions are added, so shrink the
            // rows until the whole thing fits between the header and the hint
            // line rather than letting it run over them.
            float availTop = h * 0.08f + titleFont.Height * 1.35f + smallFont.Height * 2.4f;
            float availBot = h - smallFont.Height * 5f;
            float avail    = Math.Max(availBot - availTop, smallFont.Height * 3f);

            Font itemFont = null;
            for (float size = 21f * scale; ; size -= 1f)
            {
                if (itemFont != null) itemFont.Dispose();
                itemFont = new Font("Segoe UI", size, FontStyle.Regular);
                if (itemFont.Height * 1.85f * modes.Count <= avail || size <= 8f) break;
            }

            float rowH  = itemFont.Height * 1.85f;
            float listH = rowH * modes.Count;
            float top   = availTop + (avail - listH) / 2f;
            float boxW  = Math.Min(w * 0.72f, 760f * scale);
            float boxX  = (w - boxW) / 2f;

            for (int i = 0; i < modes.Count; i++)
            {
                Mode   m   = modes[i];
                RectangleF row = new RectangleF(boxX, top + i * rowH, boxW, rowH * 0.88f);
                bool   sel = (i == index);
                bool   cur = !m.IsExit && !m.IsAdd && m.W == cw && m.H == ch;

                if (sel)
                {
                    Color hl = (pendingDelete == i) ? Color.FromArgb(150, 60, 60) : Color.FromArgb(48, 96, 190);
                    using (Brush b = new SolidBrush(hl))
                        g.FillRectangle(b, row);
                }

                Color fg = Color.FromArgb(225, 225, 232);
                if (!m.Supported) fg = Color.FromArgb(110, 110, 118);
                if (sel)          fg = Color.White;

                string text = (m.IsExit || m.IsAdd) ? m.Label : m.Label + "   -   " + m.W + " x " + m.H;
                if (cur)           text += "   (current)";
                if (!m.Supported)  text += "   (unsupported)";

                using (Brush b = new SolidBrush(fg))
                    g.DrawString(text, itemFont, b, row, sf);
            }
            itemFont.Dispose();

            if (status.Length > 0)
            {
                float sy = Math.Min(top + listH + rowH * 0.15f, h - smallFont.Height * 4.8f);
                using (Brush b = new SolidBrush(statusColour))
                    g.DrawString(status, smallFont, b, new RectangleF(0, sy, w, smallFont.Height * 2f), sf);
            }

            bool anyCustom = false;
            foreach (Mode m in modes) if (m.IsCustom) anyCustom = true;

            string hint = anyCustom
                ? "D-pad / stick  move        A  apply        X  delete custom        B  quit"
                : "D-pad / stick  move        A  apply        B  quit";

            using (Brush dim = new SolidBrush(Color.FromArgb(130, 130, 140)))
                g.DrawString(hint, smallFont, dim,
                             new RectangleF(0, h - smallFont.Height * 3f, w, smallFont.Height * 2f), sf);
        }
    }

    void DrawNumpad(Graphics g, int w, int h)
    {
        float scale = Math.Min(w / 1280f, h / 800f);
        if (scale < 0.5f) scale = 0.5f;

        using (Font titleFont = new Font("Segoe UI", 22f * scale, FontStyle.Bold))
        using (Font fieldFont = new Font("Segoe UI", 24f * scale, FontStyle.Bold))
        using (Font keyFont   = new Font("Segoe UI", 18f * scale, FontStyle.Bold))
        using (Font smallFont = new Font("Segoe UI", 12f * scale, FontStyle.Regular))
        using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
        {
            g.DrawString("Add a resolution", titleFont, Brushes.White,
                         new RectangleF(0, h * 0.10f, w, titleFont.Height * 1.4f), sf);

            float fieldW = 190f * scale;
            float fieldH = fieldFont.Height * 1.5f;
            float gap    = 46f * scale;
            float fy     = h * 0.10f + titleFont.Height * 2.0f;

            DrawField(g, new RectangleF(w / 2f - fieldW - gap / 2f, fy, fieldW, fieldH),
                      padW, padField == 0, "width", fieldFont, smallFont, sf);
            DrawField(g, new RectangleF(w / 2f + gap / 2f, fy, fieldW, fieldH),
                      padH, padField == 1, "height", fieldFont, smallFont, sf);

            using (Brush b = new SolidBrush(Color.FromArgb(150, 150, 160)))
                g.DrawString("x", fieldFont, b, new RectangleF(w / 2f - gap / 2f, fy, gap, fieldH), sf);

            float keyW = 104f * scale, keyH = 62f * scale, kgap = 10f * scale;
            float px   = (w - (keyW * 3 + kgap * 2)) / 2f;
            float py   = fy + fieldH + 34f * scale;

            for (int r = 0; r < 4; r++)
                for (int c = 0; c < 3; c++)
                {
                    RectangleF kr = new RectangleF(px + c * (keyW + kgap), py + r * (keyH + kgap), keyW, keyH);
                    bool sel = (r == padRow && c == padCol);

                    using (Brush b = new SolidBrush(sel ? Color.FromArgb(48, 96, 190) : Color.FromArgb(38, 38, 46)))
                        g.FillRectangle(b, kr);
                    using (Brush b = new SolidBrush(sel ? Color.White : Color.FromArgb(215, 215, 224)))
                        g.DrawString(KEYS[r, c], keyFont, b, kr, sf);
                }

            float by = py + 4 * (keyH + kgap) + 6f * scale;
            if (padStatus.Length > 0)
                using (Brush b = new SolidBrush(padStatusColour))
                    g.DrawString(padStatus, smallFont, b, new RectangleF(0, by, w, smallFont.Height * 2f), sf);

            using (Brush dim = new SolidBrush(Color.FromArgb(130, 130, 140)))
                g.DrawString("D-pad  move        A  press        OK  next / apply + save        B  back",
                             smallFont, dim, new RectangleF(0, h - smallFont.Height * 3f, w, smallFont.Height * 2f), sf);
        }
    }

    void DrawField(Graphics g, RectangleF r, string val, bool active, string caption,
                   Font f, Font small, StringFormat sf)
    {
        using (Brush b = new SolidBrush(active ? Color.FromArgb(30, 52, 96) : Color.FromArgb(28, 28, 34)))
            g.FillRectangle(b, r);
        using (Pen p = new Pen(active ? Color.FromArgb(90, 150, 240) : Color.FromArgb(68, 68, 78), 2f))
            g.DrawRectangle(p, r.X, r.Y, r.Width, r.Height);
        using (Brush b = new SolidBrush(val.Length > 0 ? Color.White : Color.FromArgb(90, 90, 100)))
            g.DrawString(val.Length > 0 ? val : "----", f, b, r, sf);
        using (Brush b = new SolidBrush(Color.FromArgb(140, 140, 150)))
            g.DrawString(caption, small, b,
                         new RectangleF(r.X, r.Y - small.Height * 1.5f, r.Width, small.Height * 1.5f), sf);
    }

    // Used by --shot to preview this page without opening a window.
    public void PreviewNumpad(string wv, string hv, int field)
    {
        page = Page.Numpad;
        padW = wv; padH = hv; padField = field;
        padRow = 3; padCol = 2;
    }
}

static class Program
{
    static List<Mode> BuildModes()
    {
        List<Mode> list = new List<Mode>();
        list.Add(new Mode { Label = "Steam Deck", W = 1280, H = 800  });
        list.Add(new Mode { Label = "1080p",      W = 1920, H = 1080 });
        list.Add(new Mode { Label = "1200p",      W = 1920, H = 1200 });
        list.Add(new Mode { Label = "1440p",      W = 3440, H = 1440 });
        list.Add(new Mode { Label = "480p",       W = 640,  H = 480  });

        foreach (int[] c in Store.Load())
        {
            bool dup = false;
            foreach (Mode m in list) if (m.W == c[0] && m.H == c[1]) dup = true;
            if (!dup) list.Add(new Mode { Label = "Custom", W = c[0], H = c[1], IsCustom = true });
        }

        foreach (Mode m in list) m.Supported = Native.IsSupported(m.W, m.H);

        list.Add(new Mode { Label = "Add a resolution...", IsAdd  = true });
        list.Add(new Mode { Label = "Exit",                IsExit = true });
        return list;
    }

    [STAThread]
    static int Main(string[] args)
    {
        Native.SetProcessDPIAware();

        // --selftest <file>  writes a report and exits without showing any UI.
        if (args.Length >= 2 && args[0] == "--selftest")
        {
            StringBuilder sb = new StringBuilder();
            int cw, ch, chz;
            bool ok = Native.GetCurrent(out cw, out ch, out chz);
            sb.AppendLine("current       : " + (ok ? cw + " x " + ch + " @ " + chz + " Hz" : "FAILED to read"));
            sb.AppendLine("xinput backend: " + Pad.BackendName());
            Pad.GAMEPAD g;
            sb.AppendLine("controller    : " + (Pad.Poll(out g) ? "connected" : "none connected right now"));
            sb.AppendLine("modes:");
            foreach (Mode m in BuildModes())
            {
                if (m.IsExit) { sb.AppendLine("  [Exit]"); continue; }
                if (m.IsAdd)  { sb.AppendLine("  [Add a resolution...]"); continue; }
                sb.AppendLine(string.Format("  {0,-11} {1,5} x {2,-5} {3}",
                    m.Label, m.W, m.H, m.Supported ? "supported" : "NOT SUPPORTED"));
            }
            File.WriteAllText(args[1], sb.ToString());
            return 0;
        }

        // --shot <file> [width height]  renders the menu to a PNG, no window.
        if (args.Length >= 2 && args[0] == "--shot")
        {
            int sw = args.Length >= 4 ? int.Parse(args[2]) : 1280;
            int sh = args.Length >= 4 ? int.Parse(args[3]) : 800;
            using (MenuForm f = new MenuForm(BuildModes()))
            using (Bitmap bmp = new Bitmap(sw, sh))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                if (args.Length >= 5 && args[4] == "pad")      f.PreviewNumpad("2560", "10", 1);
                else if (args.Length >= 5 && args[4] == "del") f.PreviewDelete();
                else if (args.Length >= 5 && args[4] == "del2") { f.PreviewDelete(); f.PreviewDelete(); }
                else
                {
                    f.status = "Applied  1920 x 1080";
                    f.statusColour = Color.FromArgb(120, 220, 140);
                }
                f.Draw(g, sw, sh);
                bmp.Save(args[1], System.Drawing.Imaging.ImageFormat.Png);
            }
            return 0;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MenuForm(BuildModes()));
        return 0;
    }
}
