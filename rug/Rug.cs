// Windows Rug - a cloth-simulated carpet that lives on the desktop, above the
// icons and below every other window. Compiled once by Install.ps1 into
// WindowsRug.exe (via Add-Type), so keep it C# 5 compatible.
//
// The simulation only runs while the rug is moving; once it settles the timer
// stops and the process sits idle at 0% CPU.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace WindowsRug
{
    public static class App
    {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

        [STAThread]
        public static void Main()
        {
            Run(AppDomain.CurrentDomain.BaseDirectory);
        }

        public static void Run(string dir)
        {
            bool created;
            using (var mutex = new Mutex(true, "WindowsRug_SingleInstance", out created))
            {
                if (!created) return;
                try { SetProcessDPIAware(); } catch { }
                Application.EnableVisualStyles();
                Application.Run(new RugForm(dir));
            }
        }
    }

    // Premultiplied ARGB texture.
    public class Texture
    {
        public int W, H;
        public int[] Px;

        public static Texture FromBitmap(Bitmap b)
        {
            var t = new Texture { W = b.Width, H = b.Height, Px = new int[b.Width * b.Height] };
            var data = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            for (int y = 0; y < t.H; y++)
                Marshal.Copy(new IntPtr(data.Scan0.ToInt64() + (long)y * data.Stride), t.Px, y * t.W, t.W);
            b.UnlockBits(data);
            return t;
        }
    }

    // Verlet cloth on a grid. X/Y are screen pixels, Z is height above the desk.
    public class Cloth
    {
        public readonly int NX, NY, N;
        public readonly float[] X, Y, Z, PX, PY, PZ;
        readonly bool[] supported, onLayer;   // resting on something / resting on the rug itself
        readonly int[] ca, cb;
        readonly float[] cr, ck;
        public readonly float Thickness, Spacing;

        public int Grab = -1;

        // Smooth "shake it out" unfold: points glide to the flat layout.
        readonly float[] RX, RY;           // flat layout, centred on the origin
        float[] fromX, fromY, fromZ;
        float toCX, toCY, toCos, toSin;
        public float UnfoldT = -1;          // 0..1 while unfolding, -1 otherwise
        public bool Unfolding { get { return UnfoldT >= 0; } }
        public float GrabX, GrabY, GrabZ, GrabLift;
        public float Memory;                // 0..1 pull back towards lying flat
        public readonly float HalfW, HalfH;  // half size of the flat rug

        // Something under the rug (the cat!) raises the desk locally.
        public bool BumpOn;
        public float BumpX, BumpY, BumpH, BumpR;

        // Edge points that just hit the desk hard (for dust), and a one-shot
        // flag for when an unfold lands.
        public readonly List<int> Impacts = new List<int>();
        public bool JustLanded;

        public bool IsEdge(int i) { int a = i % NX, b = i / NX; return a == 0 || b == 0 || a == NX - 1 || b == NY - 1; }

        // Point on the (possibly folded) rug at flat-layout coordinates u, v.
        public void Sample(float u, float v, out float x, out float y, out float z)
        {
            float fi = (u - RX[0]) / (RX[1] - RX[0]), fj = (v - RY[0]) / (RY[NX] - RY[0]);
            fi = Math.Max(0, Math.Min(NX - 1.001f, fi)); fj = Math.Max(0, Math.Min(NY - 1.001f, fj));
            int i = (int)fi, j = (int)fj; float a = fi - i, b = fj - j;
            int k = j * NX + i;
            float w00 = (1 - a) * (1 - b), w10 = a * (1 - b), w01 = (1 - a) * b, w11 = a * b;
            x = X[k] * w00 + X[k + 1] * w10 + X[k + NX] * w01 + X[k + NX + 1] * w11;
            y = Y[k] * w00 + Y[k + 1] * w10 + Y[k + NX] * w01 + Y[k + NX + 1] * w11;
            z = Z[k] * w00 + Z[k + 1] * w10 + Z[k + NX] * w01 + Z[k + NX + 1] * w11;
        }
        public float Settling;              // 0..1 extra damping used to make the rug come to rest
        float MaxHeight { get { return Spacing * 6; } }

        public static int[,] Links = { { 1, 0 }, { 0, 1 }, { 1, 1 }, { 1, -1 }, { 2, 0 }, { 0, 2 }, { 2, 2 }, { 2, -2 } };
        public static float[] Stiff = { 1f, 1f, 0.3f, 0.3f, 0.4f, 0.4f, 0f, 0f };

        public static float LayerGrip = 0.9f;

        const float Gravity = 0.32f;
        const int Iterations = 10;

        public Cloth(int nx, int ny, float left, float top, float w, float h)
        {
            NX = nx; NY = ny; N = nx * ny;
            X = new float[N]; Y = new float[N]; Z = new float[N];
            PX = new float[N]; PY = new float[N]; PZ = new float[N];
            supported = new bool[N]; onLayer = new bool[N];
            float dx = w / (nx - 1), dy = h / (ny - 1);
            Spacing = Math.Min(dx, dy);
            Thickness = Math.Max(2f, Spacing * 0.22f);
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                {
                    int k = j * nx + i;
                    X[k] = PX[k] = left + i * dx;
                    Y[k] = PY[k] = top + j * dy;
                    supported[k] = true;
                }

            HalfW = w / 2; HalfH = h / 2;
            RX = new float[N]; RY = new float[N];
            float mx0 = left + w / 2, my0 = top + h / 2;
            for (int k = 0; k < N; k++) { RX[k] = X[k] - mx0; RY[k] = Y[k] - my0; }

            var a = new List<int>(); var b = new List<int>(); var s = new List<float>();
            // Structural, shear and bend springs. Position-based stiffness compounds over
            // the solver iterations, so the small bend values still give a soft drape.
            int[,] links = Links;
            float[] stiff = Stiff;
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                    for (int l = 0; l < stiff.Length; l++)
                    {
                        int i2 = i + links[l, 0], j2 = j + links[l, 1];
                        if (i2 < 0 || i2 >= nx || j2 < 0 || j2 >= ny) continue;
                        a.Add(j * nx + i); b.Add(j2 * nx + i2); s.Add(stiff[l]);
                    }
            ca = a.ToArray(); cb = b.ToArray(); ck = s.ToArray();
            cr = new float[ca.Length];
            for (int c = 0; c < ca.Length; c++)
            {
                float ex = X[cb[c]] - X[ca[c]], ey = Y[cb[c]] - Y[ca[c]];
                cr[c] = (float)Math.Sqrt(ex * ex + ey * ey);
            }
        }

        public float CenterX { get { float s = 0; for (int i = 0; i < N; i++) s += X[i]; return s / N; } }
        public float CenterY { get { float s = 0; for (int i = 0; i < N; i++) s += Y[i]; return s / N; } }

        // Best-fit placement (centre and rotation) of the flat layout onto the current shape.
        public void FitFlat(out float cx, out float cy, out float cos, out float sin)
        {
            cx = CenterX; cy = CenterY;
            double sc = 0, ss = 0;
            for (int i = 0; i < N; i++)
            {
                float px = X[i] - cx, py = Y[i] - cy;
                sc += RX[i] * px + RY[i] * py;
                ss += RX[i] * py - RY[i] * px;
            }
            double ang = Math.Atan2(ss, sc);
            cos = (float)Math.Cos(ang); sin = (float)Math.Sin(ang);
        }

        // A real rug "wants" to lie flat: once let go, folds flop back open and
        // heaps spread out, while it stays where it was dropped.
        void RelaxToFlat(float k)
        {
            float cx, cy, cos, sin;
            FitFlat(out cx, out cy, out cos, out sin);
            for (int i = 0; i < N; i++)
            {
                float tx = cx + RX[i] * cos - RY[i] * sin, ty = cy + RX[i] * sin + RY[i] * cos;
                X[i] += (tx - X[i]) * k;
                Y[i] += (ty - Y[i]) * k;
            }
        }

        public void BeginUnfold()
        {
            Grab = -1;
            FitFlat(out toCX, out toCY, out toCos, out toSin);
            fromX = (float[])X.Clone(); fromY = (float[])Y.Clone(); fromZ = (float[])Z.Clone();
            UnfoldT = 0;
        }

        public void CancelUnfold()
        {
            if (!Unfolding) return;
            UnfoldT = -1;
            for (int i = 0; i < N; i++) { PX[i] = X[i]; PY[i] = Y[i]; PZ[i] = Z[i]; }
        }

        float UnfoldStep()
        {
            UnfoldT = Math.Min(1, UnfoldT + 1 / 80f);
            float t = UnfoldT, e = t * t * (3 - 2 * t);
            float lift = (float)Math.Sin(Math.PI * t) * Spacing * 0.9f;  // lifted so layers slide apart
            float maxMove = 0;
            for (int i = 0; i < N; i++)
            {
                float tx = toCX + RX[i] * toCos - RY[i] * toSin, ty = toCY + RX[i] * toSin + RY[i] * toCos;
                float nx = fromX[i] + (tx - fromX[i]) * e, ny = fromY[i] + (ty - fromY[i]) * e;
                float nz = fromZ[i] * (1 - e) + lift;
                float m = Math.Abs(nx - X[i]) + Math.Abs(ny - Y[i]) + Math.Abs(nz - Z[i]);
                if (m > maxMove) maxMove = m;
                X[i] = PX[i] = nx; Y[i] = PY[i] = ny; Z[i] = PZ[i] = nz;
                supported[i] = nz <= 0.01f;
            }
            if (t >= 1) { UnfoldT = -1; JustLanded = true; }
            return Math.Max(maxMove, 1);
        }

        // One simulation step. Returns the largest distance any point moved.
        public float Step()
        {
            if (Unfolding) return UnfoldStep();
            for (int i = 0; i < N; i++)
            {
                float vx = X[i] - PX[i], vy = Y[i] - PY[i], vz = Z[i] - PZ[i];
                if (onLayer[i])
                {
                    // Pile sliding on pile: much slicker than rug on the desk.
                    vx *= LayerGrip; vy *= LayerGrip;
                }
                else if (supported[i])
                {
                    // Friction against the desk.
                    vx *= 0.55f; vy *= 0.55f;
                    if (vx * vx + vy * vy < 0.0009f) { vx = 0; vy = 0; }
                }
                else { vx *= 0.993f; vy *= 0.993f; }
                vz *= 0.985f;
                if (Settling > 0) { float k = 1 - Settling; vx *= k; vy *= k; vz *= k; }
                // Never let a point jump more than half a cell in one step.
                float v2 = vx * vx + vy * vy + vz * vz, vmax = Spacing * 0.5f;
                if (v2 > vmax * vmax) { float k = vmax / (float)Math.Sqrt(v2); vx *= k; vy *= k; vz *= k; }
                PX[i] = X[i]; PY[i] = Y[i]; PZ[i] = Z[i];
                X[i] += vx; Y[i] += vy; Z[i] += vz - Gravity;
            }

            if (Grab >= 0)
            {
                GrabZ += (GrabLift - GrabZ) * 0.18f;
                X[Grab] = GrabX; Y[Grab] = GrabY; Z[Grab] = GrabZ;
            }

            for (int it = 0; it < Iterations; it++)
            {
                for (int c = 0; c < ca.Length; c++)
                {
                    int p = ca[c], q = cb[c];
                    float ex = X[q] - X[p], ey = Y[q] - Y[p], ez = Z[q] - Z[p];
                    float d = (float)Math.Sqrt(ex * ex + ey * ey + ez * ez);
                    if (d < 1e-5f) continue;
                    float diff = (d - cr[c]) / d * ck[c];
                    float wp = p == Grab ? 0 : 1, wq = q == Grab ? 0 : 1;
                    float sum = wp + wq;
                    if (sum == 0) continue;
                    float fp = diff * wp / sum, fq = diff * wq / sum;
                    X[p] += ex * fp; Y[p] += ey * fp; Z[p] += ez * fp;
                    X[q] -= ex * fq; Y[q] -= ey * fq; Z[q] -= ez * fq;
                }
                if ((it & 3) == 3) ClampFloor(false);
            }

            ClampFloor(true);
            SelfCollide();
            Friction();
            if (Memory > 0 && Grab < 0) RelaxToFlat(Memory);

            float maxMove = 0;
            for (int i = 0; i < N; i++)
            {
                float mx = X[i] - PX[i], my = Y[i] - PY[i], mz = Z[i] - PZ[i];
                float m = mx * mx + my * my + mz * mz;
                if (m > maxMove) maxMove = m;
            }
            return (float)Math.Sqrt(maxMove);
        }

        // Coulomb-style friction for points resting on the desk or on the rug:
        // small pulls are cancelled (static), bigger ones are slowed (kinetic).
        void Friction()
        {
            const float staticLimit = 0.015f, kinetic = 0.02f;
            for (int i = 0; i < N; i++)
            {
                if (!supported[i] || onLayer[i] || i == Grab) continue;
                float dx = X[i] - PX[i], dy = Y[i] - PY[i];
                float d = (float)Math.Sqrt(dx * dx + dy * dy);
                if (d < staticLimit) { X[i] = PX[i]; Y[i] = PY[i]; }
                else { float k = 1 - kinetic / d; X[i] = PX[i] + dx * k; Y[i] = PY[i] + dy * k; }
            }
        }

        void ClampFloor(bool final)
        {
            for (int i = 0; i < N; i++)
            {
                if (final) { supported[i] = false; onLayer[i] = false; }
                if (Z[i] > MaxHeight) Z[i] = MaxHeight;
                float floor = 0;
                if (BumpOn)
                {
                    float dx = X[i] - BumpX, dy = Y[i] - BumpY;
                    floor = BumpH * (float)Math.Exp(-(dx * dx + dy * dy) / (BumpR * BumpR));
                }
                if (Z[i] <= floor)
                {
                    if (final && PZ[i] - Z[i] > 1.4f && IsEdge(i)) Impacts.Add(i);
                    Z[i] = floor;
                    if (final) { supported[i] = true; PZ[i] = floor; }
                }
            }
        }

        // Keeps folded layers stacked instead of passing through each other.
        int[] cellStart, cellFill, cellItems, cellOf;
        void SelfCollide()
        {
            float cell = Spacing;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < N; i++)
            {
                if (X[i] < minX) minX = X[i]; if (X[i] > maxX) maxX = X[i];
                if (Y[i] < minY) minY = Y[i]; if (Y[i] > maxY) maxY = Y[i];
            }
            int gw = Math.Min(4096, (int)((maxX - minX) / cell) + 1), gh = Math.Min(4096, (int)((maxY - minY) / cell) + 1);
            cell = Math.Max(cell, Math.Max((maxX - minX) / (gw - 0.5f), (maxY - minY) / (gh - 0.5f)));
            int cells = gw * gh;
            if (cellStart == null || cellStart.Length < cells + 1) { cellStart = new int[cells + 1]; cellFill = new int[cells + 1]; }
            else { Array.Clear(cellStart, 0, cells + 1); Array.Clear(cellFill, 0, cells + 1); }
            if (cellItems == null) { cellItems = new int[N]; cellOf = new int[N]; lift = new float[N]; }
            Array.Clear(lift, 0, N);
            for (int i = 0; i < N; i++)
            {
                int c = (int)((Y[i] - minY) / cell) * gw + (int)((X[i] - minX) / cell);
                cellOf[i] = c;
                cellStart[c + 1]++;
            }
            for (int c = 0; c < cells; c++) cellStart[c + 1] += cellStart[c];
            for (int i = 0; i < N; i++) { int c = cellOf[i]; cellItems[cellStart[c] + cellFill[c]++] = i; }

            float r2 = Spacing * 0.95f * Spacing * 0.95f;
            for (int i = 0; i < N; i++)
            {
                int ci = cellOf[i], cx = ci % gw, cy = ci / gw;
                int ii = i % NX, ij = i / NX;
                for (int oy = -1; oy <= 1; oy++)
                {
                    int yy = cy + oy; if (yy < 0 || yy >= gh) continue;
                    for (int ox = -1; ox <= 1; ox++)
                    {
                        int xx = cx + ox; if (xx < 0 || xx >= gw) continue;
                        int c = yy * gw + xx;
                        for (int s = cellStart[c]; s < cellStart[c + 1]; s++)
                        {
                            int j = cellItems[s];
                            if (j <= i) continue;
                            int dj = j / NX - ij, di = j % NX - ii;
                            if (di >= -2 && di <= 2 && dj >= -2 && dj <= 2) continue;
                            float ex = X[j] - X[i], ey = Y[j] - Y[i];
                            if (ex * ex + ey * ey > r2) continue;
                            float dz = Z[j] - Z[i];
                            if (dz >= Thickness || dz <= -Thickness) continue;
                            // Whoever was higher before this step stays on top.
                            bool jUp = PZ[j] != PZ[i] ? PZ[j] > PZ[i] : Z[j] >= Z[i];
                            int up = jUp ? j : i, down = jUp ? i : j;
                            if (up == Grab) continue;
                            // Collected and applied afterwards from this step's heights, so
                            // stacks can't ratchet themselves upwards within one pass.
                            float need = Math.Min(Z[down] + Thickness - Z[up], Thickness * 0.5f);
                            if (need > lift[up]) lift[up] = need;
                            supported[up] = true; onLayer[up] = true;
                        }
                    }
                }
            }
            for (int i = 0; i < N; i++)
            {
                if (lift[i] <= 0) continue;
                Z[i] += lift[i];
                if (PZ[i] < Z[i] - 0.5f) PZ[i] = Z[i];
            }
        }

        float[] lift;

        // A puff of air lifting one edge.
        public void Gust(Random rnd)
        {
            int side = rnd.Next(4);
            float power = 2.5f + (float)rnd.NextDouble() * 3f;
            for (int j = 0; j < NY; j++)
                for (int i = 0; i < NX; i++)
                {
                    float t;
                    if (side == 0) t = 1 - i / (float)(NX - 1);
                    else if (side == 1) t = i / (float)(NX - 1);
                    else if (side == 2) t = 1 - j / (float)(NY - 1);
                    else t = j / (float)(NY - 1);
                    t = t * t * t * t;
                    int k = j * NX + i;
                    PZ[k] = Z[k] - power * t;
                    supported[k] = false;
                }
        }
    }

    // Software rasterizer: textured, lit cloth mesh plus soft shadows, written
    // into a premultiplied BGRA buffer for UpdateLayeredWindow.
    public class Renderer
    {
        public int OX, OY, W, H;      // buffer origin on screen and size
        int SW, SH;                    // half-resolution shadow buffer size
        public int[] Out;              // premultiplied ARGB
        public int[] Hit;              // topmost triangle per pixel, -1 = none
        public float CamX, CamY;       // point on screen the camera looks straight down at
        public int Pad = 16;           // margin around the rug (room for shadows, dust, the cat)
        float[] shadow, tmp, casterZ, depth;
        int capacity;

        // Light from the top-left; shadows fall bottom-right.
        const float SX = 0.45f, SY = 0.65f;
        const float CamH = 2200f;
        const float Ambient = 0.36f, Diffuse = 0.8f;
        static readonly float LX, LY, LZ, Flat;

        static Renderer()
        {
            float lx = -0.35f, ly = -0.5f, lz = 0.8f, l = (float)Math.Sqrt(lx * lx + ly * ly + lz * lz);
            LX = lx / l; LY = ly / l; LZ = lz / l;
            Flat = Ambient + Diffuse * LZ;
        }

        float[] vx, vy, vz, sFront, sBack, vu, vv;
        int[] tri, triOrder;
        float[] triKey;

        void Setup(Cloth c, Texture tex)
        {
            int n = c.N;
            vx = new float[n]; vy = new float[n]; vz = new float[n];
            sFront = new float[n]; sBack = new float[n]; vu = new float[n]; vv = new float[n];
            int tris = (c.NX - 1) * (c.NY - 1) * 2;
            tri = new int[tris * 3]; triKey = new float[tris]; triOrder = new int[tris];
            int t = 0;
            for (int j = 0; j < c.NY - 1; j++)
                for (int i = 0; i < c.NX - 1; i++)
                {
                    int a = j * c.NX + i, b = a + 1, d = a + c.NX, e = d + 1;
                    tri[t++] = a; tri[t++] = b; tri[t++] = d;
                    tri[t++] = b; tri[t++] = e; tri[t++] = d;
                }
            for (int j = 0; j < c.NY; j++)
                for (int i = 0; i < c.NX; i++)
                {
                    vu[j * c.NX + i] = i / (float)(c.NX - 1) * (tex.W - 1);
                    vv[j * c.NX + i] = j / (float)(c.NY - 1) * (tex.H - 1);
                }
        }

        Cloth lastCloth;

        public void Draw(Cloth c, Texture tex)
        {
            if (c != lastCloth) { Setup(c, tex); lastCloth = c; }

            // Project vertices and light them.
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int j = 0; j < c.NY; j++)
                for (int i = 0; i < c.NX; i++)
                {
                    int k = j * c.NX + i;
                    float f = CamH / (CamH - Math.Min(c.Z[k], CamH * 0.5f));
                    vx[k] = CamX + (c.X[k] - CamX) * f;
                    vy[k] = CamY + (c.Y[k] - CamY) * f;
                    vz[k] = c.Z[k];
                    int il = j * c.NX + Math.Max(0, i - 1), ir = j * c.NX + Math.Min(c.NX - 1, i + 1);
                    int jt = Math.Max(0, j - 1) * c.NX + i, jb = Math.Min(c.NY - 1, j + 1) * c.NX + i;
                    float ax = c.X[ir] - c.X[il], ay = c.Y[ir] - c.Y[il], az = c.Z[ir] - c.Z[il];
                    float bx = c.X[jb] - c.X[jt], by = c.Y[jb] - c.Y[jt], bz = c.Z[jb] - c.Z[jt];
                    float nx = ay * bz - az * by, ny = az * bx - ax * bz, nz = ax * by - ay * bx;
                    float nl = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz) + 1e-6f;
                    float ndl = (nx * LX + ny * LY + nz * LZ) / nl;
                    sFront[k] = (Ambient + Diffuse * Math.Max(0, ndl)) / Flat;
                    sBack[k] = (Ambient + Diffuse * Math.Max(0, -ndl)) / Flat;
                    float sxp = c.X[k] + c.Z[k] * SX, syp = c.Y[k] + c.Z[k] * SY;
                    minX = Math.Min(minX, Math.Min(vx[k], sxp)); maxX = Math.Max(maxX, Math.Max(vx[k], sxp));
                    minY = Math.Min(minY, Math.Min(vy[k], syp)); maxY = Math.Max(maxY, Math.Max(vy[k], syp));
                }

            int pad = Pad;
            OX = (int)Math.Floor(minX) - pad; OY = (int)Math.Floor(minY) - pad;
            W = ((int)Math.Ceiling(maxX) + pad - OX + 15) & ~15;
            H = ((int)Math.Ceiling(maxY) + pad - OY + 15) & ~15;
            int size = W * H;
            SW = W / 2; SH = H / 2;
            int ssize = SW * SH;
            if (size > capacity)
            {
                capacity = size + size / 4;
                Out = new int[capacity]; Hit = new int[capacity]; depth = new float[capacity];
                shadow = new float[capacity / 4 + 1]; tmp = new float[capacity / 4 + 1]; casterZ = new float[capacity / 4 + 1];
            }
            Array.Clear(shadow, 0, ssize);
            for (int i = 0; i < ssize; i++) casterZ[i] = -1;
            for (int i = 0; i < size; i++) { Hit[i] = -1; depth[i] = float.MinValue; }

            int tcount = triKey.Length;

            // Shadow pass at half resolution: project every triangle onto the desk.
            for (int t = 0; t < tcount; t++)
            {
                int a = tri[t * 3], b = tri[t * 3 + 1], d = tri[t * 3 + 2];
                ShadowTri((c.X[a] + c.Z[a] * SX - OX) * 0.5f, (c.Y[a] + c.Z[a] * SY - OY) * 0.5f, c.Z[a],
                          (c.X[b] + c.Z[b] * SX - OX) * 0.5f, (c.Y[b] + c.Z[b] * SY - OY) * 0.5f, c.Z[b],
                          (c.X[d] + c.Z[d] * SX - OX) * 0.5f, (c.Y[d] + c.Z[d] * SY - OY) * 0.5f, c.Z[d]);
                triKey[t] = c.Z[a] + c.Z[b] + c.Z[d];
                triOrder[t] = t;
            }
            BoxBlur(shadow, 3); BoxBlur(shadow, 3);
            BoxBlur(casterZ, 2);

            for (int y = 0; y < H; y++)
            {
                int srow = Math.Min(SH - 1, y >> 1) * SW, row = y * W;
                for (int x = 0; x < W; x++)
                {
                    int al = (int)(shadow[srow + Math.Min(SW - 1, x >> 1)] * 255);
                    Out[row + x] = al > 255 ? unchecked((int)0xFF000000) : al << 24;
                }
            }

            // Colour pass, lowest triangles first.
            Array.Sort(triKey, triOrder);
            for (int s = 0; s < tcount; s++)
            {
                int t = triOrder[s];
                ColorTri(t, tri[t * 3], tri[t * 3 + 1], tri[t * 3 + 2], tex, c.Thickness);
            }
        }

        void ShadowTri(float x0, float y0, float z0, float x1, float y1, float z1, float x2, float y2, float z2)
        {
            float area = (x1 - x0) * (y2 - y0) - (x2 - x0) * (y1 - y0);
            if (Math.Abs(area) < 1e-4f) return;
            int minx = Math.Max(0, (int)Math.Min(x0, Math.Min(x1, x2))), maxx = Math.Min(SW - 1, (int)Math.Max(x0, Math.Max(x1, x2)) + 1);
            int miny = Math.Max(0, (int)Math.Min(y0, Math.Min(y1, y2))), maxy = Math.Min(SH - 1, (int)Math.Max(y0, Math.Max(y1, y2)) + 1);
            float inv = 1 / area;
            float dw0 = (y1 - y2) * inv, dw1 = (y2 - y0) * inv;
            for (int y = miny; y <= maxy; y++)
            {
                float py = y + 0.5f, px = minx + 0.5f;
                float w0 = ((x1 - px) * (y2 - py) - (x2 - px) * (y1 - py)) * inv;
                float w1 = ((x2 - px) * (y0 - py) - (x0 - px) * (y2 - py)) * inv;
                int row = y * SW;
                for (int x = minx; x <= maxx; x++, w0 += dw0, w1 += dw1)
                {
                    float w2 = 1 - w0 - w1;
                    if (w0 < -0.001f || w1 < -0.001f || w2 < -0.001f) continue;
                    float z = w0 * z0 + w1 * z1 + w2 * z2;
                    float a = 0.55f / (1 + z * 0.018f);
                    int idx = row + x;
                    if (a > shadow[idx]) shadow[idx] = a;
                    if (z > casterZ[idx]) casterZ[idx] = z;
                }
            }
        }

        void ColorTri(int t, int a, int b, int d, Texture tex, float thick)
        {
            float x0 = vx[a] - OX, y0 = vy[a] - OY, x1 = vx[b] - OX, y1 = vy[b] - OY, x2 = vx[d] - OX, y2 = vy[d] - OY;
            float area = (x1 - x0) * (y2 - y0) - (x2 - x0) * (y1 - y0);
            if (Math.Abs(area) < 1e-4f) return;
            bool back = area < 0;
            float[] sh = back ? sBack : sFront;
            float s0 = sh[a], s1 = sh[b], s2 = sh[d];
            float z0 = vz[a], z1 = vz[b], z2 = vz[d];
            float u0 = vu[a], u1 = vu[b], u2 = vu[d], v0 = vv[a], v1 = vv[b], v2 = vv[d];
            int minx = Math.Max(0, (int)Math.Min(x0, Math.Min(x1, x2))), maxx = Math.Min(W - 1, (int)Math.Max(x0, Math.Max(x1, x2)) + 1);
            int miny = Math.Max(0, (int)Math.Min(y0, Math.Min(y1, y2))), maxy = Math.Min(H - 1, (int)Math.Max(y0, Math.Max(y1, y2)) + 1);
            float inv = 1 / area;
            float dw0 = (y1 - y2) * inv, dw1 = (y2 - y0) * inv;
            int tw = tex.W, th = tex.H;
            int[] px = tex.Px;
            float selfGap = thick * 1.5f;
            // Is anything folded over this triangle? If not, skip the self-shadow lookups.
            bool covered = false;
            {
                float zmin = Math.Min(z0, Math.Min(z1, z2));
                int sx0 = Math.Max(0, minx / 2), sx1 = Math.Min(SW - 1, maxx / 2 + 1 + (int)(zmin * SX));
                int sy0 = Math.Max(0, miny / 2), sy1 = Math.Min(SH - 1, maxy / 2 + 1 + (int)(zmin * SY));
                for (int sy = sy0; sy <= sy1 && !covered; sy++)
                    for (int sx = sx0; sx <= sx1; sx++)
                        if (casterZ[sy * SW + sx] > zmin + selfGap) { covered = true; break; }
            }
            for (int y = miny; y <= maxy; y++)
            {
                float py = y + 0.5f, pxs = minx + 0.5f;
                float w0 = ((x1 - pxs) * (y2 - py) - (x2 - pxs) * (y1 - py)) * inv;
                float w1 = ((x2 - pxs) * (y0 - py) - (x0 - pxs) * (y2 - py)) * inv;
                int row = y * W;
                for (int x = minx; x <= maxx; x++, w0 += dw0, w1 += dw1)
                {
                    float w2 = 1 - w0 - w1;
                    if (w0 < -0.001f || w1 < -0.001f || w2 < -0.001f) continue;

                    // Depth test: only the topmost layer shows.
                    float z = w0 * z0 + w1 * z1 + w2 * z2;
                    int idx = row + x;
                    if (z < depth[idx] - 0.35f) continue;

                    // Bilinear texture fetch.
                    float u = w0 * u0 + w1 * u1 + w2 * u2, v = w0 * v0 + w1 * v1 + w2 * v2;
                    int iu = (int)u, iv = (int)v;
                    if (iu < 0) iu = 0; if (iv < 0) iv = 0;
                    if (iu > tw - 2) iu = tw - 2; if (iv > th - 2) iv = th - 2;
                    float fu = u - iu, fv = v - iv;
                    if (fu < 0) fu = 0; if (fu > 1) fu = 1; if (fv < 0) fv = 0; if (fv > 1) fv = 1;
                    int i00 = iv * tw + iu;
                    int c00 = px[i00], c10 = px[i00 + 1], c01 = px[i00 + tw], c11 = px[i00 + tw + 1];
                    float k00 = (1 - fu) * (1 - fv), k10 = fu * (1 - fv), k01 = (1 - fu) * fv, k11 = fu * fv;
                    float sa = ((c00 >> 24) & 255) * k00 + ((c10 >> 24) & 255) * k10 + ((c01 >> 24) & 255) * k01 + ((c11 >> 24) & 255) * k11;
                    if (sa < 2) continue;
                    float sr = ((c00 >> 16) & 255) * k00 + ((c10 >> 16) & 255) * k10 + ((c01 >> 16) & 255) * k01 + ((c11 >> 16) & 255) * k11;
                    float sg = ((c00 >> 8) & 255) * k00 + ((c10 >> 8) & 255) * k10 + ((c01 >> 8) & 255) * k01 + ((c11 >> 8) & 255) * k11;
                    float sb = (c00 & 255) * k00 + (c10 & 255) * k10 + (c01 & 255) * k01 + (c11 & 255) * k11;

                    float light = w0 * s0 + w1 * s1 + w2 * s2;
                    if (back)
                    {
                        // Underside: the dull, flat weave of the backing.
                        float l = (sr + sg + sb) / 3;
                        sr = (sr * 0.45f + l * 0.55f) * 0.62f; sg = (sg * 0.45f + l * 0.55f) * 0.58f; sb = (sb * 0.45f + l * 0.55f) * 0.52f;
                    }

                    if (covered)
                    {
                        // Shadow cast by a layer folded over this one.
                        int sxp = (int)(x + 0.5f + z * SX) >> 1, syp = (int)(py + z * SY) >> 1;
                        if (sxp < SW && syp < SH)
                        {
                            float over = casterZ[syp * SW + sxp] - z - selfGap;
                            if (over > 0) light *= 1 - 0.38f * Math.Min(1, over / 12f + 0.35f);
                        }
                    }

                    sr *= light; sg *= light; sb *= light;
                    if (sr > sa) sr = sa; if (sg > sa) sg = sa; if (sb > sa) sb = sa;

                    if (sa > 128 && z > depth[idx]) depth[idx] = z;
                    if (sa >= 253)
                        Out[idx] = unchecked((int)0xFF000000) | ((int)sr << 16) | ((int)sg << 8) | (int)sb;
                    else
                    {
                        int dst = Out[idx];
                        float k = 1 - sa / 255f;
                        int oa = (int)(sa + ((dst >> 24) & 255) * k), orr = (int)(sr + ((dst >> 16) & 255) * k);
                        int og = (int)(sg + ((dst >> 8) & 255) * k), ob = (int)(sb + (dst & 255) * k);
                        Out[idx] = (Math.Min(255, oa) << 24) | (Math.Min(255, orr) << 16) | (Math.Min(255, og) << 8) | Math.Min(255, ob);
                    }
                    if (sa > 80) Hit[idx] = t;
                }
            }
        }

        // Separable box blur over the half-resolution shadow buffers.
        void BoxBlur(float[] buf, int r)
        {
            int w = SW, h = SH;
            float norm = 1f / (2 * r + 1);
            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                float acc = 0;
                for (int x = -r; x <= r; x++) acc += buf[row + Math.Max(0, Math.Min(w - 1, x))];
                for (int x = 0; x < w; x++)
                {
                    tmp[row + x] = acc * norm;
                    acc += buf[row + Math.Min(w - 1, x + r + 1)] - buf[row + Math.Max(0, x - r)];
                }
            }
            for (int x = 0; x < w; x++)
            {
                float acc = 0;
                for (int y = -r; y <= r; y++) acc += tmp[Math.Max(0, Math.Min(h - 1, y)) * w + x];
                for (int y = 0; y < h; y++)
                {
                    buf[y * w + x] = acc * norm;
                    acc += tmp[Math.Min(h - 1, y + r + 1) * w + x] - tmp[Math.Max(0, y - r) * w + x];
                }
            }
        }
    }

    // A puff of dust kicked up when the rug slaps down on the desk.
    public class Dust
    {
        struct Mote { public float X, Y, VX, VY, Life, Size, Span; }
        readonly List<Mote> motes = new List<Mote>();
        readonly Random rnd = new Random();
        public int Count { get { return motes.Count; } }

        public void Emit(float x, float y, float dx, float dy, int n, float scale)
        {
            for (int k = 0; k < n && motes.Count < 220; k++)
            {
                float ang = (float)Math.Atan2(dy, dx) + (float)(rnd.NextDouble() - 0.5) * 1.6f;
                float sp = (15 + (float)rnd.NextDouble() * 60) * scale;
                x += (float)(rnd.NextDouble() - 0.5) * 14 * scale; y += (float)(rnd.NextDouble() - 0.5) * 14 * scale;
                motes.Add(new Mote {
                    X = x, Y = y, VX = (float)Math.Cos(ang) * sp, VY = (float)Math.Sin(ang) * sp,
                    Life = 1, Size = (7 + (float)rnd.NextDouble() * 10) * scale, Span = 0.8f + (float)rnd.NextDouble() * 0.9f });
            }
        }

        public void Update(float dt)
        {
            for (int k = motes.Count - 1; k >= 0; k--)
            {
                var m = motes[k];
                m.X += m.VX * dt; m.Y += m.VY * dt;
                m.VX *= 0.93f; m.VY *= 0.93f; m.VY -= 4 * dt;   // drifts up a touch
                m.Size += 16 * dt;
                m.Life -= dt / m.Span;
                if (m.Life <= 0) motes.RemoveAt(k); else motes[k] = m;
            }
        }

        public void Draw(Graphics g, int ox, int oy)
        {
            foreach (var m in motes)
            {
                int a = (int)(34 * m.Life);
                if (a <= 0) continue;
                // Three soft layers fake a blurred puff.
                for (int k = 0; k < 3; k++)
                {
                    float r = m.Size * (1 - k * 0.3f);
                    using (var br = new SolidBrush(Color.FromArgb(a, 218, 208, 190)))
                        g.FillEllipse(br, m.X - ox - r, m.Y - oy - r, r * 2, r * 2);
                }
            }
        }
    }

    // A cat that now and then visits the rug: it naps on it, or crawls underneath
    // (a moving lump). Nothing runs while it is away or asleep.
    public class Cat
    {
        enum State { Away, Walk, Nap, Hide, Flee }
        State state = State.Away;
        bool napAfterWalk;
        float u, v, heading, phase, alpha, napLeft, scale = 1;
        readonly List<PointF> path = new List<PointF>();
        readonly Random rnd = new Random();
        float sx, sy, sz;   // last screen position

        // Pre-rendered sprites from cat.png: 8 walk frames + sleeping, shadows below.
        Bitmap[] sprites, shadows;
        int cellW, cellH;

        public void LoadSprites(Bitmap sheet)
        {
            const int frames = 9;
            cellW = sheet.Width / frames; cellH = sheet.Height / 2;
            sprites = new Bitmap[frames]; shadows = new Bitmap[frames];
            for (int i = 0; i < frames; i++)
            {
                sprites[i] = sheet.Clone(new Rectangle(i * cellW, 0, cellW, cellH), PixelFormat.Format32bppPArgb);
                shadows[i] = sheet.Clone(new Rectangle(i * cellW, cellH, cellW, cellH), PixelFormat.Format32bppPArgb);
            }
        }

        public bool Visible { get { return state == State.Walk || state == State.Nap || state == State.Flee; } }
        public bool Busy { get { return state == State.Walk || state == State.Hide || state == State.Flee; } }
        public bool Present { get { return state != State.Away; } }
        public bool Hiding { get { return state == State.Hide; } }
        public bool Napping { get { return state == State.Nap; } }
        public float NapLeft { get { return napLeft; } }
        public void EndNap() { if (state == State.Nap) napLeft = 0; }

        PointF EdgePoint(Cloth c, float inset)
        {
            float hw = c.HalfW * inset, hh = c.HalfH * inset;
            switch (rnd.Next(4))
            {
                case 0: return new PointF(-hw, (float)(rnd.NextDouble() * 2 - 1) * hh);
                case 1: return new PointF(hw, (float)(rnd.NextDouble() * 2 - 1) * hh);
                case 2: return new PointF((float)(rnd.NextDouble() * 2 - 1) * hw, -hh);
                default: return new PointF((float)(rnd.NextDouble() * 2 - 1) * hw, hh);
            }
        }

        PointF Inside(Cloth c, float f)
        {
            return new PointF((float)(rnd.NextDouble() * 2 - 1) * c.HalfW * f, (float)(rnd.NextDouble() * 2 - 1) * c.HalfH * f);
        }

        public void Visit(Cloth c, float rugScale)
        {
            if (state != State.Away) return;
            scale = rugScale * 2.3f;   // a cat is about a quarter of a rug's width
            var start = EdgePoint(c, 0.9f);
            u = start.X; v = start.Y; alpha = 0; phase = 0;
            path.Clear();
            if (rnd.NextDouble() < 0.6)
            {
                state = State.Walk; napAfterWalk = true;
                path.Add(Inside(c, 0.5f));
            }
            else
            {
                state = State.Hide;
                path.Add(Inside(c, 0.6f)); path.Add(Inside(c, 0.6f));
                var end = EdgePoint(c, 0.92f); path.Add(end);
            }
            heading = (float)Math.Atan2(path[0].Y - v, path[0].X - u);
        }

        // Startled: run for the nearest edge.
        public void Flee(Cloth c)
        {
            if (state == State.Away || state == State.Flee) return;
            float hw = c.HalfW, hh = c.HalfH;
            float[] d = { u + hw, hw - u, v + hh, hh - v };
            int best = 0; for (int k = 1; k < 4; k++) if (d[k] < d[best]) best = k;
            var p = best == 0 ? new PointF(-hw * 1.1f, v) : best == 1 ? new PointF(hw * 1.1f, v) : best == 2 ? new PointF(u, -hh * 1.1f) : new PointF(u, hh * 1.1f);
            path.Clear(); path.Add(p);
            if (state == State.Hide) alpha = 1;
            state = State.Flee;
            c.BumpOn = false;
        }

        public bool HitTest(Point p)
        {
            if (!Visible) return false;
            float dx = p.X - sx, dy = p.Y - sy;
            return dx * dx + dy * dy < 24 * 24 * scale * scale;
        }

        public void Update(float dt, Cloth c)
        {
            if (state == State.Away) return;
            if (state == State.Nap)
            {
                napLeft -= dt;
                if (napLeft <= 0) { state = State.Walk; napAfterWalk = false; path.Clear(); path.Add(EdgePoint(c, 1.1f)); }
                return;
            }
            float speed = (state == State.Flee ? 190 : state == State.Hide ? 45 : 60) * scale;
            if (path.Count > 0)
            {
                float tx = path[0].X - u, ty = path[0].Y - v, d = (float)Math.Sqrt(tx * tx + ty * ty);
                float step = speed * dt;
                if (d <= step) { u = path[0].X; v = path[0].Y; path.RemoveAt(0); }
                else { u += tx / d * step; v += ty / d * step; }
                float want = (float)Math.Atan2(ty, tx), diff = want - heading;
                while (diff > Math.PI) diff -= (float)(2 * Math.PI);
                while (diff < -Math.PI) diff += (float)(2 * Math.PI);
                heading += diff * Math.Min(1, dt * 6);
                phase += step / (6 * scale);
            }
            bool leaving = (state == State.Flee || (state == State.Walk && !napAfterWalk)) &&
                           (Math.Abs(u) > c.HalfW * 0.9f || Math.Abs(v) > c.HalfH * 0.9f);
            alpha = leaving ? Math.Max(0, alpha - dt * 3) : Math.Min(1, alpha + dt * 2.5f);

            if (state == State.Hide)
            {
                float cx, cy, cos, sin;
                c.FitFlat(out cx, out cy, out cos, out sin);
                c.BumpOn = true;
                c.BumpX = cx + u * cos - v * sin; c.BumpY = cy + u * sin + v * cos;
                c.BumpH = 12 * scale; c.BumpR = 13 * scale;
            }
            if (path.Count == 0)
            {
                if (state == State.Walk && napAfterWalk) { state = State.Nap; napLeft = 60 + (float)rnd.NextDouble() * 120; }
                else { state = State.Away; c.BumpOn = false; }
            }
            else if (leaving && alpha <= 0) { state = State.Away; c.BumpOn = false; }
        }

        public void Draw(Graphics g, Cloth c, int ox, int oy)
        {
            if (!Visible || alpha <= 0.01f) return;
            c.Sample(u, v, out sx, out sy, out sz);
            float cx, cy, cos, sin;
            c.FitFlat(out cx, out cy, out cos, out sin);
            float rugAngle = (float)Math.Atan2(sin, cos);
            if (sprites != null) { DrawSprite(g, ox, oy, rugAngle); return; }
            var st = g.Save();
            g.TranslateTransform(sx - ox, sy - oy);
            float s = scale * (1 + sz * 0.004f);
            g.ScaleTransform(s, s);
            int a = (int)(255 * alpha);
            if (state == State.Nap) DrawCurled(g, a, rugAngle + heading);
            else DrawWalking(g, a, (float)((rugAngle + heading) * 180 / Math.PI), (float)Math.Sin(phase));
            g.Restore(st);
        }

        void DrawSprite(Graphics g, int ox, int oy, float rugAngle)
        {
            int frame = state == State.Nap ? 8 : (((int)(phase / (2 * Math.PI) * 8)) % 8 + 8) % 8;
            float k = scale / 2.3f * (1 + sz * 0.004f);
            float deg = (float)((rugAngle + heading) * 180 / Math.PI);
            g.InterpolationMode = InterpolationMode.Bilinear;
            using (var ia = new ImageAttributes())
            {
                var cm = new ColorMatrix { Matrix33 = alpha };
                ia.SetColorMatrix(cm);
                float rad = (float)(deg * Math.PI / 180), cs = (float)Math.Cos(rad) * k, sn = (float)Math.Sin(rad) * k;
                for (int pass = 0; pass < 2; pass++)
                {
                    // The shadow falls toward the bottom-right whatever way the cat faces.
                    float off = pass == 0 ? 1 : 0;
                    float px = sx - ox + off * 5 * k, py = sy - oy + off * 7 * k;
                    float hx = cellW / 2f, hy = cellH / 2f;
                    var dest = new[] {
                        new PointF(px - hx * cs + hy * sn, py - hx * sn - hy * cs),   // top-left
                        new PointF(px + hx * cs + hy * sn, py + hx * sn - hy * cs),   // top-right
                        new PointF(px - hx * cs - hy * sn, py - hx * sn + hy * cs) }; // bottom-left
                    var img = pass == 0 ? shadows[frame] : sprites[frame];
                    g.DrawImage(img, dest, new RectangleF(0, 0, cellW, cellH), GraphicsUnit.Pixel, ia);
                }
            }
        }

        static readonly Color Fur = Color.FromArgb(226, 142, 66), Stripe = Color.FromArgb(176, 94, 38), Ear = Color.FromArgb(238, 170, 158);

        static void DrawWalking(Graphics g, int a, float deg, float swing)
        {
            using (var sh = new SolidBrush(Color.FromArgb(a * 55 / 255, 0, 0, 0)))
                g.FillEllipse(sh, -20, -8, 46, 22);
            g.RotateTransform(deg);
            using (var fur = new SolidBrush(Color.FromArgb(a, Fur)))
            using (var dark = new SolidBrush(Color.FromArgb(a, Stripe)))
            using (var ear = new SolidBrush(Color.FromArgb(a, Ear)))
            using (var tail = new Pen(Color.FromArgb(a, Fur), 5.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            using (var stripe = new Pen(Color.FromArgb(a, Stripe), 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                float s1 = swing * 5;
                // legs
                g.FillEllipse(dark, 10 + s1, -11, 8, 5); g.FillEllipse(dark, 10 - s1, 6, 8, 5);
                g.FillEllipse(dark, -16 - s1, -11, 8, 5); g.FillEllipse(dark, -16 + s1, 6, 8, 5);
                // tail
                g.DrawBezier(tail, -18, 0, -28, swing * 4, -36, -swing * 6, -44, swing * 3 - 4);
                // body
                g.FillEllipse(fur, -22, -9.5f, 42, 19);
                g.DrawArc(stripe, -14, -9, 6, 18, -70, 140);
                g.DrawArc(stripe, -6, -9, 6, 18, -70, 140);
                g.DrawArc(stripe, 2, -9, 6, 18, -70, 140);
                // head
                g.FillPolygon(fur, new[] { new PointF(20, -5), new PointF(23, -12), new PointF(27, -5) });
                g.FillPolygon(fur, new[] { new PointF(20, 5), new PointF(23, 12), new PointF(27, 5) });
                g.FillPolygon(ear, new[] { new PointF(21.5f, -5.5f), new PointF(23, -9.5f), new PointF(25, -5.5f) });
                g.FillPolygon(ear, new[] { new PointF(21.5f, 5.5f), new PointF(23, 9.5f), new PointF(25, 5.5f) });
                g.FillEllipse(fur, 15, -8, 17, 16);
                g.DrawLine(stripe, 18, 0, 22, 0);
                using (var eye = new SolidBrush(Color.FromArgb(a, 40, 30, 20)))
                { g.FillEllipse(eye, 26, -4.5f, 2.6f, 2.6f); g.FillEllipse(eye, 26, 1.9f, 2.6f, 2.6f); }
                using (var nose = new SolidBrush(Color.FromArgb(a, 220, 120, 120)))
                    g.FillEllipse(nose, 30.2f, -1.2f, 2.4f, 2.4f);
            }
        }

        static void DrawCurled(Graphics g, int a, float rad)
        {
            using (var sh = new SolidBrush(Color.FromArgb(a * 60 / 255, 0, 0, 0)))
                g.FillEllipse(sh, -15, -13, 38, 36);
            g.RotateTransform((float)(rad * 180 / Math.PI));
            using (var fur = new SolidBrush(Color.FromArgb(a, Fur)))
            using (var ear = new SolidBrush(Color.FromArgb(a, Ear)))
            using (var tail = new Pen(Color.FromArgb(a, Fur), 6f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            using (var tailTip = new Pen(Color.FromArgb(a, Stripe), 6f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            using (var stripe = new Pen(Color.FromArgb(a, Stripe), 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            using (var lid = new Pen(Color.FromArgb(a, 70, 45, 30), 1.2f))
            {
                g.FillEllipse(fur, -16, -15, 32, 30);
                g.DrawArc(stripe, -10, -11, 20, 22, 200, 80);
                g.DrawArc(stripe, -6, -7, 12, 14, 200, 80);
                g.DrawArc(stripe, -12, -13, 24, 26, 300, 60);
                g.DrawArc(tail, -17, -16, 34, 32, 20, 150);
                g.DrawArc(tailTip, -17, -16, 34, 32, 155, 15);
                // tucked head
                g.FillPolygon(fur, new[] { new PointF(2, -12), new PointF(4, -19), new PointF(8, -12) });
                g.FillPolygon(fur, new[] { new PointF(10, -11), new PointF(15, -16), new PointF(15, -8) });
                g.FillPolygon(ear, new[] { new PointF(3.5f, -12.5f), new PointF(4.5f, -16.5f), new PointF(7, -12.5f) });
                g.FillEllipse(fur, 0, -14, 17, 15);
                g.DrawArc(lid, 4, -8, 4, 3, 0, 180);
                g.DrawArc(lid, 10, -8, 4, 3, 0, 180);
            }
        }
    }

    // Moves the real desktop icons (the shell's list view lives in Explorer, so
    // reading positions and names goes through a small block of its memory).
    public static class DesktopIcons
    {
        [DllImport("user32.dll")] static extern IntPtr FindWindow(string cls, string name);
        [DllImport("user32.dll")] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string name);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int idx);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")] static extern bool ScreenToClient(IntPtr hWnd, ref Point p);
        [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll")] static extern IntPtr VirtualAllocEx(IntPtr proc, IntPtr addr, IntPtr size, uint type, uint protect);
        [DllImport("kernel32.dll")] static extern bool VirtualFreeEx(IntPtr proc, IntPtr addr, IntPtr size, uint type);
        [DllImport("kernel32.dll")] static extern bool ReadProcessMemory(IntPtr proc, IntPtr addr, byte[] buf, IntPtr size, out IntPtr read);
        [DllImport("kernel32.dll")] static extern bool WriteProcessMemory(IntPtr proc, IntPtr addr, byte[] buf, IntPtr size, out IntPtr written);

        const int LVM_FIRST = 0x1000, LVM_GETITEMCOUNT = LVM_FIRST + 4, LVM_SETITEMPOSITION = LVM_FIRST + 15,
                  LVM_GETITEMPOSITION = LVM_FIRST + 16, LVM_GETITEMSPACING = LVM_FIRST + 51,
                  LVM_SETEXTENDEDLISTVIEWSTYLE = LVM_FIRST + 54, LVM_GETITEMTEXTW = LVM_FIRST + 115;
        const int LVS_AUTOARRANGE = 0x100, LVS_EX_SNAPTOGRID = 0x80000, GWL_STYLE = -16;

        public class Icon { public string Name; public int X, Y; }

        public static IntPtr FindListView()
        {
            IntPtr def = FindWindowEx(FindWindow("Progman", null), IntPtr.Zero, "SHELLDLL_DefView", null);
            IntPtr w = IntPtr.Zero;
            while (def == IntPtr.Zero && (w = FindWindowEx(IntPtr.Zero, w, "WorkerW", null)) != IntPtr.Zero)
                def = FindWindowEx(w, IntPtr.Zero, "SHELLDLL_DefView", null);
            return def == IntPtr.Zero ? IntPtr.Zero : FindWindowEx(def, IntPtr.Zero, "SysListView32", null);
        }

        public static bool AutoArrange(IntPtr lv) { return (GetWindowLong(lv, GWL_STYLE) & LVS_AUTOARRANGE) != 0; }

        public static Size Spacing(IntPtr lv)
        {
            long r = SendMessage(lv, LVM_GETITEMSPACING, IntPtr.Zero, IntPtr.Zero).ToInt64();
            int cx = (int)(r & 0xFFFF), cy = (int)((r >> 16) & 0xFFFF);
            return new Size(cx > 0 ? cx : 76, cy > 0 ? cy : 86);
        }

        public static List<Icon> Read(IntPtr lv)
        {
            var list = new List<Icon>();
            int n = SendMessage(lv, LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero).ToInt32();
            uint pid; GetWindowThreadProcessId(lv, out pid);
            IntPtr proc = OpenProcess(0x0008 | 0x0010 | 0x0020, false, pid);
            if (proc == IntPtr.Zero) return list;
            IntPtr mem = VirtualAllocEx(proc, IntPtr.Zero, new IntPtr(4096), 0x3000, 0x04);
            try
            {
                var buf = new byte[1024]; IntPtr done;
                for (int i = 0; i < n; i++)
                {
                    SendMessage(lv, LVM_GETITEMPOSITION, new IntPtr(i), mem);
                    ReadProcessMemory(proc, mem, buf, new IntPtr(8), out done);
                    var ic = new Icon { X = BitConverter.ToInt32(buf, 0), Y = BitConverter.ToInt32(buf, 4) };
                    // LVITEMW with pszText pointing at the second half of the block.
                    var item = new byte[64];
                    IntPtr text = new IntPtr(mem.ToInt64() + 1024);
                    int textOff = IntPtr.Size == 8 ? 24 : 20;
                    BitConverter.GetBytes(i).CopyTo(item, 4);
                    if (IntPtr.Size == 8) BitConverter.GetBytes(text.ToInt64()).CopyTo(item, textOff);
                    else BitConverter.GetBytes(text.ToInt32()).CopyTo(item, textOff);
                    BitConverter.GetBytes(260).CopyTo(item, textOff + IntPtr.Size);
                    WriteProcessMemory(proc, mem, item, new IntPtr(item.Length), out done);
                    int len = SendMessage(lv, LVM_GETITEMTEXTW, new IntPtr(i), mem).ToInt32();
                    ReadProcessMemory(proc, text, buf, new IntPtr(Math.Min(1024, Math.Max(0, len) * 2)), out done);
                    ic.Name = System.Text.Encoding.Unicode.GetString(buf, 0, Math.Min(1024, Math.Max(0, len) * 2));
                    list.Add(ic);
                }
            }
            finally
            {
                VirtualFreeEx(proc, mem, IntPtr.Zero, 0x8000);
                CloseHandle(proc);
            }
            return list;
        }

        public static void Move(IntPtr lv, int index, int x, int y)
        {
            SendMessage(lv, LVM_SETITEMPOSITION, new IntPtr(index), new IntPtr((y << 16) | (x & 0xFFFF)));
        }

        // Grid snapping would push packed icons back out from under the rug.
        public static void DisableSnap(IntPtr lv)
        {
            SendMessage(lv, LVM_SETEXTENDEDLISTVIEWSTYLE, new IntPtr(LVS_EX_SNAPTOGRID), IntPtr.Zero);
        }

        public static Point ToClient(IntPtr lv, Point screen) { ScreenToClient(lv, ref screen); return screen; }
    }

    public class RugForm : Form
    {
        // ---- Win32 ----
        [StructLayout(LayoutKind.Sequential)] struct POINT { public int x, y; public POINT(int x, int y) { this.x = x; this.y = y; } }
        [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; public SIZE(int cx, int cy) { this.cx = cx; this.cy = cy; } }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
        [StructLayout(LayoutKind.Sequential)] struct WINDOWPOS { public IntPtr hwnd, hwndInsertAfter; public int x, y, cx, cy; public uint flags; }
        [StructLayout(LayoutKind.Sequential)]
        struct BITMAPINFOHEADER
        {
            public int biSize, biWidth, biHeight; public short biPlanes, biBitCount;
            public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
        }

        [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pprSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hDC);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr hObject);
        [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("user32.dll")] static extern IntPtr FindWindow(string cls, string name);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll", EntryPoint = "SetWindowLong")] static extern int SetWindowLong32(IntPtr hWnd, int idx, int val);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")] static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int idx, IntPtr val);
        [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll")] static extern bool SetProcessWorkingSetSize(IntPtr proc, IntPtr min, IntPtr max);

        const int WS_EX_LAYERED = 0x80000, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
        const int WM_WINDOWPOSCHANGING = 0x46, GWL_HWNDPARENT = -8;
        const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;
        static readonly IntPtr HWND_BOTTOM = new IntPtr(1);

        // ---- State ----
        readonly string stateFile;
        readonly Bitmap source;
        Texture tex;
        Cloth cloth;
        readonly Renderer renderer = new Renderer();
        float scale = 0.6f;
        bool rotated, breeze, catOn = true;
        readonly Dust dust = new Dust();
        readonly Cat cat = new Cat();
        readonly System.Windows.Forms.Timer catTimer = new System.Windows.Forms.Timer();
        bool simAwake, clothDirty = true;
        int lastPad = -1;
        int[] work;
        string iconsFile;

        readonly System.Windows.Forms.Timer tick = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer breezeTimer = new System.Windows.Forms.Timer();
        readonly Random rnd = new Random();
        int calmFrames, restlessFrames;
        float grabOffX, grabOffY, mouseVX, mouseVY;
        Point lastMouse;

        IntPtr memDc, dib, dibBits, oldBmp;
        int dibW, dibH;

        public RugForm(string dir)
        {
            string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WindowsRug");
            Directory.CreateDirectory(appData);
            stateFile = Path.Combine(appData, "state.txt");
            iconsFile = Path.Combine(appData, "icons.txt");

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Text = "Windows Rug";
            Location = new Point(-32000, -32000);

            source = new Bitmap(Path.Combine(dir, "rug.png"));
            string catSheet = Path.Combine(dir, "cat.png");
            if (File.Exists(catSheet)) using (var sheet = new Bitmap(catSheet)) cat.LoadSprites(sheet);
            if (!LoadState()) Rebuild(float.NaN, float.NaN);

            tick.Interval = 15;
            tick.Tick += delegate { Frame(); };
            breezeTimer.Tick += delegate { Gust(); };
            ScheduleBreeze();
            catTimer.Tick += delegate { CatVisit(); };
            ScheduleCat(true);
            BuildMenu();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // Owned by the desktop: survives "Show desktop" (Win+D) and stays
            // above the icons, while WM_WINDOWPOSCHANGING keeps it under apps.
            IntPtr progman = FindWindow("Progman", null);
            if (progman != IntPtr.Zero)
            {
                if (IntPtr.Size == 8) SetWindowLongPtr64(Handle, GWL_HWNDPARENT, progman);
                else SetWindowLong32(Handle, GWL_HWNDPARENT, progman.ToInt32());
            }
            Render();
            SetWindowPos(Handle, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            Wake();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_WINDOWPOSCHANGING)
            {
                var wp = (WINDOWPOS)Marshal.PtrToStructure(m.LParam, typeof(WINDOWPOS));
                if ((wp.flags & SWP_NOZORDER) == 0)
                {
                    wp.hwndInsertAfter = HWND_BOTTOM;
                    Marshal.StructureToPtr(wp, m.LParam, false);
                }
            }
            base.WndProc(ref m);
        }

        // ---- Setup ----
        void Rebuild(float cx, float cy)
        {
            using (var src = (Bitmap)source.Clone())
            {
                if (rotated) src.RotateFlip(RotateFlipType.Rotate90FlipNone);
                int w = Math.Max(80, (int)(src.Width * scale)), h = Math.Max(60, (int)(src.Height * scale));
                using (var scaled = new Bitmap(w, h, PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(scaled))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.DrawImage(src, 0, 0, w, h);
                    }
                    tex = Texture.FromBitmap(scaled);
                }
            }
            var wa = Screen.PrimaryScreen.WorkingArea;
            if (float.IsNaN(cx)) { cx = wa.Left + wa.Width / 2f; cy = wa.Top + wa.Height / 2f; }
            renderer.CamX = wa.Left + wa.Width / 2f;
            renderer.CamY = wa.Top + wa.Height / 2f;
            int nx = Math.Max(8, (int)Math.Round(tex.W / 18f) + 1), ny = Math.Max(6, (int)Math.Round(tex.H / 18f) + 1);
            cloth = new Cloth(nx, ny, cx - tex.W / 2f, cy - tex.H / 2f, tex.W, tex.H);
        }

        // ---- Main loop: runs only while the rug, the cat or the dust moves ----
        void Wake()
        {
            simAwake = true; calmFrames = 0; restlessFrames = 0; cloth.Settling = 0;
            if (!tick.Enabled) tick.Start();
        }

        float RugScale { get { return tex.W / 610f; } }

        void Frame()
        {
            const float dt = 0.016f;
            if (simAwake)
            {
                // Let go: the rug's stiffness lets folds flop back open.
                cloth.Memory = cloth.Grab < 0 && !cloth.Unfolding ? 0.005f : 0;
                float moved = Math.Max(cloth.Step(), cloth.Step());
                clothDirty = true;
                KickUpDust();
                bool free = cloth.Grab < 0 && !cloth.Unfolding && !cat.Hiding;
                if (free && moved < 0.025f) calmFrames++;
                else calmFrames = 0;
                // Watchdog: a rug that keeps fidgeting is damped down and put to sleep,
                // so a tangled heap can never keep the CPU busy.
                restlessFrames = free ? restlessFrames + 1 : 0;
                cloth.Settling = Math.Max(0, Math.Min(0.9f, (restlessFrames - 180) / 150f));
                if (calmFrames > 40 || restlessFrames > 420) SimSleep();
            }
            bool hidingBefore = cat.Hiding;
            cat.Update(dt, cloth);
            if (cat.Hiding || hidingBefore) Wake();     // the lump under the rug needs physics
            dust.Update(dt);
            Render();
            if (!simAwake && !cat.Busy && dust.Count == 0)
            {
                tick.Stop();
                // A napping cat is static; its timer wakes it when the nap is over.
                if (cat.Napping) { catTimer.Interval = Math.Max(1000, (int)(cat.NapLeft * 1000)); catTimer.Enabled = true; }
                GC.Collect();
                try { SetProcessWorkingSetSize(GetCurrentProcess(), new IntPtr(-1), new IntPtr(-1)); } catch { }
            }
        }

        void KickUpDust()
        {
            float s = RugScale;
            float cx = cloth.CenterX, cy = cloth.CenterY;
            if (cloth.Impacts.Count > 0)
            {
                foreach (int i in cloth.Impacts)
                    dust.Emit(cloth.X[i], cloth.Y[i], cloth.X[i] - cx, cloth.Y[i] - cy, 2, s);
                cloth.Impacts.Clear();
            }
            if (cloth.JustLanded)
            {
                cloth.JustLanded = false;
                for (int i = 0; i < cloth.N; i += 2)
                    if (cloth.IsEdge(i)) dust.Emit(cloth.X[i], cloth.Y[i], cloth.X[i] - cx, cloth.Y[i] - cy, 1, s);
            }
        }

        void SimSleep()
        {
            simAwake = false;
            cloth.Settling = 0;
            for (int i = 0; i < cloth.N; i++)
            {
                cloth.PX[i] = cloth.X[i]; cloth.PY[i] = cloth.Y[i]; cloth.PZ[i] = cloth.Z[i];
            }
            SaveState();
        }

        void Render()
        {
            if (!IsHandleCreated) return;
            bool overlay = dust.Count > 0 || cat.Visible;
            renderer.Pad = overlay ? 44 : 16;
            if (clothDirty || renderer.Pad != lastPad)
            {
                renderer.Draw(cloth, tex);
                clothDirty = false; lastPad = renderer.Pad;
            }
            int w = renderer.W, h = renderer.H;
            int[] buf = renderer.Out;
            if (overlay)
            {
                // Cat and dust are drawn over a copy, so the rug itself isn't re-rendered.
                if (work == null || work.Length < w * h) work = new int[renderer.Out.Length];
                Array.Copy(renderer.Out, work, w * h);
                var pin = GCHandle.Alloc(work, GCHandleType.Pinned);
                try
                {
                    using (var bmp = new Bitmap(w, h, w * 4, PixelFormat.Format32bppPArgb, pin.AddrOfPinnedObject()))
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        cat.Draw(g, cloth, renderer.OX, renderer.OY);
                        dust.Draw(g, renderer.OX, renderer.OY);
                    }
                }
                finally { pin.Free(); }
                buf = work;
            }
            IntPtr screenDc = GetDC(IntPtr.Zero);
            if (w != dibW || h != dibH)
            {
                FreeDib();
                memDc = CreateCompatibleDC(screenDc);
                var bi = new BITMAPINFOHEADER { biSize = 40, biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
                dib = CreateDIBSection(memDc, ref bi, 0, out dibBits, IntPtr.Zero, 0);
                oldBmp = SelectObject(memDc, dib);
                dibW = w; dibH = h;
            }
            Marshal.Copy(buf, 0, dibBits, w * h);
            var size = new SIZE(w, h);
            var src = new POINT(0, 0);
            var dst = new POINT(renderer.OX, renderer.OY);
            var blend = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
            UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, 2);
            ReleaseDC(IntPtr.Zero, screenDc);
        }

        // ---- Cat ----
        void ScheduleCat(bool soon)
        {
            catTimer.Interval = soon ? 45000 + rnd.Next(60000) : 180000 + rnd.Next(240000);
            catTimer.Enabled = catOn;
        }

        void CatVisit()
        {
            if (cat.Napping) { cat.EndNap(); ScheduleCat(false); if (!tick.Enabled) tick.Start(); return; }
            ScheduleCat(false);
            if (!catOn || cloth.Grab >= 0 || cat.Present) return;
            cat.Visit(cloth, RugScale);
            Wake();
        }

        // ---- Sweep icons under the rug ----
        void SweepIcons()
        {
            IntPtr lv = DesktopIcons.FindListView();
            if (lv == IntPtr.Zero) { Say("مش لاقي أيقونات سطح المكتب."); return; }
            if (DesktopIcons.AutoArrange(lv))
            {
                Say("اقفل الترتيب التلقائي الأول: كليك يمين على سطح المكتب ← View ← شيل العلامة من Auto arrange icons، وبعدين جرب تاني.");
                return;
            }
            var icons = DesktopIcons.Read(lv);
            if (icons.Count == 0) return;
            if (!File.Exists(iconsFile))
            {
                var lines = new List<string>();
                foreach (var ic in icons) lines.Add(ic.Name + "\t" + ic.X + "\t" + ic.Y);
                try { File.WriteAllLines(iconsFile, lines.ToArray(), System.Text.Encoding.UTF8); } catch { }
            }
            DesktopIcons.DisableSnap(lv);

            // Pack the icons in a grid inside the flat rug, following its rotation.
            var sp = DesktopIcons.Spacing(lv);
            float cx, cy, cos, sin;
            cloth.FitFlat(out cx, out cy, out cos, out sin);
            float uw = cloth.HalfW * 1.6f, vh = cloth.HalfH * 1.5f;
            float cw = sp.Width * 0.8f, ch = sp.Height * 0.8f;
            int cols = Math.Max(1, (int)(uw / cw));
            int rows = (icons.Count + cols - 1) / cols;
            if (rows * ch > vh) ch = vh / rows;
            if (cols > icons.Count) { cols = icons.Count; }
            for (int k = 0; k < icons.Count; k++)
            {
                float u = ((k % cols) - (cols - 1) / 2f) * cw, v = ((k / cols) - (rows - 1) / 2f) * ch;
                var p = new Point((int)(cx + u * cos - v * sin - sp.Width / 2f), (int)(cy + u * sin + v * cos - sp.Height / 2f));
                p = DesktopIcons.ToClient(lv, p);
                DesktopIcons.Move(lv, k, p.X, p.Y);
            }
            // Lift the rug and drop it back over them.
            cloth.BeginUnfold();
            Wake();
        }

        void RestoreIcons()
        {
            IntPtr lv = DesktopIcons.FindListView();
            if (lv == IntPtr.Zero || !File.Exists(iconsFile)) { Say("مفيش أيقونات متكنّسة ترجع."); return; }
            var saved = new Dictionary<string, Point>();
            try
            {
                foreach (var line in File.ReadAllLines(iconsFile, System.Text.Encoding.UTF8))
                {
                    var f = line.Split('\t');
                    if (f.Length == 3 && !saved.ContainsKey(f[0])) saved[f[0]] = new Point(int.Parse(f[1]), int.Parse(f[2]));
                }
            }
            catch { }
            DesktopIcons.DisableSnap(lv);
            var icons = DesktopIcons.Read(lv);
            for (int k = 0; k < icons.Count; k++)
            {
                Point p;
                if (saved.TryGetValue(icons[k].Name, out p)) DesktopIcons.Move(lv, k, p.X, p.Y);
            }
            try { File.Delete(iconsFile); } catch { }
            cloth.BeginUnfold();
            Wake();
        }

        static void Say(string text)
        {
            MessageBox.Show(text, "Windows Rug", MessageBoxButtons.OK, MessageBoxIcon.Information,
                MessageBoxDefaultButton.Button1, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
        }

        void FreeDib()
        {
            if (memDc == IntPtr.Zero) return;
            SelectObject(memDc, oldBmp);
            DeleteObject(dib);
            DeleteDC(memDc);
            memDc = IntPtr.Zero; dibW = dibH = 0;
        }

        // ---- Mouse ----
        int TriangleAt(Point screen)
        {
            int x = screen.X - renderer.OX, y = screen.Y - renderer.OY;
            if (renderer.Hit == null || x < 0 || y < 0 || x >= renderer.W || y >= renderer.H) return -1;
            return renderer.Hit[y * renderer.W + x];
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            Point m = Cursor.Position;
            if (cat.HitTest(m)) { cat.Flee(cloth); Wake(); return; }
            int t = TriangleAt(m);
            if (t >= 0 && cat.Present) cat.Flee(cloth);     // grabbing the rug startles the cat
            if (t >= 0 && e.Clicks >= 2) { cloth.BeginUnfold(); Wake(); return; }
            cloth.CancelUnfold();
            if (t < 0) return;
            // Grab the corner of the touched cell closest to the cursor.
            int cellIdx = t / 2, ci = cellIdx % (cloth.NX - 1), cj = cellIdx / (cloth.NX - 1);
            int best = -1; float bestD = float.MaxValue;
            for (int dj = 0; dj <= 1; dj++)
                for (int di = 0; di <= 1; di++)
                {
                    int k = (cj + dj) * cloth.NX + ci + di;
                    float dx = cloth.X[k] - m.X, dy = cloth.Y[k] - m.Y, d = dx * dx + dy * dy;
                    if (d < bestD) { bestD = d; best = k; }
                }
            cloth.Grab = best;
            grabOffX = cloth.X[best] - m.X; grabOffY = cloth.Y[best] - m.Y;
            cloth.GrabX = cloth.X[best]; cloth.GrabY = cloth.Y[best]; cloth.GrabZ = cloth.Z[best];
            cloth.GrabLift = cloth.Z[best] + Math.Max(14f, Math.Min(tex.W, tex.H) * 0.08f);
            lastMouse = m; mouseVX = mouseVY = 0;
            Capture = true;
            Wake();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Point m = Cursor.Position;
            if (cloth.Grab < 0)
            {
                Cursor = TriangleAt(m) >= 0 ? Cursors.Hand : Cursors.Default;
                return;
            }
            mouseVX = mouseVX * 0.5f + (m.X - lastMouse.X) * 0.5f;
            mouseVY = mouseVY * 0.5f + (m.Y - lastMouse.Y) * 0.5f;
            lastMouse = m;
            cloth.GrabX = m.X + grabOffX; cloth.GrabY = m.Y + grabOffY;
            Wake();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (cloth.Grab < 0) return;
            // Let go with whatever speed the hand had (a small fling).
            int g = cloth.Grab;
            cloth.PX[g] = cloth.X[g] - mouseVX * 0.25f;
            cloth.PY[g] = cloth.Y[g] - mouseVY * 0.25f;
            cloth.PZ[g] = cloth.Z[g];
            cloth.Grab = -1;
            Capture = false;
            Wake();
        }

        // ---- Menu ----
        void BuildMenu()
        {
            var menu = new ContextMenuStrip { RightToLeft = RightToLeft.Yes };
            menu.Items.Add("كنّس الأيقونات تحت السجادة", null, delegate { SweepIcons(); });
            menu.Items.Add("رجّع الأيقونات مكانها", null, delegate { RestoreIcons(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("افرد السجادة", null, delegate { cloth.BeginUnfold(); Wake(); });
            menu.Items.Add("كبّر", null, delegate { ScaleRug(1.15f); });
            menu.Items.Add("صغّر", null, delegate { ScaleRug(1 / 1.15f); });
            menu.Items.Add("لف السجادة", null, delegate { rotated = !rotated; Rebuild(cloth.CenterX, cloth.CenterY); Wake(); });
            var breezeItem = new ToolStripMenuItem("هوا خفيف") { CheckOnClick = true, Checked = breeze };
            breezeItem.CheckedChanged += delegate { breeze = breezeItem.Checked; ScheduleBreeze(); SaveState(); };
            menu.Items.Add(breezeItem);
            var catItem = new ToolStripMenuItem("قطة") { CheckOnClick = true, Checked = catOn };
            catItem.CheckedChanged += delegate
            {
                catOn = catItem.Checked;
                if (catOn) { ScheduleCat(true); } else { catTimer.Enabled = false; cat.Flee(cloth); Wake(); }
                SaveState();
            };
            menu.Items.Add(catItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("اقفل السجادة", null, delegate { SaveState(); Close(); });
            ContextMenuStrip = menu;
        }

        void ScaleRug(float factor)
        {
            scale = Math.Max(0.2f, Math.Min(2.5f, scale * factor));
            Rebuild(cloth.CenterX, cloth.CenterY);
            Wake();
        }

        void ScheduleBreeze() { breezeTimer.Interval = 15000 + rnd.Next(30000); breezeTimer.Enabled = breeze; }

        void Gust()
        {
            ScheduleBreeze();
            if (cloth.Grab >= 0 || cloth.Unfolding) return;
            cloth.Gust(rnd);
            Wake();
        }

        // ---- Persistence: the rug comes back exactly as you left it ----
        bool LoadState()
        {
            try
            {
                if (!File.Exists(stateFile)) return false;
                var lines = File.ReadAllLines(stateFile);
                var inv = CultureInfo.InvariantCulture;
                int nx = 0, ny = 0, p = 0;
                for (; p < lines.Length; p++)
                {
                    var kv = lines[p].Split('=');
                    if (kv.Length != 2) break;
                    if (kv[0] == "scale") scale = float.Parse(kv[1], inv);
                    else if (kv[0] == "rotated") rotated = kv[1] == "1";
                    else if (kv[0] == "breeze") breeze = kv[1] == "1";
                    else if (kv[0] == "cat") catOn = kv[1] == "1";
                    else if (kv[0] == "grid") { var g = kv[1].Split('x'); nx = int.Parse(g[0]); ny = int.Parse(g[1]); }
                }
                Rebuild(float.NaN, float.NaN);
                if (nx != cloth.NX || ny != cloth.NY || lines.Length - p < cloth.N) return true;
                for (int i = 0; i < cloth.N; i++)
                {
                    var v = lines[p + i].Split(' ');
                    cloth.X[i] = cloth.PX[i] = float.Parse(v[0], inv);
                    cloth.Y[i] = cloth.PY[i] = float.Parse(v[1], inv);
                    cloth.Z[i] = cloth.PZ[i] = float.Parse(v[2], inv);
                }
                if (!SystemInformation.VirtualScreen.Contains((int)cloth.CenterX, (int)cloth.CenterY))
                    Rebuild(float.NaN, float.NaN);
                return true;
            }
            catch { return false; }
        }

        void SaveState()
        {
            try
            {
                var inv = CultureInfo.InvariantCulture;
                var lines = new List<string> {
                    "scale=" + scale.ToString(inv), "rotated=" + (rotated ? "1" : "0"),
                    "breeze=" + (breeze ? "1" : "0"), "cat=" + (catOn ? "1" : "0"), "grid=" + cloth.NX + "x" + cloth.NY,
                };
                for (int i = 0; i < cloth.N; i++)
                    lines.Add(cloth.X[i].ToString("0.##", inv) + " " + cloth.Y[i].ToString("0.##", inv) + " " + cloth.Z[i].ToString("0.##", inv));
                File.WriteAllLines(stateFile, lines.ToArray());
            }
            catch { }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            tick.Stop();
            breezeTimer.Stop();
            catTimer.Stop();
            FreeDib();
            base.OnFormClosed(e);
        }
    }
}
