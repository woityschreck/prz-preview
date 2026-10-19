using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Forms.Integration;
using WinForms = System.Windows.Forms;

namespace PrzPreview
{
    /// <summary>Vorschau-Oberfläche (WinForms-Hülle für Explorer/Viewer, Inhalt komplett WPF).</summary>
    public class PrzPreviewControl : WinForms.UserControl
    {
        readonly PreviewView view = new PreviewView();

        public PrzPreviewControl()
        {
            BackColor = System.Drawing.Color.FromArgb(0xF5, 0xF6, 0xF8);
            Controls.Add(new ElementHost { Dock = WinForms.DockStyle.Fill, Child = view });
        }

        public void ShowInfo(PrzInfo info) { view.ShowInfo(info); }
        public void ShowError(string message) { view.ShowError(message); }
    }

    /// <summary>Das eigentliche Layout: Kopf, 3D-Karte, Operationsliste.</summary>
    class PreviewView : Grid
    {
        static readonly Brush PageBg = Hex("#F5F6F8");
        static readonly Brush CardBg = Brushes.White;
        static readonly Brush CardBorder = Hex("#E3E6EB");
        static readonly Brush TextMain = Hex("#1F2329");
        static readonly Brush TextSub = Hex("#6B7380");
        static readonly Brush Accent = Hex("#2F6FDE");
        static readonly Brush AccentSoft = Hex("#E7EFFC");
        static readonly Brush Warn = Hex("#D9822B");
        static readonly Brush WarnSoft = Hex("#FCEFE1");
        static readonly Brush BarBg = Hex("#EEF0F3");

        readonly TextBlock title = new TextBlock();
        readonly WrapPanel chips = new WrapPanel();
        readonly TextBlock detailsText = new TextBlock();
        readonly ModelViewer viewer = new ModelViewer();
        readonly StackPanel layerPills = new StackPanel { Orientation = Orientation.Horizontal };
        readonly ListBox list = new ListBox();
        readonly TextBlock sortButton = new TextBlock();
        readonly TextBlock listTitle = new TextBlock();
        readonly TextBlock countsText = new TextBlock();   // "11 Operationen · 6 Werkzeuge · 1 Nullpunkt"
        readonly TextBlock totalText = new TextBlock();    // "Gesamt 0:13:04" im Listenkopf
        readonly TextBlock timeNote = new TextBlock();     // "unvollständig · aus Einrichteblatt vom ..."

        PrzInfo current;
        bool sortByTime;

