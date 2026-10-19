using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Windows.Forms;

namespace PrzPreview
{
    /// <summary>Testet den registrierten Vorschau-Handler wie der Explorer (out-of-process in prevhost.exe).</summary>
    static class HandlerTest
    {
        [ComImport, Guid("8895b1c6-b41f-4c1c-a562-0d564250836f"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IPreviewHandlerT
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
        interface IInitializeWithStreamT
        {
            void Initialize(IStream pstream, uint grfMode);
        }

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        static extern IStream SHCreateStreamOnFileEx(string file, uint mode, uint attr, bool create, IStream template);

        [STAThread]
        static void Main(string[] args)
        {
            var form = new Form { Text = "Handler-Test (prevhost)", Width = 520, Height = 760 };
            form.Shown += delegate
            {
                try
                {
                    object o;
                    // Optional 2. Argument: andere CLSID (z.B. MSIX-Brücke a62e1a79-...)
                    var clsid = new Guid(args.Length > 1 ? args[1] : "224aabb0-d365-4cea-95af-e95c1979a035");
                    var iunk = new Guid("00000000-0000-0000-C000-000000000046");
                    int hr = CoCreateInstance(ref clsid, IntPtr.Zero, 4 /*LOCAL_SERVER*/, ref iunk, out o);
                    if (hr != 0) throw new COMException("CoCreateInstance", hr);

                    var stream = SHCreateStreamOnFileEx(args[0], 0x20 /*read, share deny write*/, 0, false, null);
                    ((IInitializeWithStreamT)o).Initialize(stream, 0);
                    var h = (IPreviewHandlerT)o;
                    var r = new RECT { left = 0, top = 0, right = form.ClientSize.Width, bottom = form.ClientSize.Height };
                    h.SetWindow(form.Handle, ref r);
                    h.DoPreview();
                    form.Resize += delegate
                    {
                        var rr = new RECT { right = form.ClientSize.Width, bottom = form.ClientSize.Height };
                        h.SetRect(ref rr);
                    };
                }
                catch (Exception ex) { MessageBox.Show(ex.ToString()); }
            };
            Application.Run(form);
        }

        [DllImport("ole32.dll")]
        static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, uint ctx, ref Guid iid,
            [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
    }
}
