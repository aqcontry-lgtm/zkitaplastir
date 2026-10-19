using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Automation;

class ZKitaplastir
{
    delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc cb, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hWnd, StringBuilder s, int max);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int n);
    [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint a, uint b, bool f);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool ReadProcessMemory(IntPtr h, IntPtr addr, byte[] buf, IntPtr size, out IntPtr read);
    [DllImport("kernel32.dll")] static extern IntPtr VirtualQueryEx(IntPtr h, IntPtr addr, out MEMORY_BASIC_INFORMATION mbi, IntPtr len);
    [StructLayout(LayoutKind.Sequential)] public struct MEMORY_BASIC_INFORMATION
    { public IntPtr BaseAddress; public IntPtr AllocationBase; public uint AllocationProtect; public uint a1; public IntPtr RegionSize; public uint State; public uint Protect; public uint Type; public uint a2; }

    [DllImport("advapi32.dll", SetLastError = true)] static extern bool OpenProcessToken(IntPtr h, uint access, out IntPtr tok);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool LookupPrivilegeValue(string host, string name, out long luid);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool AdjustTokenPrivileges(IntPtr tok, bool dis, ref TOKEN_PRIVILEGES np, uint len, IntPtr prev, IntPtr rl);
    [StructLayout(LayoutKind.Sequential)] struct TOKEN_PRIVILEGES { public uint Count; public long Luid; public uint Attr; }

    static string DwebpPath, exeDir, outDir, rawDir;
    static StreamWriter log;
    static void L(string s) { Console.WriteLine(s); if (log != null) { log.WriteLine(s); log.Flush(); } }

    static void EnableDebug()
    {
        IntPtr tok; if (!OpenProcessToken((IntPtr)(-1), 0x28, out tok)) return;
        long luid; if (!LookupPrivilegeValue(null, "SeDebugPrivilege", out luid)) return;
        TOKEN_PRIVILEGES tp; tp.Count = 1; tp.Luid = luid; tp.Attr = 0x2;
        AdjustTokenPrivileges(tok, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
    }

    static void ForceForeground(IntPtr hwnd)
    {
        IntPtr fg = GetForegroundWindow(); uint d; uint fgT = GetWindowThreadProcessId(fg, out d);
        uint myT = GetCurrentThreadId(); bool att = fgT != 0 && fgT != myT;
        if (att) AttachThreadInput(myT, fgT, true);
        ShowWindow(hwnd, 9); BringWindowToTop(hwnd); SetForegroundWindow(hwnd);
        if (att) AttachThreadInput(myT, fgT, false);
    }

    static bool PickWindow(out IntPtr best, out uint bestPid, out string bestTitle)
    {
        List<IntPtr> all = new List<IntPtr>();
        EnumWindows(delegate(IntPtr h, IntPtr l) { if (IsWindowVisible(h)) all.Add(h); return true; }, IntPtr.Zero);
        uint self = (uint)Process.GetCurrentProcess().Id;
        best = IntPtr.Zero; bestPid = 0; bestTitle = null; long bestScore = -1;
        foreach (IntPtr h in all)
        {
            uint pid; GetWindowThreadProcessId(h, out pid);
            if (pid == self || pid == 0) continue;
            StringBuilder sb = new StringBuilder(512); GetWindowText(h, sb, sb.Capacity); string t = sb.ToString();
            string pl = ""; try { pl = Process.GetProcessById((int)pid).ProcessName.ToLowerInvariant(); } catch { }
            if (pl.Contains("zkitaplastir")) continue;
            if (pl == "cmd" || pl == "conhost" || pl == "windowsterminal" || pl == "powershell") continue;
            string tl = t.ToLowerInvariant();
            if (tl.Contains("zkitaplastir")) continue;
            bool strongProc = pl.Contains("kutuphane") || pl.Contains("fernus");
            bool procMatch = strongProc || pl.Contains("zkitap");
            bool titleMatch = tl.Contains("kitap") || tl.Contains("kütüphane") || tl.Contains("kutuphane") || tl.Contains("zkitap") || tl.Contains("atolyem") || tl.Contains("atölyem");
            RECT r; if (!GetWindowRect(h, out r)) continue;
            long w = r.Right - r.Left, hh = r.Bottom - r.Top; long area = w * hh;
            long baseScore = strongProc ? 5000000000000L : (titleMatch ? 3000000000000L : (procMatch ? 2000000000000L : 0));
            long score = baseScore + area;
            if (score > bestScore) { bestScore = score; best = h; bestPid = pid; bestTitle = t; }
        }
        return best != IntPtr.Zero && bestScore >= 2000000000000L;
    }

    static string GetUiaTitle(IntPtr hwnd)
    {
        try
        {
            AutomationElement root = AutomationElement.FromHandle(hwnd);
            if (root == null) return null;
            AutomationElementCollection all = root.FindAll(TreeScope.Descendants, Condition.TrueCondition);
            string best = null;
            string[] skip = { "arı", "ari", "geri", "kapat", "menü", "menu", "ayarlar", "seç", "sec", "ara", "giriş", "giris", "oku", "indir", "paylaş", "paylas" };
            foreach (AutomationElement e in all)
            {
                string n = "";
                try { n = e.Current.Name; } catch { continue; }
                if (string.IsNullOrEmpty(n)) continue;
                n = n.Trim();
                if (n.Length < 6 || n.Length > 160) continue;
                string nl = n.ToLowerInvariant();
                if (nl.Contains("text input application") || nl.Contains("microsoft") || nl.Contains("program manager")) continue;
                bool sk = false; foreach (string s in skip) if (nl == s) { sk = true; break; }
                if (sk) continue;
                int letters = 0; foreach (char c in n) if (char.IsLetter(c)) letters++;
                if (letters < 4) continue;
                if (best == null || n.Length > best.Length) best = n;
            }
            return best;
        }
        catch { return null; }
    }

    static void Flip(IntPtr hwnd) { ForceForeground(hwnd); Thread.Sleep(120); try { SendKeys.SendWait("{PGDN}"); } catch { } }

    static HashSet<string> seen = new HashSet<string>();
    static List<byte[]> pages = new List<byte[]>();

    static int Capture(uint pid)
    {
        IntPtr h = OpenProcess(0x0410, false, (int)pid);
        if (h == IntPtr.Zero) return 0;
        int added = 0; IntPtr addr = IntPtr.Zero; MEMORY_BASIC_INFORMATION mbi; int chunk = 16 * 1024 * 1024;
        SHA1 sha = SHA1.Create();
        try
        {
            while (true)
            {
                IntPtr q = VirtualQueryEx(h, addr, out mbi, (IntPtr)Marshal.SizeOf(typeof(MEMORY_BASIC_INFORMATION)));
                if (q == IntPtr.Zero) break;
                long size = (long)mbi.RegionSize;
                bool rd = mbi.State == 0x1000 && (mbi.Protect & 0x100) == 0 && (mbi.Protect & 0x01) == 0;
                if (rd && size > 0 && size < 400L * 1024 * 1024)
                {
                    long off = 0;
                    while (off < size)
                    {
                        long len = Math.Min((long)chunk, size - off);
                        byte[] buf = new byte[len]; IntPtr got;
                        bool ok = ReadProcessMemory(h, (IntPtr)((long)mbi.BaseAddress + off), buf, (IntPtr)len, out got);
                        long n = (long)got;
                        if (ok && n > 12)
                        {
                            long i = 0;
                            while (i < n - 12)
                            {
                                if (buf[i] == 0x52 && buf[i + 1] == 0x49 && buf[i + 2] == 0x46 && buf[i + 3] == 0x46 &&
                                    buf[i + 8] == 0x57 && buf[i + 9] == 0x45 && buf[i + 10] == 0x42 && buf[i + 11] == 0x50)
                                {
                                    uint sz = BitConverter.ToUInt32(buf, (int)i + 4); long tot = (long)sz + 8;
                                    if (sz > 80000 && tot < 30L * 1024 * 1024 && (i + tot) <= n
                                        && buf[i + 12] == 0x56 && buf[i + 13] == 0x50 && buf[i + 14] == 0x38 && buf[i + 15] == 0x20)
                                    {
                                        byte[] img = new byte[tot]; Array.Copy(buf, i, img, 0, tot);
                                        string key = BitConverter.ToString(sha.ComputeHash(img));
                                        if (!seen.Contains(key))
                                        {
                                            seen.Add(key); pages.Add(img); added++;
                                            try { File.WriteAllBytes(Path.Combine(rawDir, "p_" + pages.Count.ToString("D4") + ".webp"), img); } catch { }
                                        }
                                        i += tot; continue;
                                    }
                                }
                                i++;
                            }
                        }
                        off += len;
                    }
                }
                long nx = (long)mbi.BaseAddress + size; if (nx <= (long)addr) break; addr = (IntPtr)nx;
            }
        }
        finally { CloseHandle(h); }
        return added;
    }

    static int dFail = 0;
    static bool DecodeWebp(byte[] webp, string pngPath, out int w, out int h)
    {
        w = 0; h = 0; string wp = pngPath + ".webp";
        try
        {
            File.WriteAllBytes(wp, webp);
            ProcessStartInfo psi = new ProcessStartInfo(DwebpPath, "\"" + wp + "\" -o \"" + pngPath + "\"");
            psi.UseShellExecute = false; psi.CreateNoWindow = true; psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
            Process p = Process.Start(psi);
            p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd();
            if (!p.WaitForExit(10000)) { try { p.Kill(); } catch { } if (dFail < 4) { dFail++; L("  dwebp TIMEOUT"); } return false; }
            if (!File.Exists(pngPath)) { if (dFail < 4) { dFail++; L("  dwebp FAIL"); } return false; }
            using (Image img = Image.FromFile(pngPath)) { w = img.Width; h = img.Height; }
            return true;
        }
        catch { return false; }
        finally { try { File.Delete(wp); } catch { } }
    }

    static ulong AHash(string pngPath)
    {
        using (Image img = Image.FromFile(pngPath))
        using (Bitmap b = new Bitmap(16, 16))
        {
            using (Graphics g = Graphics.FromImage(b)) { g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear; g.DrawImage(img, 0, 0, 16, 16); }
            int sum = 0; int[] v = new int[256];
            for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++) { Color c = b.GetPixel(x, y); v[y * 16 + x] = (c.R * 299 + c.G * 587 + c.B * 114) / 1000; sum += v[y * 16 + x]; }
            int avg = sum / 256; ulong hash = 0;
            for (int i = 0; i < 256; i++) if (v[i] >= avg) hash |= (1UL << i);
            return hash;
        }
    }
    static byte[] Gray64(string pngPath)
    {
        using (Image img = Image.FromFile(pngPath))
        using (Bitmap b = new Bitmap(64, 64))
        {
            using (Graphics g = Graphics.FromImage(b)) { g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear; g.DrawImage(img, 0, 0, 64, 64); }
            byte[] r = new byte[4096];
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++) { Color c = b.GetPixel(x, y); r[y * 64 + x] = (byte)((c.R * 299 + c.G * 587 + c.B * 114) / 1000); }
            return r;
        }
    }
    static double MAD(byte[] a, byte[] b)
    {
        long s = 0; for (int i = 0; i < a.Length; i++) { int d = a[i] - b[i]; if (d < 0) d = -d; s += d; }
        return (double)s / a.Length;
    }

    static int Hamming(ulong a, ulong b) { ulong x = a ^ b; int c = 0; while (x != 0) { c += (int)(x & 1UL); x >>= 1; } return c; }

    static byte[] PngToJpeg(string pngPath, int targetW, long quality)
    {
        try
        {
            using (Image img = Image.FromFile(pngPath))
            {
                int w = img.Width; double sc = targetW > 0 ? (double)targetW / w : 1.0; if (sc > 1.0) sc = 1.0;
                int nw = (int)(w * sc), nh = (int)(img.Height * sc);
                using (Bitmap bmp = new Bitmap(nw, nh, PixelFormat.Format24bppRgb))
                {
                    using (Graphics g = Graphics.FromImage(bmp)) { g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic; g.DrawImage(img, 0, 0, nw, nh); }
                    ImageCodecInfo jpg = null; foreach (ImageCodecInfo c in ImageCodecInfo.GetImageEncoders()) if (c.MimeType == "image/jpeg") jpg = c;
                    EncoderParameters ep = new EncoderParameters(1);
                    ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
                    using (MemoryStream ms = new MemoryStream()) { bmp.Save(ms, jpg, ep); return ms.ToArray(); }
                }
            }
        }
        catch { return null; }
    }

    static void WritePdf(List<byte[]> jpegs, int[] ws, int[] hs, string outPath)
    {
        int n = jpegs.Count, total = 2 + 3 * n;
        MemoryStream body = new MemoryStream(); List<long> offsets = new List<long>();
        Action<int, byte[]> addObj = delegate(int num, byte[] data)
        {
            while (offsets.Count < num) offsets.Add(-1);
            offsets[num - 1] = body.Length;
            byte[] head = Encoding.ASCII.GetBytes(num + " 0 obj\n"); body.Write(head, 0, head.Length);
            body.Write(data, 0, data.Length);
            byte[] tail = Encoding.ASCII.GetBytes("\nendobj\n"); body.Write(tail, 0, tail.Length);
        };
        addObj(1, Encoding.ASCII.GetBytes("<< /Type /Catalog /Pages 2 0 R >>"));
        StringBuilder kids = new StringBuilder();
        for (int i = 0; i < n; i++) kids.Append((3 + i * 3 + 2) + " 0 R ");
        addObj(2, Encoding.ASCII.GetBytes("<< /Type /Pages /Count " + n + " /Kids [" + kids.ToString().Trim() + "] >>"));
        for (int i = 0; i < n; i++)
        {
            int imgObj = 3 + i * 3, contObj = imgObj + 1, pageObj = imgObj + 2;
            double ptW = 595.28, ptH = 595.28 * (double)hs[i] / ws[i];
            string imgDict = "<< /Type /XObject /Subtype /Image /Width " + ws[i] + " /Height " + hs[i] + " /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length " + jpegs[i].Length + " >>";
            MemoryStream im = new MemoryStream();
            byte[] ih = Encoding.ASCII.GetBytes(imgDict + "\nstream\n"); im.Write(ih, 0, ih.Length);
            im.Write(jpegs[i], 0, jpegs[i].Length);
            byte[] it = Encoding.ASCII.GetBytes("\nendstream"); im.Write(it, 0, it.Length);
            addObj(imgObj, im.ToArray());
            string cont = "q " + ptW.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " 0 0 " + ptH.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " 0 0 cm /Im" + i + " Do Q";
            addObj(contObj, Encoding.ASCII.GetBytes("<< /Length " + cont.Length + " >>\nstream\n" + cont + "\nendstream"));
            string pg = "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 " + ptW.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " " + ptH.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "] /Resources << /XObject << /Im" + i + " " + imgObj + " 0 R >> >> /Contents " + contObj + " 0 R >>";
            addObj(pageObj, Encoding.ASCII.GetBytes(pg));
        }
        MemoryStream pdf = new MemoryStream();
        byte[] hdr = Encoding.ASCII.GetBytes("%PDF-1.4\n"); pdf.Write(hdr, 0, hdr.Length);
        byte[] marker = new byte[] { 0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x0A }; pdf.Write(marker, 0, marker.Length);
        long bodyStart = pdf.Length; byte[] b = body.ToArray(); pdf.Write(b, 0, b.Length);
        long xref = pdf.Length; StringBuilder xr = new StringBuilder();
        xr.Append("xref\n0 " + (total + 1) + "\n0000000000 65535 f \n");
        for (int i = 0; i < total; i++) { long o = offsets[i] >= 0 ? offsets[i] + bodyStart : 0; xr.Append(o.ToString("0000000000") + " 00000 n \n"); }
        xr.Append("trailer\n<< /Size " + (total + 1) + " /Root 1 0 R >>\nstartxref\n" + xref + "\n%%EOF");
        byte[] tb = Encoding.ASCII.GetBytes(xr.ToString()); pdf.Write(tb, 0, tb.Length);
        File.WriteAllBytes(outPath, pdf.ToArray());
    }

    static int Main(string[] args)
    {
        int wantPages = -1, intervalMs = 800, settleMs = 650, autoMiss = 10, rewind = 0, targetW = 1000; long quality = 68;
        int dupThresh = 5; string outPath = null; bool noPause = false; string logPath = null; bool keepRaw = false;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-pages": wantPages = int.Parse(args[++i]); break;
                case "-interval": intervalMs = int.Parse(args[++i]); break;
                case "-settle": settleMs = int.Parse(args[++i]); break;
                case "-k": autoMiss = int.Parse(args[++i]); break;
                case "-w": targetW = int.Parse(args[++i]); break;
                case "-q": quality = long.Parse(args[++i]); break;
                case "-dup": dupThresh = int.Parse(args[++i]); break;
                case "-rewind": rewind = int.Parse(args[++i]); break;
                case "-out": outPath = args[++i]; break;
                case "-nopause": noPause = true; break;
                case "-log": logPath = args[++i]; break;
                case "-keepraw": keepRaw = true; break;
            }
        }
        exeDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
        outDir = Path.Combine(exeDir, "pdfler"); Directory.CreateDirectory(outDir);
        rawDir = Path.Combine(outDir, "_raw"); Directory.CreateDirectory(rawDir);
        DwebpPath = Path.Combine(exeDir, "dwebp.exe");
        if (!File.Exists(DwebpPath))
        {
            try
            {
                string tmpExe = Path.Combine(Path.GetTempPath(), "zk_dwebp_1.4.0.exe");
                if (!File.Exists(tmpExe))
                {
                    Assembly asm = Assembly.GetExecutingAssembly();
                    Stream s = asm.GetManifestResourceStream("dwebp.exe");
                    if (s == null) { foreach (string nm in asm.GetManifestResourceNames()) if (nm.ToLowerInvariant().EndsWith("dwebp.exe")) { s = asm.GetManifestResourceStream(nm); break; } }
                    if (s != null) { using (s) using (FileStream fs = new FileStream(tmpExe, FileMode.Create, FileAccess.Write)) { byte[] bb = new byte[81920]; int r; while ((r = s.Read(bb, 0, bb.Length)) > 0) fs.Write(bb, 0, r); } }
                }
                if (File.Exists(tmpExe)) DwebpPath = tmpExe;
            }
            catch { }
        }
        if (logPath != null) { try { log = new StreamWriter(logPath, false, Encoding.UTF8); log.AutoFlush = true; } catch { } }

        L("ZKitaplastir (bellek modu)  dwebp=" + DwebpPath + "  " + (File.Exists(DwebpPath) ? "OK" : "YOK"));
        EnableDebug();
        IntPtr hwnd; uint pid; string title;
        if (!PickWindow(out hwnd, out pid, out title)) { L("Hedef pencere bulunamadi."); if (!noPause) { try { Console.ReadKey(); } catch { } } return 1; }
        L("Hedef: '" + title + "' pid=" + pid);
        if (wantPages <= 0 && !noPause)
        {
            Console.Write("Sayfa sayisi (Enter = otomatik): ");
            string s = Console.ReadLine(); int v;
            if (s != null && int.TryParse(s.Trim(), out v) && v > 0) wantPages = v;
        }
        if (!noPause)
        {
            Console.Write("Sayfa gecis gecikmesi ms (ornek 600/800/1000, Enter=800): ");
            string ds = Console.ReadLine(); int dv;
            if (ds != null && int.TryParse(ds.Trim(), out dv) && dv >= 200) { intervalMs = dv; settleMs = Math.Max(250, dv - 150); }
            else { intervalMs = 800; settleMs = 650; }
        }
        string bookTitle = null;
        try { bookTitle = GetUiaTitle(hwnd); } catch { }
        if (string.IsNullOrEmpty(bookTitle)) bookTitle = title;
        L("Kitap adi: " + bookTitle);
        L(wantPages > 0 ? ("Mod: SABIT " + wantPages) : "Mod: OTOMATIK");
        if (!noPause) { Console.WriteLine("3 sn sonra..."); Thread.Sleep(3000); }
        if (rewind > 0)
        {
            L("Basa sarma " + rewind);
            for (int i = 0; i < rewind; i++) { ForceForeground(hwnd); try { SendKeys.SendWait("{PGUP}"); } catch { } Thread.Sleep(110); }
            Thread.Sleep(600);
        }

        int miss = 0, iter = 0, maxIter = wantPages > 0 ? wantPages : 2000;
        while (iter < maxIter)
        {
            bool alive = true; try { Process.GetProcessById((int)pid); } catch { alive = false; }
            if (!alive) { L("Okuyucu kapandi, duruldu."); break; }
            iter++;
            int before = pages.Count;
            Flip(hwnd);
            Thread.Sleep(settleMs);
            int added = Capture(pid);
            if (added > 0) miss = 0; else miss++;
            L("#" + iter + " yeni=" + added + " toplam=" + pages.Count + (wantPages > 0 ? "" : (" bos=" + miss + "/" + autoMiss)));
            if (wantPages <= 0 && miss >= autoMiss) { L("Otomatik: yeni sayfa yok, durdu."); break; }
            int rem = intervalMs - settleMs; if (rem > 0) Thread.Sleep(rem);
        }
        L("Yakalama bitti: " + pages.Count + " goruntu");

        L("PDF uretiliyor (tekillestirme=" + dupThresh + ")...");
        string tmp = Path.Combine(Path.GetTempPath(), "zk_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(tmp);
        List<byte[]> keptGray = new List<byte[]>();
        List<byte[]> jpegs = new List<byte[]>(); List<int> ws = new List<int>(), hs = new List<int>();
        int kept = 0;
        for (int i = 0; i < pages.Count; i++)
        {
            string png = Path.Combine(tmp, i + ".png"); int w, h;
            if (!DecodeWebp(pages[i], png, out w, out h)) { continue; }
            byte[] gray = Gray64(png); bool dup = false;
            if (dupThresh >= 0) { for (int k = 0; k < keptGray.Count; k++) if (MAD(gray, keptGray[k]) < dupThresh) { dup = true; break; } }
            if (!dup)
            {
                keptGray.Add(gray); byte[] jpg = PngToJpeg(png, targetW, quality);
                if (jpg != null) { using (MemoryStream ms = new MemoryStream(jpg)) using (Image im = Image.FromStream(ms)) { ws.Add(im.Width); hs.Add(im.Height); } jpegs.Add(jpg); kept++; }
            }
            try { File.Delete(png); } catch { }
            if (i % 40 == 0) L("  " + i + "/" + pages.Count + " tekil=" + kept);
        }
        L("Tekil sayfa: " + kept);
        try { Directory.Delete(tmp, true); } catch { }
        if (jpegs.Count == 0) { L("PDF icin sayfa yok."); if (!noPause) { try { Console.ReadKey(); } catch { } } return 1; }

        if (outPath == null)
        {
            string safe = string.IsNullOrEmpty(bookTitle) ? "kitap" : bookTitle;
            foreach (char c in Path.GetInvalidFileNameChars()) safe = safe.Replace(c, '_');
            outPath = Path.Combine(outDir, safe + ".pdf");
        }
        WritePdf(jpegs, ws.ToArray(), hs.ToArray(), outPath);
        L("Bitti: " + outPath + "  (" + jpegs.Count + " sayfa)");
        try { if (!keepRaw && Directory.Exists(rawDir)) Directory.Delete(rawDir, true); } catch { }
        if (log != null) { log.Flush(); log.Close(); }
        if (!noPause) { try { Console.ReadKey(); } catch { } }
        return 0;
    }
}
