using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Windows.Forms;

namespace PrzPreview
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int left, top, right, bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG { public IntPtr hwnd; public int message; public IntPtr wParam, lParam; public int time, pt_x, pt_y; }

    [ComImport, Guid("8895b1c6-b41f-4c1c-a562-0d564250836f"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPreviewHandler
    {
        void SetWindow(IntPtr hwnd, ref RECT rect);
        void SetRect(ref RECT rect);
        void DoPreview();
        void Unload();
        void SetFocus();
        void QueryFocus(out IntPtr phwnd);
        [PreserveSig] uint TranslateAccelerator(ref MSG pmsg);
    }

    [ComImport, Guid("b824b49d-22ac-4161-ac8a-9916e8fa3f7f"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IInitializeWithStream
    {
        void Initialize(IStream pstream, uint grfMode);
    }

    [ComImport, Guid("00000114-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IOleWindow
    {
        void GetWindow(out IntPtr phwnd);
        void ContextSensitiveHelp([MarshalAs(UnmanagedType.Bool)] bool fEnterMode);
    }

    [ComImport, Guid("fc4801a3-2ba9-11cf-a229-00aa003d7352"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IObjectWithSite
    {
        void SetSite([MarshalAs(UnmanagedType.IUnknown)] object pUnkSite);
        void GetSite(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppvSite);
    }

    /// <summary>Explorer-Vorschau-Handler für .prz-Dateien (läuft in prevhost.exe).</summary>
    [ComVisible(true)]
    [Guid("224aabb0-d365-4cea-95af-e95c1979a035")]
    [ClassInterface(ClassInterfaceType.None)]
    public class PrzPreviewHandler : IPreviewHandler, IInitializeWithStream, IOleWindow, IObjectWithSite
    {
        PrzPreviewControl control;
        object site;

        [DllImport("user32.dll")] static extern IntPtr SetParent(IntPtr child, IntPtr parent);
        [DllImport("user32.dll")] static extern IntPtr GetFocus();

        /// <summary>Protokoll nach %LOCALAPPDATA%Low\PrzPreview.log (prevhost läuft mit niedriger Integrität).</summary>
        internal static void Log(string msg)
        {
            try
            {
                var dir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "Low";
                File.AppendAllText(Path.Combine(dir, "PrzPreview.log"),
                    DateTime.Now.ToString("HH:mm:ss.fff") + " [" + System.Threading.Thread.CurrentThread.ManagedThreadId + "] " + msg + Environment.NewLine);
            }
            catch { }
        }

        static PrzPreviewHandler()
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Log("UNHANDLED: " + e.ExceptionObject);
            Application.ThreadException += (s, e) => Log("THREAD: " + e.Exception);
        }

        IntPtr controlHandle;
        IntPtr parentHwnd;
        Rectangle bounds;

        public PrzPreviewHandler()
        {
            // prevhost erzeugt das Objekt auf seinem UI-Thread (STA) – hier die Oberfläche anlegen.
            // Spätere Aufrufe kommen auf RPC-Threads an (.NET-COM-Objekte sind "agil") und werden
            // deshalb per Invoke auf diesen Thread umgeleitet.
            control = new PrzPreviewControl();
            control.CreateControl();
            controlHandle = control.Handle;
        }

        /// <summary>Führt eine Aktion auf dem Oberflächen-Thread aus.</summary>
        void OnUi(Action a)
        {
            try
            {
                if (control.InvokeRequired) control.Invoke(a);
                else a();
            }
            catch (Exception ex) { Log("UI FEHLER: " + ex); }
        }

        IStream stream;   // vom Explorer; wird erst in DoPreview gelesen und dann freigegeben

        public void Initialize(IStream pstream, uint grfMode)
        {
            ReleaseStream();
            stream = pstream;
        }

        void ReleaseStream()
        {
            if (stream == null) return;
            try { Marshal.ReleaseComObject(stream); } catch { }
            stream = null;
        }

        /// <summary>
        /// Liest die .prz direkt aus dem Explorer-Stream (nur die benötigten Teile des ZIP-Archivs).
        /// Unterstützt der Stream kein Springen, wird er wie früher komplett in den Speicher kopiert.
        /// </summary>
        PrzInfo LoadFromStream()
        {
            try
            {
                using (var com = new ComStream(stream))
                using (var buffered = new BufferedStream(com, 1 << 16))
                    return PrzInfo.Load(buffered);
            }
            catch (NotSupportedException)
            {
                using (var ms = new MemoryStream())
                {
                    stream.Seek(0, 0, IntPtr.Zero);
                    var buf = new byte[81920];
                    IntPtr pRead = Marshal.AllocCoTaskMem(4);
                    try
                    {
                        while (true)
                        {
                            stream.Read(buf, buf.Length, pRead);
                            int n = Marshal.ReadInt32(pRead);
                            if (n <= 0) break;
                            ms.Write(buf, 0, n);
                        }
                    }
                    finally { Marshal.FreeCoTaskMem(pRead); }
                    ms.Position = 0;
                    return PrzInfo.Load(ms);
                }
            }
        }
        public void SetWindow(IntPtr hwnd, ref RECT rect)
        {
            parentHwnd = hwnd;
            bounds = Rectangle.FromLTRB(rect.left, rect.top, rect.right, rect.bottom);
            OnUi(() =>
            {
                SetParent(controlHandle, parentHwnd);
                control.Bounds = bounds;
            });
        }

        public void SetRect(ref RECT rect)
        {
            bounds = Rectangle.FromLTRB(rect.left, rect.top, rect.right, rect.bottom);
            OnUi(() => control.Bounds = bounds);
        }

        public void DoPreview()
        {
            PrzInfo info = null;
            string error = null;
            try
            {
                if (stream == null) throw new InvalidOperationException("Keine Datei übergeben");
                info = LoadFromStream();
            }
            catch (Exception ex)
            {
                Log("DoPreview FEHLER: " + ex);
                error = ex.Message;
            }
            ReleaseStream();   // Datei nicht länger als nötig offen halten
            OnUi(() =>
            {
                if (info != null) control.ShowInfo(info); else control.ShowError(error);
                control.Visible = true;
            });
        }

        public void Unload()
        {
            ReleaseStream();
            OnUi(() => control.Visible = false);
        }

        public void SetFocus() { OnUi(() => control.Focus()); }
        public void QueryFocus(out IntPtr phwnd) { phwnd = GetFocus(); }
        public uint TranslateAccelerator(ref MSG pmsg) { return 1; /* S_FALSE */ }

        public void GetWindow(out IntPtr phwnd) { phwnd = controlHandle; }
        public void ContextSensitiveHelp(bool fEnterMode) { throw new NotImplementedException(); }

        public void SetSite(object pUnkSite) { site = pUnkSite; }
        public void GetSite(ref Guid riid, out object ppvSite) { ppvSite = site; }
    }

    /// <summary>Stream-Hülle um einen COM-IStream (z.B. die Datei, die der Explorer übergibt) – suchbar, nur lesend.</summary>
    sealed class ComStream : Stream
    {
        readonly IStream source;
        readonly long length;
        readonly IntPtr scratch = Marshal.AllocCoTaskMem(8);
        long position;
        byte[] temp = new byte[0];

        public ComStream(IStream source)
        {
            this.source = source;
            System.Runtime.InteropServices.ComTypes.STATSTG stat;
            try
            {
                source.Stat(out stat, 1 /* STATFLAG_NONAME */);
                source.Seek(0, 0 /* STREAM_SEEK_SET */, IntPtr.Zero);
            }
            catch (Exception ex) { Marshal.FreeCoTaskMem(scratch); throw new NotSupportedException("Stream nicht suchbar", ex); }
            length = stat.cbSize;
        }

        public override bool CanRead { get { return true; } }
        public override bool CanSeek { get { return true; } }
        public override bool CanWrite { get { return false; } }
        public override long Length { get { return length; } }
        public override long Position { get { return position; } set { Seek(value, SeekOrigin.Begin); } }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (count <= 0) return 0;
            byte[] target = buffer;
            if (offset != 0)
            {
                if (temp.Length < count) temp = new byte[count];
                target = temp;
            }
            source.Read(target, count, scratch);
            int n = Marshal.ReadInt32(scratch);
            if (offset != 0 && n > 0) Buffer.BlockCopy(temp, 0, buffer, offset, n);
            position += n;
            return n;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            source.Seek(offset, (int)origin, scratch);   // SeekOrigin entspricht STREAM_SEEK_SET/CUR/END
            position = Marshal.ReadInt64(scratch);
            return position;
        }

        public override void Flush() { }
        public override void SetLength(long value) { throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }

        bool disposed;

        protected override void Dispose(bool disposing)
        {
            if (!disposed) { disposed = true; Marshal.FreeCoTaskMem(scratch); }   // BufferedStream + using rufen Dispose zweimal
            base.Dispose(disposing);
        }
    }
}