        public PreviewView()
        {
            Background = PageBg;
            TextElement.SetFontFamily(this, new FontFamily("Segoe UI Variable Text, Segoe UI"));
            TextElement.SetFontSize(this, 12.5);
            TextElement.SetForeground(this, TextMain);

            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = new GridLength(3, GridUnitType.Star), MinHeight = 120 });
            RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) });
            RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star), MinHeight = 90 });

            // ---- Kopf ----
            var header = new StackPanel { Margin = new Thickness(14, 12, 14, 8) };
            title.FontSize = 18;
            title.FontWeight = FontWeights.SemiBold;
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            chips.Margin = new Thickness(0, 6, 0, 0);
            detailsText.Foreground = TextSub;
            detailsText.Margin = new Thickness(2, 2, 0, 0);
            detailsText.TextWrapping = TextWrapping.Wrap;
            header.Children.Add(title);
            countsText.Margin = new Thickness(1, 4, 0, 0);
            countsText.TextTrimming = TextTrimming.CharacterEllipsis;
            header.Children.Add(countsText);
            header.Children.Add(chips);   // nur noch für Fehlermeldungen
            header.Children.Add(detailsText);
            Add(header, 0);

            // ---- 3D-Karte ----
            var viewGrid = new Grid();
            viewGrid.Children.Add(viewer);
            layerPills.HorizontalAlignment = HorizontalAlignment.Left;
            layerPills.VerticalAlignment = VerticalAlignment.Top;
            layerPills.Margin = new Thickness(8);
            viewGrid.Children.Add(layerPills);
            Add(Card(viewGrid, new Thickness(12, 0, 12, 0), true), 1);

            // ---- Trenner zum Verschieben ----
            Add(new GridSplitter
            {
                Height = 10, HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = Brushes.Transparent, ResizeDirection = GridResizeDirection.Rows
            }, 2);

            // ---- Operationsliste ----
            var listGrid = new Grid();
            listGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            listGrid.RowDefinitions.Add(new RowDefinition());
            var listHead = new DockPanel { Margin = new Thickness(12, 9, 12, 4) };
            listTitle.FontWeight = FontWeights.SemiBold;
            sortButton.Foreground = Accent;
            sortButton.Cursor = Cursors.Hand;
            sortButton.MouseLeftButtonUp += delegate { sortByTime = !sortByTime; FillList(); };
            DockPanel.SetDock(sortButton, Dock.Right);
            totalText.Foreground = Accent;
            totalText.FontWeight = FontWeights.SemiBold;
            totalText.Margin = new Thickness(0, 0, 14, 0);
            Typography.SetNumeralAlignment(totalText, FontNumeralAlignment.Tabular);
            DockPanel.SetDock(totalText, Dock.Right);
            listHead.Children.Add(sortButton);
            listHead.Children.Add(totalText);
            listHead.Children.Add(listTitle);
            timeNote.Foreground = TextSub;
            timeNote.FontSize = 11.5;
            timeNote.Margin = new Thickness(0, 2, 0, 0);
            timeNote.TextWrapping = TextWrapping.Wrap;
            DockPanel.SetDock(timeNote, Dock.Bottom);
            listHead.Children.Insert(0, timeNote);
            listGrid.Children.Add(listHead);

            list.BorderThickness = new Thickness(0);
            list.Background = Brushes.Transparent;
            list.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
            VirtualizingStackPanel.SetIsVirtualizing(list, true);   // auch bei hunderten Jobs flüssig
            VirtualizingStackPanel.SetVirtualizationMode(list, VirtualizationMode.Recycling);
            var itemStyle = new Style(typeof(ListBoxItem));
            itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
            itemStyle.Setters.Add(new Setter(UIElement.FocusableProperty, false));
            itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            // Eigenes Template: keine blaue Auswahl-/Hover-Markierung, nur der Inhalt
            var tpl = new ControlTemplate(typeof(ListBoxItem));
            tpl.VisualTree = new FrameworkElementFactory(typeof(ContentPresenter));
            itemStyle.Setters.Add(new Setter(Control.TemplateProperty, tpl));
            list.ItemContainerStyle = itemStyle;
            Grid.SetRow(list, 1);
            listGrid.Children.Add(list);
            Add(Card(listGrid, new Thickness(12, 0, 12, 12), false), 3);
        }

        void Add(UIElement e, int row) { Grid.SetRow(e, row); Children.Add(e); }

        static Brush Hex(string s)
        {
            var b = (Brush)new BrushConverter().ConvertFromString(s);
            b.Freeze();
            return b;
        }

        /// <summary>Weiße Karte mit runden Ecken; clip=true schneidet auch den 3D-Inhalt rund ab.</summary>
        static Border Card(UIElement child, Thickness margin, bool clip)
        {
            var b = new Border
            {
                Background = CardBg, BorderBrush = CardBorder, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12), Margin = margin, Child = child
            };
            if (clip)
            {
                var fe = (FrameworkElement)child;
                fe.SizeChanged += delegate
                {
                    fe.Clip = new RectangleGeometry(new Rect(0, 0, fe.ActualWidth, fe.ActualHeight), 11, 11);
                };
            }
            return b;
        }

        static Border Chip(string text, Brush bg, Brush fg)
        {
            return new Border
            {
                Background = bg, CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 2, 9, 3),
                Margin = new Thickness(0, 0, 6, 4),
                Child = new TextBlock { Text = text, Foreground = fg, FontSize = 12 }
            };
        }

        public void ShowInfo(PrzInfo info)
        {
            current = info;
            title.Text = info.PartName;
            title.ToolTip = info.Version;

            chips.Children.Clear();
            chips.Visibility = Visibility.Collapsed;

            // Listenkopf: Gesamtzeit + Hinweise zur Herkunft der Zeiten
            var total = info.TotalTime;
            bool missing = info.Operations.Exists(o => !o.Time.HasValue);
            totalText.Text = total.HasValue ? "Gesamt " + PrzInfo.FormatTime(total) : "keine Zeiten gespeichert";
            totalText.Foreground = total.HasValue ? Accent : TextSub;
            totalText.FontWeight = total.HasValue ? FontWeights.SemiBold : FontWeights.Normal;
            var notes = new List<string>();
            if (total.HasValue && missing) notes.Add("unvollständig");
            if (!total.HasValue && info.Operations.Count > 0) notes.Add("Zeiten entstehen bei der Simulation im CAM-System");
            if (info.TimeSource == "Einrichteblatt" || info.TimeSource == "gemischt")
            {
                string when = info.SetupSheetDate.HasValue ? " vom " + info.SetupSheetDate.Value.ToString("dd.MM.yyyy") : "";
                notes.Add((info.TimeSource == "gemischt" ? "teils aus Einrichteblatt" : "aus Einrichteblatt") + when);
            }
            timeNote.Text = string.Join("  ·  ", notes);
            timeNote.Visibility = notes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            timeNote.ToolTip = (total.HasValue && missing) || info.TimeSource == "Einrichteblatt" || info.TimeSource == "gemischt"
                ? "Unvollständig: nicht alle Jobs haben eine gespeicherte Zeit (neu oder nach der letzten Simulation geändert). Zeiten entstehen bei der Simulation im CAM-System.\nEinrichteblatt: für Jobs ohne eigene Zeitdatei stammt die Zeit aus dem zuletzt erzeugten Einrichteblatt."
                : null;
            // Maschine und Werkstoff in einer Zeile
            var details = new List<string>();
            if (!string.IsNullOrEmpty(info.Machine)) details.Add("Maschine: " + info.Machine);
            if (!string.IsNullOrEmpty(info.Material)) details.Add("Werkstoff: " + info.Material);
            // geschützte Leerzeichen: umgebrochen wird nur zwischen den Angaben, nie innerhalb eines Werts
            string line = string.Join("   ·   ", details.Select(d => d.Replace(" ", "\u00A0")));

            detailsText.Text = line;
            detailsText.Visibility = detailsText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

            // Eine Zeile: Operationen · Werkzeuge (laut Werkzeugtabelle) · Nullpunkte (aus der .PRT)
            var counts = new List<string>();
            counts.Add(info.Operations.Count + (info.Operations.Count == 1 ? " Operation" : " Operationen"));
            if (info.ToolsInUse > 0) counts.Add(info.ToolsInUse + (info.ToolsInUse == 1 ? " Werkzeug" : " Werkzeuge"));
            if (info.Homes.Count > 0) counts.Add(info.Homes.Count + (info.Homes.Count == 1 ? " Nullpunkt" : " Nullpunkte"));
            countsText.Text = string.Join("   ·   ", counts);
            countsText.Visibility = Visibility.Visible;
            // 3D-Ebenen: Fertigteil und Rohteil
            var part = new ModelViewer.Layer { Triangles = info.Triangles, Color = Color.FromRgb(0x9F, 0xB4, 0xC8) };
            // Rohteil standardmäßig ausgeblendet – per Schalter einblendbar
            var stock = new ModelViewer.Layer { Triangles = info.StockTriangles, Color = Color.FromRgb(0xF0, 0xA0, 0x30), Opacity = 0.35, Visible = false };
            viewer.SetLayers(new[] { part, stock });
            layerPills.Children.Clear();
            AddLayerPill("Fertigteil", Color.FromRgb(0x7F, 0x9C, 0xB8), part);
            AddLayerPill(info.StockSource == "nach Bearbeitung" ? "Rohteil (bearbeitet)" : "Rohteil", Color.FromRgb(0xF0, 0xA0, 0x30), stock);

            FillList();
        }

        void AddLayerPill(string text, Color dotColor, ModelViewer.Layer layer)
        {
            if (layer.Triangles == null || layer.Triangles.Length < 9) return;
            var dot = new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, Fill = new SolidColorBrush(dotColor), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            var label = new TextBlock { Text = text, FontSize = 12 };
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(dot);
            content.Children.Add(label);
            var pill = new Border
            {
                CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 3, 10, 4), Margin = new Thickness(0, 0, 6, 0),
                BorderBrush = CardBorder, BorderThickness = new Thickness(1), Cursor = Cursors.Hand, Child = content
            };
            Action apply = () =>
            {
                pill.Background = new SolidColorBrush(layer.Visible ? Color.FromArgb(0xEE, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF));
                label.Foreground = layer.Visible ? TextMain : TextSub;
                dot.Opacity = layer.Visible ? 1 : 0.3;
            };
            apply();
            pill.MouseLeftButtonDown += (s, e) => { viewer.SetLayerVisible(layer, !layer.Visible); apply(); e.Handled = true; };
            layerPills.Children.Add(pill);
        }

        void FillList()
        {
            var info = current;
            list.Items.Clear();
            if (info == null) return;

            listTitle.Text = "Operationen";
            sortButton.Text = sortByTime ? "nach Dauer ▾" : "nach Reihenfolge ▾";
            sortButton.Visibility = info.Operations.Any(o => o.Time.HasValue) ? Visibility.Visible : Visibility.Collapsed;

            var ops = info.Operations.Select((o, i) => new { Op = o, Nr = i + 1 }).ToList();
            if (sortByTime)
                ops = ops.OrderByDescending(x => x.Op.Time.HasValue ? x.Op.Time.Value.TotalSeconds : -1).ToList();
            double max = info.Operations.Where(o => o.Time.HasValue).Select(o => o.Time.Value.TotalSeconds).DefaultIfEmpty(0).Max();

            foreach (var x in ops) list.Items.Add(Row(x.Nr, x.Op, max));
        }

        UIElement Row(int nr, OperationInfo op, double maxSeconds)
        {
            var g = new Grid { Margin = new Thickness(12, 6, 12, 6) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var num = new TextBlock { Text = nr.ToString(), Foreground = TextSub, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center };
            var name = new TextBlock { Text = op.Name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, ToolTip = op.Name };
            var time = new TextBlock
            {
                Text = PrzInfo.FormatTime(op.Time),
                Foreground = op.Time.HasValue ? TextMain : TextSub,
                Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center
            };
            Typography.SetNumeralAlignment(time, FontNumeralAlignment.Tabular);
            if (op.TimeFromSetupSheet) time.ToolTip = "Zeit aus dem Einrichteblatt";
            else if (!op.Time.HasValue) time.ToolTip = "Keine Zeit gespeichert – Job neu oder nach der letzten Simulation geändert";
            Grid.SetColumn(name, 1);
            Grid.SetColumn(time, 2);
            g.Children.Add(num); g.Children.Add(name); g.Children.Add(time);

            return g;
        }

        public void ShowError(string message)
        {
            current = null;
            title.Text = "Vorschau nicht möglich";
            chips.Children.Clear();
            chips.Children.Add(Chip(message, WarnSoft, Warn));
            chips.Visibility = Visibility.Visible;
            countsText.Visibility = Visibility.Collapsed;
            totalText.Text = "";
            timeNote.Visibility = Visibility.Collapsed;
            detailsText.Visibility = Visibility.Collapsed;
            layerPills.Children.Clear();
            list.Items.Clear();
            listTitle.Text = "";
            sortButton.Visibility = Visibility.Collapsed;
            viewer.SetTriangles(null);
        }
    }
}
