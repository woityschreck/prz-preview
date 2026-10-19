using System;
using System.IO;
using System.Windows.Forms;

namespace PrzPreview
{
    /// <summary>Eigenständiger Viewer: PrzViewer.exe [datei.prz] – auch per Drag &amp; Drop.</summary>
    static class ViewerProgram
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main(string[] args)
        {
            SetProcessDPIAware();   // scharfe Darstellung bei Windows-Skalierung (z.B. 125 %)
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var form = new Form { Text = "PRZ Viewer", Width = 520, Height = 760, AllowDrop = true };
            var view = new PrzPreviewControl { Dock = DockStyle.Fill };
            form.Controls.Add(view);

            Action<string> open = path =>
            {
                try
                {
                    view.ShowInfo(PrzInfo.Load(path));
                    form.Text = Path.GetFileName(path) + " – PRZ Viewer";
                }
                catch (Exception ex) { view.ShowError(ex.Message); }
            };

            form.DragEnter += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
            form.DragDrop += (s, e) => { var f = (string[])e.Data.GetData(DataFormats.FileDrop); if (f.Length > 0) open(f[0]); };
            form.Shown += delegate
            {
                if (args.Length > 0) { open(args[0]); return; }
                using (var dlg = new OpenFileDialog { Filter = "CAM-Part (*.prz)|*.prz" })
                    if (dlg.ShowDialog(form) == DialogResult.OK) open(dlg.FileName);
            };
            Application.Run(form);
        }
    }
}
