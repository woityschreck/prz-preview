using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace PrzPreview
{
    /// <summary>
    /// Einfacher 3D-Betrachter (WPF): linke Maustaste drehen, rechte/mittlere verschieben,
    /// Mausrad zoomen, Doppelklick = Ansicht zurücksetzen.
    /// </summary>
    public class ModelViewer : Grid
    {
        readonly Viewport3D viewport = new Viewport3D();
        readonly PerspectiveCamera camera = new PerspectiveCamera();
        readonly ModelVisual3D modelVisual = new ModelVisual3D();
        readonly QuaternionRotation3D rotation = new QuaternionRotation3D();
        readonly TranslateTransform3D centerShift = new TranslateTransform3D();
        readonly TextBlock hint = new TextBlock();

        static readonly Vector3D ViewDir = new Vector3D(-1, 1, -0.8);   // isometrisch, Z oben
        static readonly Quaternion StartRotation = new Quaternion(new Vector3D(0, 0, 1), 0);

        double radius = 1, distance = 3;
        Vector3D pan;
        Point lastPos;
        MouseButton? dragButton;

        public ModelViewer()
        {
            Background = new LinearGradientBrush(Color.FromRgb(0xF4, 0xF6, 0xF9), Color.FromRgb(0xC9, 0xD1, 0xDC), 90);
            ClipToBounds = true;

            camera.FieldOfView = 35;
            camera.UpDirection = new Vector3D(0, 0, 1);
            viewport.Camera = camera;

            var lights = new Model3DGroup();
            lights.Children.Add(new AmbientLight(Color.FromRgb(0x55, 0x55, 0x55)));
            lights.Children.Add(new DirectionalLight(Color.FromRgb(0xC0, 0xC0, 0xC0), new Vector3D(-0.4, 0.6, -1)));
            lights.Children.Add(new DirectionalLight(Color.FromRgb(0x50, 0x50, 0x58), new Vector3D(0.6, -0.3, 0.6)));
            viewport.Children.Add(new ModelVisual3D { Content = lights });

            var tg = new Transform3DGroup();
            tg.Children.Add(centerShift);
            tg.Children.Add(new RotateTransform3D(rotation));
            modelVisual.Transform = tg;
            viewport.Children.Add(modelVisual);

            hint.Text = "Kein 3D-Modell in der Datei";
            hint.Foreground = Brushes.Gray;
            hint.HorizontalAlignment = HorizontalAlignment.Center;
            hint.VerticalAlignment = VerticalAlignment.Center;
            hint.Visibility = Visibility.Collapsed;

            Children.Add(viewport);
            Children.Add(hint);
        }

        /// <summary>Eine darstellbare Ebene (Fertigteil, Rohteil, Spannmittel ...).</summary>
        public class Layer
        {
            public float[] Triangles;
            public Color Color;
            public double Opacity = 1;
            public bool Visible = true;
            internal GeometryModel3D Model;
        }

        readonly System.Collections.Generic.List<Layer> layers = new System.Collections.Generic.List<Layer>();

        public void SetTriangles(float[] t)
        {
            SetLayers(new[] { new Layer { Triangles = t, Color = Color.FromRgb(0x9F, 0xB4, 0xC8) } });
        }

        /// <summary>Zeigt mehrere Ebenen; die Ansicht wird auf die erste Ebene (das Teil) zentriert.</summary>
        public void SetLayers(System.Collections.Generic.IEnumerable<Layer> newLayers)
        {
            layers.Clear();
            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
            bool first = true, any = false;

            foreach (var layer in newLayers)
            {
                var t = layer.Triangles;
                if (t == null || t.Length < 9) continue;
                var pos = new Point3DCollection(t.Length / 3);
                var nrm = new Vector3DCollection(t.Length / 3);
                for (int i = 0; i < t.Length; i += 9)
                {
                    var a = new Point3D(t[i], t[i + 1], t[i + 2]);
                    var b = new Point3D(t[i + 3], t[i + 4], t[i + 5]);
                    var c = new Point3D(t[i + 6], t[i + 7], t[i + 8]);
                    var n = Vector3D.CrossProduct(b - a, c - a);
                    if (n.LengthSquared > 0) n.Normalize();
                    pos.Add(a); pos.Add(b); pos.Add(c);
                    nrm.Add(n); nrm.Add(n); nrm.Add(n);
                    if (first)
                        foreach (var p in new[] { a, b, c })
                        {
                            if (p.X < minX) minX = p.X; if (p.X > maxX) maxX = p.X;
                            if (p.Y < minY) minY = p.Y; if (p.Y > maxY) maxY = p.Y;
                            if (p.Z < minZ) minZ = p.Z; if (p.Z > maxZ) maxZ = p.Z;
                        }
                }
                var mesh = new MeshGeometry3D { Positions = pos, Normals = nrm };
                mesh.Freeze();

                byte alpha = (byte)(255 * layer.Opacity);
                var col = Color.FromArgb(alpha, layer.Color.R, layer.Color.G, layer.Color.B);
                var mat = new MaterialGroup();
                mat.Children.Add(new DiffuseMaterial(new SolidColorBrush(col)));
                if (layer.Opacity >= 1)
                    mat.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromRgb(0x50, 0x50, 0x50)), 40));
                var back = new DiffuseMaterial(new SolidColorBrush(layer.Opacity >= 1 ? Color.FromRgb(0xC8, 0x80, 0x60) : col));
                layer.Model = new GeometryModel3D(mesh, mat) { BackMaterial = back };
                if (layer.Opacity < 1)
                {
                    // Transparente Hülle minimal vergrößern, damit deckungsgleiche Flächen nicht flimmern
                    var bb = mesh.Bounds;
                    layer.Model.Transform = new ScaleTransform3D(1.003, 1.003, 1.003,
                        bb.X + bb.SizeX / 2, bb.Y + bb.SizeY / 2, bb.Z + bb.SizeZ / 2);
                }
                layers.Add(layer);
                first = false;
                any = true;
            }

            hint.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
            RebuildScene();
            if (!any) return;

            centerShift.OffsetX = -(minX + maxX) / 2;
            centerShift.OffsetY = -(minY + maxY) / 2;
            centerShift.OffsetZ = -(minZ + maxZ) / 2;
            var size = new Vector3D(maxX - minX, maxY - minY, maxZ - minZ);
            radius = Math.Max(size.Length / 2, 1e-3);
            ResetView();
        }

        public void SetLayerVisible(Layer layer, bool visible)
        {
            layer.Visible = visible;
            RebuildScene();
        }

        void RebuildScene()
        {
            var group = new Model3DGroup();
            // Undurchsichtiges zuerst, transparente Ebenen zuletzt (WPF sortiert nicht selbst)
            foreach (var l in layers) if (l.Visible && l.Opacity >= 1) group.Children.Add(l.Model);
            foreach (var l in layers) if (l.Visible && l.Opacity < 1) group.Children.Add(l.Model);
            modelVisual.Content = group;
        }

        public void ResetView()
        {
            rotation.Quaternion = StartRotation;
            pan = new Vector3D();
            // WPF-FieldOfView ist horizontal; bei breiten Fenstern begrenzt der vertikale Winkel.
            double half = camera.FieldOfView * Math.PI / 360;
            double aspect = (ActualWidth > 0 && ActualHeight > 0) ? ActualWidth / ActualHeight : 1;
            double halfV = Math.Atan(Math.Tan(half) / aspect);
            distance = radius / Math.Sin(Math.Min(half, halfV)) * 1.05;
            viewTouched = false;
            UpdateCamera();
        }

        bool viewTouched;   // hat der Benutzer gedreht/gezoomt? Dann bei Größenänderung nicht zurücksetzen

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            if (!viewTouched && modelVisual.Content != null) ResetView();
        }

        void UpdateCamera()
        {
            var dir = ViewDir; dir.Normalize();
            camera.LookDirection = dir;
            camera.Position = new Point3D(0, 0, 0) + pan - dir * distance;
            camera.NearPlaneDistance = Math.Max(distance - radius * 2, distance * 0.01);
            camera.FarPlaneDistance = distance + radius * 4;
        }

        void ScreenAxes(out Vector3D right, out Vector3D up)
        {
            var look = ViewDir; look.Normalize();
            right = Vector3D.CrossProduct(look, camera.UpDirection); right.Normalize();
            up = Vector3D.CrossProduct(right, look); up.Normalize();
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.ClickCount == 2) { ResetView(); return; }
            dragButton = e.ChangedButton;
            viewTouched = true;
            lastPos = e.GetPosition(this);
            CaptureMouse();
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            base.OnMouseUp(e);
            dragButton = null;
            ReleaseMouseCapture();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragButton == null) return;
            var p = e.GetPosition(this);
            var d = p - lastPos;
            lastPos = p;
            Vector3D right, up;
            ScreenAxes(out right, out up);

            if (dragButton == MouseButton.Left)
            {
                var q = new Quaternion(up, d.X * 0.5) * new Quaternion(right, d.Y * 0.5);
                rotation.Quaternion = q * rotation.Quaternion;
            }
            else
            {
                double scale = 2 * distance * Math.Tan(camera.FieldOfView * Math.PI / 360) / Math.Max(ActualHeight, 1);
                pan += (-right * d.X + up * d.Y) * scale;
                UpdateCamera();
            }
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);
            viewTouched = true;
            distance *= Math.Pow(0.999, e.Delta);
            distance = Math.Max(distance, radius * 0.05);
            UpdateCamera();
        }
    }
}
