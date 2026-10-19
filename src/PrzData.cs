using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace PrzPreview
{
    public class OperationInfo
    {
        public string Id;          // z.B. JA21238
        public string Name;        // z.B. iRough_Aussen
        public TimeSpan? Time;
        public double? Length;     // Verfahrweg in mm
        public bool TimeFromSetupSheet;   // Zeit stammt aus dem Einrichteblatt (nicht aus der genauen Zeitdatei)
    }

    /// <summary>Liest eine CAM-Part-Datei (.prz = ZIP-Archiv des CAM-Part-Ordners).</summary>
    public class PrzInfo
    {
        public string PartName;
        public string Version;
        public List<OperationInfo> Operations = new List<OperationInfo>();
        public float[] Triangles;   // je 9 Werte pro Dreieck (3 Punkte x/y/z)
        public string ModelSource;  // aus welcher Datei das 3D-Modell stammt
        public string TimeSource;   // "dat.xml", "Einrichteblatt", "gemischt" oder null
        public DateTime? SetupSheetDate;   // wann das Einrichteblatt erzeugt wurde

        public float[] StockTriangles;    // aktualisiertes Rohteil (rm_after.STF) bzw. Rohmaterial
        public string StockSource;
        public float[] FixtureTriangles;  // positioniertes Spannmittel (.FSTF)
        public string FixtureName;        // z.B. "KSC-F 160-480"
        public List<string> FixtureParts = new List<string>();  // Komponenten laut ToolTable.tlp
        public List<HomeInfo> Homes = new List<HomeInfo>();     // Nullpunkte aus der .PRT

        public TimeSpan? TotalTime
        {
            get
            {
                var withTime = Operations.Where(o => o.Time.HasValue).ToList();
                if (withTime.Count == 0) return null;
                return TimeSpan.FromSeconds(withTime.Sum(o => o.Time.Value.TotalSeconds));
            }
        }

        static readonly Encoding Ansi = Encoding.GetEncoding(1252);

        public static PrzInfo Load(string path)
        {
            using (var fs = File.OpenRead(path)) return Load(fs);
        }

        public static PrzInfo Load(Stream stream)
        {
            var info = new PrzInfo();
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read, true))
            {
                var entries = zip.Entries.ToList();

                // Teilename = Name der .PRT im Wurzelverzeichnis
                var prt = entries.FirstOrDefault(e => !e.FullName.Contains("/") &&
                    e.FullName.EndsWith(".PRT", StringComparison.OrdinalIgnoreCase));
                if (prt != null)
                {
                    info.PartName = Path.GetFileNameWithoutExtension(prt.FullName);
                    var prtBytes = ReadAll(prt);
                    info.Operations = ReadOperationOrder(prtBytes);
                    info.Homes = HomeInfo.ReadAll(prtBytes);
                }
                else
                {
                    var dir = entries.Select(e => e.FullName.Split('/')[0]).FirstOrDefault();
                    info.PartName = dir ?? "?";
                }

                var version = Find(entries, "Version.info");
                if (version != null)
                {
                    var m = Regex.Match(Ansi.GetString(ReadAll(version)), @"To\s*:\s*(\S[^\r\n]*)");   // Zeile "To :<CAM-System> <Version>"
                    if (m.Success) info.Version = m.Groups[1].Value.Trim();
                }

                // Zeiten: zuerst die genauen Zeitdateien je Job, Lücken aus dem Einrichteblatt füllen
                ReadTimesFromDatXml(entries, info);
                ReadSetupSheet(entries, info);
                bool fromDat = info.Operations.Exists(o => o.Time.HasValue && !o.TimeFromSetupSheet);
                bool fromSheet = info.Operations.Exists(o => o.Time.HasValue && o.TimeFromSetupSheet);
                info.TimeSource = fromDat && fromSheet ? "gemischt" : fromDat ? "dat.xml" : fromSheet ? "Einrichteblatt" : null;

                foreach (var name in new[] { "Fertigmodell.STL", "DesignModel.STL" })
                {
                    var stl = Find(entries, name);
                    if (stl == null) continue;
                    try
                    {
                        info.Triangles = StlReader.Read(ReadAll(stl));
                        info.ModelSource = name;
                        break;
                    }
                    catch (Exception) { /* nächste Quelle probieren */ }
                }

                ReadStock(entries, info);
                ReadFixture(entries, info);
            }
            return info;
        }

        /// <summary>Rohteil: nach der letzten Operation (rm_after.STF), sonst Ausgangs-Rohmaterial.</summary>
        static void ReadStock(List<ZipArchiveEntry> entries, PrzInfo info)
        {
            foreach (var name in new[] { "rm_after.STF", "Rohmaterial.FST", "Rohmaterial.stl" })
            {
                var e = Find(entries, name);
                if (e == null) continue;
                try
                {
                    var b = ReadAll(e);
                    var t = name.EndsWith(".stl", StringComparison.OrdinalIgnoreCase) ? StlReader.Read(b) : StfReader.Read(b);
                    if (t.Length == 0) continue;
                    info.StockTriangles = t;
                    info.StockSource = name.StartsWith("rm_after") ? "nach Bearbeitung" : "Ausgangs-Rohteil";
                    return;
                }
                catch (Exception) { }
            }
        }

        /// <summary>
        /// Spannmittel: "FixtureForS-&lt;Aufspannung&gt;_T-&lt;Tisch&gt;_F-&lt;Name&gt;_&lt;ID&gt;.FSTF" enthält das
        /// positionierte Mesh; Komponenten stehen in ToolTable.tlp (CompTool mit dieser ID).
        /// </summary>
        static void ReadFixture(List<ZipArchiveEntry> entries, PrzInfo info)
        {
            var fstf = entries
                .Where(e => e.Name.StartsWith("FixtureFor", StringComparison.OrdinalIgnoreCase) &&
                            e.Name.EndsWith(".FSTF", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(e => e.LastWriteTime).ToList();
            if (fstf.Count == 0) return;

            string compId = null;
            var m = Regex.Match(fstf[0].Name, @"_F-(.+)_(\d+)\.FSTF$", RegexOptions.IgnoreCase);
            if (m.Success) { info.FixtureName = m.Groups[1].Value; compId = m.Groups[2].Value; }

            // Spannmittel-Mesh wird (noch) nicht angezeigt -> nicht laden, spart Zeit und Speicher.


            var tlp = Find(entries, "ToolTable.tlp");
            if (tlp == null || compId == null) return;
            try
            {
                var text = Encoding.GetEncoding(28591).GetString(ReadAll(tlp));
                // Oberstes CompTool mit dieser ID finden und dessen direkte Komponenten-Namen lesen
                var start = Regex.Match(text, "<CompTool Ver=\"\\d+\" ID=\"" + compId + "\" [^>]*Name=\"([^\"]*)\"[^>]*ToolNumber=");
                if (!start.Success) return;
                if (string.IsNullOrEmpty(info.FixtureName)) info.FixtureName = start.Groups[1].Value;
                int depth = 0, i = start.Index;
                var parts = new List<string>();
                foreach (Match t in Regex.Matches(text.Substring(i), @"<(/?)CompTool\b([^>]*)>"))
                {
                    if (t.Groups[1].Value == "/") { depth--; if (depth == 0) break; continue; }
                    var attrs = t.Groups[2].Value;
                    bool selfClosing = attrs.EndsWith("/");
                    if (!selfClosing) depth++;
                    var nm = Regex.Match(attrs, "Name=\"([^\"]*)\"");
                    if ((selfClosing ? depth + 1 : depth) == 2 && nm.Success) parts.Add(nm.Groups[1].Value);
                }
                info.FixtureParts = parts;
            }
            catch (Exception) { }
        }

        static ZipArchiveEntry Find(List<ZipArchiveEntry> entries, string fileName)
        {
            return entries.FirstOrDefault(e =>
                string.Equals(e.Name, fileName, StringComparison.OrdinalIgnoreCase) &&
                e.FullName.Count(c => c == '/') == 1);
        }

        static byte[] ReadAll(ZipArchiveEntry e)
        {
            using (var s = e.Open())
            using (var ms = new MemoryStream())
            {
                s.CopyTo(ms);
                return ms.ToArray();
            }
        }

        /// <summary>
        /// Die .PRT enthält am Anfang die Operationsliste in Bearbeitungsreihenfolge:
        /// [uint32 Länge][Name\0][uint32 8]["JAxxxxx\0"] ...
        /// </summary>
        static List<OperationInfo> ReadOperationOrder(byte[] b)
        {
            var ops = new List<OperationInfo>();
            var seen = new HashSet<string>();
            for (int i = 4; i + 13 <= b.Length; i++)
            {
                // Suche "\x08\0\0\0JA" + 5 Ziffern + \0
                if (b[i] != 8 || b[i + 1] != 0 || b[i + 2] != 0 || b[i + 3] != 0) continue;
                if (b[i + 4] != 'J' || b[i + 5] != 'A' || b[i + 11] != 0) continue;
                bool digits = true;
                for (int k = 6; k < 11; k++) if (b[i + k] < '0' || b[i + k] > '9') digits = false;
                if (!digits) continue;

                // Davor muss der Name mit Längenpräfix stehen
                int nameEnd = i - 1;            // Position der abschließenden 0
                if (nameEnd < 0 || b[nameEnd] != 0) continue;
                string name = null;
                for (int len = 2; len <= 256 && nameEnd - len + 1 - 4 >= 0; len++)
                {
                    int start = nameEnd - len + 1;
                    int declared = BitConverter.ToInt32(b, start - 4);
                    if (declared == len)
                    {
                        name = Ansi.GetString(b, start, len - 1);
                        break;
                    }
                }
                if (name == null) continue;

                string id = Encoding.ASCII.GetString(b, i + 4, 7);
                if (!seen.Add(id)) break;   // Liste wiederholt sich weiter hinten in der Datei
                ops.Add(new OperationInfo { Id = id, Name = name });
            }
            return ops;
        }

        /// <summary>
        /// Zeitdateien je Job (vom CAM-System bei der Zeitberechnung geschrieben). Benannt nach der Job-Nummer
        /// ("JA21238.dat.xml") oder nach dem Jobnamen ("Anguss Bo Fräsen.dat.xml"). Zeitdateien gelöschter
        /// Jobs werden ignoriert.
        /// </summary>
        static void ReadTimesFromDatXml(List<ZipArchiveEntry> entries, PrzInfo info)
        {
            foreach (var e in entries.Where(x => x.Name.EndsWith(".dat.xml", StringComparison.OrdinalIgnoreCase) &&
                                                 x.FullName.Count(c => c == '/') == 1))
            {
                XElement x;
                try { using (var s = e.Open()) x = XElement.Load(s); } catch { continue; }
                string baseName = e.Name.Substring(0, e.Name.Length - ".dat.xml".Length);
                string jobName = ((string)x.Element("JobName") ?? "").Trim();
                var op = info.Operations.FirstOrDefault(o => string.Equals(o.Id, baseName, StringComparison.OrdinalIgnoreCase))
                      ?? info.Operations.FirstOrDefault(o => o.Name == jobName && !o.Time.HasValue);
                if (op == null)
                {
                    // Ohne Operationsliste (kein .PRT) alles übernehmen, sonst gehört die Datei zu einem gelöschten Job
                    if (info.Operations.Count > 0 || !Regex.IsMatch(baseName, @"^JA\d+$")) continue;
                    op = new OperationInfo { Id = baseName, Name = jobName.Length > 0 ? jobName : baseName };
                    info.Operations.Add(op);
                }
                op.Time = ParseTime((string)x.Element("Time"));
                op.Length = ParseDouble((string)x.Element("Length"));
            }
        }

        /// <summary>
        /// Einrichteblatt (Doc/d.xml): füllt Zeiten der Jobs, die keine Zeitdatei haben. Ob diese Zeiten noch
        /// aktuell sind, lässt sich nicht feststellen (das CAM-System schreibt beim Speichern alle Jobdateien neu) –
        /// deshalb wird nur das Datum des Einrichteblatts angezeigt.
        /// </summary>
        static void ReadSetupSheet(List<ZipArchiveEntry> entries, PrzInfo info)
        {
            var d = entries.FirstOrDefault(e => e.FullName.EndsWith("/Doc/d.xml", StringComparison.OrdinalIgnoreCase));
            if (d == null) return;
            XDocument doc;
            try { using (var s = d.Open()) doc = XDocument.Load(s); } catch { return; }

            bool used = false;
            string pendingName = null;
            foreach (var el in doc.Descendants())
            {
                if (el.Name.LocalName == "SCOperationName")
                    pendingName = ((string)el.Element("Value") ?? "").Trim();
                else if (el.Name.LocalName == "SCOperationTime" && pendingName != null)
                {
                    var t = ParseTime((string)el.Element("Value"));
                    var op = info.Operations.FirstOrDefault(o => o.Name == pendingName && !o.Time.HasValue);
                    if (op == null && info.Operations.Count == 0)
                    {
                        op = new OperationInfo { Name = pendingName };
                        info.Operations.Add(op);
                    }
                    // op == null: Job hat schon eine genaue Zeit oder existiert nicht mehr -> überspringen
                    if (op != null && t.HasValue)
                    {
                        op.Time = t;
                        op.TimeFromSetupSheet = true;
                        used = true;
                    }
                    pendingName = null;
                }
            }
            if (used) info.SetupSheetDate = d.LastWriteTime.LocalDateTime;
        }
        static TimeSpan? ParseTime(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            var p = s.Trim().Split(':');
            int h = 0, m = 0, sec = 0;
            try
            {
                if (p.Length == 3) { h = int.Parse(p[0]); m = int.Parse(p[1]); sec = int.Parse(p[2]); }
                else if (p.Length == 2) { m = int.Parse(p[0]); sec = int.Parse(p[1]); }
                else return null;
            }
            catch { return null; }
            return new TimeSpan(h, m, sec);
        }

        static double? ParseDouble(string s)
        {
            double v;
            if (s != null && double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return v;
            return null;
        }

        public static string FormatTime(TimeSpan? t)
        {
            if (!t.HasValue) return "–";
            return string.Format("{0}:{1:00}:{2:00}", (int)t.Value.TotalHours, t.Value.Minutes, t.Value.Seconds);
        }
    }

    /// <summary>
    /// Nullpunkt (Koordinatensystem). Die .PRT ist ein Schlüssel-Wert-Speicher in zwei Varianten:
    ///   Maker/Demo:   [u32 Schlüssel]['0'+Schlüssel%7][u32 Länge][Daten], Ganzzahlen mit Prüfzeichen
    ///   Vollversion:  [u32 Schlüssel][u32 Länge][Daten]
    /// Jeder Nullpunkt hat einen Ursprung (Schlüssel 0x2C78, 3 double) und davor seine Z-Richtung (0x2C66).
    /// </summary>
    public class HomeInfo
    {
        public double[] Origin;
        public double[] Direction;
        public int Number;     // Nullpunkt-Nummer (MAC), z.B. 6 bei "6-1"; 0 = unbekannt
        public int Position;   // Position innerhalb der Nummer, z.B. 2 bei "1-2"

        /// <summary>Bezeichnung wie im CAM-System, z.B. "1-2".</summary>
        public string Name { get { return Number > 0 ? Number + "-" + Position : null; } }

        const uint KeyOrigin = 0x2C78, KeyDirection = 0x2C66;

        /// <summary>Kopf eines Eintrags mit 24 Byte Daten (3 double); withCheck = Maker-Variante.</summary>
        static byte[] Pattern(uint key, bool withCheck)
        {
            var p = new byte[withCheck ? 9 : 8];
            BitConverter.GetBytes(key).CopyTo(p, 0);
            if (withCheck) p[4] = (byte)('0' + key % 7);
            BitConverter.GetBytes(24).CopyTo(p, withCheck ? 5 : 4);
            return p;
        }

        static IEnumerable<int> FindAll(byte[] b, byte[] pat, int from, int to)
        {
            for (int i = Math.Max(from, 0); i <= Math.Min(to, b.Length) - pat.Length - 24; i++)
            {
                int k = 0;
                while (k < pat.Length && b[i + k] == pat[k]) k++;
                if (k == pat.Length) yield return i;
            }
        }

        static double[] Vec(byte[] b, int dataAt)
        {
            return new[] { BitConverter.ToDouble(b, dataAt), BitConverter.ToDouble(b, dataAt + 8), BitConverter.ToDouble(b, dataAt + 16) };
        }

        public static List<HomeInfo> ReadAll(byte[] prt)
        {
            var list = ReadAll(prt, true);          // Maker-/Demo-Variante
            if (list.Count == 0) list = ReadAll(prt, false);   // Vollversion
            return list;
        }

        static List<HomeInfo> ReadAll(byte[] prt, bool withCheck)
        {
            var list = new List<HomeInfo>();
            var originPat = Pattern(KeyOrigin, withCheck);
            var dirPat = Pattern(KeyDirection, withCheck);
            int head = originPat.Length;
            foreach (int at in FindAll(prt, originPat, 0, prt.Length).ToList())
            {
                double[] dir = null;
                foreach (int d in FindAll(prt, dirPat, at - 1200, at))
                {
                    var v = Vec(prt, d + head);
                    if (Math.Abs(v[0]) + Math.Abs(v[1]) + Math.Abs(v[2]) > 1e-9) dir = v;   // letzter Nicht-Nullvektor davor
                }
                list.Add(new HomeInfo { Origin = Vec(prt, at + head), Direction = dir });
            }

            // Nullpunkt-Nummern: vor jedem Nullpunkt-Block steht das verschachtelte Feld "0x3F = 0",
            // direkt gefolgt von "0x40 = Nummer".
            var numbers = new List<int>();
            int step = withCheck ? 10 : 8;
            for (int p = step; p + step <= prt.Length; p++)
            {
                int v, marker;
                if (NestedInt(prt, p, 0x40, withCheck, out v) && v > 0 && v < 10000 &&
                    NestedInt(prt, p - step, 0x3F, withCheck, out marker) && marker == 0)
                    numbers.Add(v);
            }
            if (numbers.Count == list.Count)
            {
                var seen = new Dictionary<int, int>();
                for (int i = 0; i < list.Count; i++)
                {
                    int n = numbers[i], c;
                    seen.TryGetValue(n, out c);
                    seen[n] = ++c;
                    list[i].Number = n;
                    list[i].Position = c;   // zweites Vorkommen derselben Nummer = Position 2 usw.
                }
            }
            return list;
        }

        /// <summary>Verschachtelte Ganzzahl: Maker [Feld]['0'+Feld%7][Wert]['0'+Wert%7], Vollversion [Feld][Wert].</summary>
        static bool NestedInt(byte[] b, int p, uint key, bool withCheck, out int value)
        {
            value = 0;
            int size = withCheck ? 10 : 8;
            if (p < 0 || p + size > b.Length) return false;
            if (BitConverter.ToUInt32(b, p) != key) return false;
            if (!withCheck) { value = BitConverter.ToInt32(b, p + 4); return true; }
            if (b[p + 4] != '0' + key % 7) return false;
            value = BitConverter.ToInt32(b, p + 5);
            int mod = ((value % 7) + 7) % 7;
            return b[p + 9] == '0' + mod;
        }
        /// <summary>Z-Achse des Nullpunkts in SolidWorks-Modellkoordinaten, z.B. "Richtung Z+".</summary>
        public string DirectionText
        {
            get
            {
                if (Direction == null) return "?";
                double x = Direction[0], y = Direction[1], z = Direction[2];
                const double t = 0.999;
                if (z > t) return "Richtung Z+";
                if (z < -t) return "Richtung Z−";
                if (x > t) return "Richtung X+";
                if (x < -t) return "Richtung X−";
                if (y > t) return "Richtung Y+";
                if (y < -t) return "Richtung Y−";
                return string.Format(CultureInfo.InvariantCulture, "schräg ({0:0.###}, {1:0.###}, {2:0.###})", x, y, z);
            }
        }

        public string OriginText
        {
            get { return string.Format(CultureInfo.CurrentCulture, "X {0:0.###}  Y {1:0.###}  Z {2:0.###}", Origin[0], Origin[1], Origin[2]); }
        }
    }

    /// <summary>
    /// Mesh-Format des CAM-Systems (.STF/.FST/.FCT/.FSTF): 1024 Byte Kopf, dann int Körperanzahl; je Körper
    /// 6 double Bounding Box, int Punktanzahl, je Punkt 48 Byte (x,y,z double + Reserve),
    /// int Dreiecksanzahl, je Dreieck 3 int Indizes.
    /// </summary>
    public static class StfReader
    {
        public static float[] Read(byte[] b)
        {
            int o = 0x400;
            int bodies = BitConverter.ToInt32(b, o); o += 4;
            if (bodies < 0 || bodies > 10000) throw new InvalidDataException("Keine STF-Datei");
            var result = new List<float>();
            for (int k = 0; k < bodies; k++)
            {
                o += 48;   // Bounding Box
                int nv = BitConverter.ToInt32(b, o); o += 4;
                if (nv < 0 || o + (long)nv * 48 > b.Length) throw new InvalidDataException("STF beschädigt");
                var v = new float[nv * 3];
                for (int i = 0; i < nv; i++)
                    for (int c = 0; c < 3; c++)
                        v[i * 3 + c] = (float)BitConverter.ToDouble(b, o + i * 48 + c * 8);
                o += nv * 48;
                int nt = BitConverter.ToInt32(b, o); o += 4;
                if (nt < 0 || o + (long)nt * 12 > b.Length) throw new InvalidDataException("STF beschädigt");
                for (int i = 0; i < nt * 3; i++)
                {
                    int idx = BitConverter.ToInt32(b, o + i * 4);
                    if (idx < 0 || idx >= nv) throw new InvalidDataException("STF-Index ungültig");
                    result.Add(v[idx * 3]); result.Add(v[idx * 3 + 1]); result.Add(v[idx * 3 + 2]);
                }
                o += nt * 12;
            }
            return result.ToArray();
        }
    }

    public static class StlReader
    {
        public static float[] Read(byte[] b)
        {
            if (b.Length >= 84)
            {
                uint n = BitConverter.ToUInt32(b, 80);
                if (84 + 50L * n == b.Length)
                {
                    var tris = new float[n * 9];
                    for (int t = 0; t < n; t++)
                    {
                        int off = 84 + t * 50 + 12;   // Normale überspringen
                        for (int k = 0; k < 9; k++) tris[t * 9 + k] = BitConverter.ToSingle(b, off + k * 4);
                    }
                    return tris;
                }
            }
            // ASCII-STL
            var text = Encoding.ASCII.GetString(b);
            var list = new List<float>();
            foreach (Match m in Regex.Matches(text, @"vertex\s+(\S+)\s+(\S+)\s+(\S+)"))
                for (int k = 1; k <= 3; k++)
                    list.Add(float.Parse(m.Groups[k].Value, CultureInfo.InvariantCulture));
            if (list.Count == 0 || list.Count % 9 != 0) throw new InvalidDataException("Keine gültige STL");
            return list.ToArray();
        }
    }
}
