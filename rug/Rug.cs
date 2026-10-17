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
        readonly bool[] supported;
        readonly int[] ca, cb;
        readonly float[] cr, ck;
        public readonly float Thickness, Spacing;

        public int Grab = -1;
        public float GrabX, GrabY, GrabZ, GrabLift;

        const float Gravity = 0.32f;
        const int Iterations = 10;

        public Cloth(int nx, int ny, float left, float top, float w, float h)
        {
            NX = nx; NY = ny; N = nx * ny;
            X = new float[N]; Y = new float[N]; Z = new float[N];
            PX = new float[N]; PY = new float[N]; PZ = new float[N];
            supported = new bool[N];
            float dx = w / (nx - 1), dy = h / (ny - 1);
            Spacing = Math.Min(dx, dy);
            Thickness = Math.Max(1.5f, Spacing * 0.14f);
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                {
                    int k = j * nx + i;
                    X[k] = PX[k] = left + i * dx;
                    Y[k] = PY[k] = top + j * dy;
                    supported[k] = true;
                }

            var a = new List<int>(); var b = new List<int>(); var s = new List<float>();
            // Structural, shear and bend springs. Position-based stiffness compounds over
            // the solver iterations, so the small bend values still give a soft drape.
            int[,] links = { { 1, 0 }, { 0, 1 }, { 1, 1 }, { 1, -1 }, { 2, 0 }, { 0, 2 } };
            float[] stiff = { 1f, 1f, 0.02f, 0.02f, 0.01f, 0.01f };
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                    for (int l = 0; l < 6; l++)
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

        // One simulation step. Returns the largest distance any point moved.
        public float Step()
        {
            for (int i = 0; i < N; i++)
            {
                float vx = X[i] - PX[i], vy = Y[i] - PY[i], vz = Z[i] - PZ[i];
                if (supported[i])
                {
                    // Friction against the desk (or the rug underneath).
                    vx *= 0.55f; vy *= 0.55f;
                    if (vx * vx + vy * vy < 0.0009f) { vx = 0; vy = 0; }
                }
                else { vx *= 0.993f; vy *= 0.993f; }
                vz *= 0.985f;
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
                if (!supported[i] || i == Grab) continue;
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
                if (final) supported[i] = false;
                if (Z[i] <= 0)
                {
                    Z[i] = 0;
                    if (final) { supported[i] = true; PZ[i] = 0; }
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
            int gw = (int)((maxX - minX) / cell) + 1, gh = (int)((maxY - minY) / cell) + 1;
            int cells = gw * gh;
            if (cellStart == null || cellStart.Length < cells + 1) { cellStart = new int[cells + 1]; cellFill = new int[cells + 1]; }
            else { Array.Clear(cellStart, 0, cells + 1); Array.Clear(cellFill, 0, cells + 1); }
            if (cellItems == null) { cellItems = new int[N]; cellOf = new int[N]; }
            for (int i = 0; i < N; i++)
            {
                int c = (int)((Y[i] - minY) / cell) * gw + (int)((X[i] - minX) / cell);
                cellOf[i] = c;
                cellStart[c + 1]++;
            }
            for (int c = 0; c < cells; c++) cellStart[c + 1] += cellStart[c];
            for (int i = 0; i < N; i++) { int c = cellOf[i]; cellItems[cellStart[c] + cellFill[c]++] = i; }

            float r2 = Spacing * 0.8f * Spacing * 0.8f;
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
                            Z[up] = Z[down] + Thickness;
                            if (PZ[up] < Z[up] - 0.5f) PZ[up] = Z[up];
                            supported[up] = true;
                        }
                    }
                }
            }
        }

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
        float[] shadow, tmp, casterZ;
        int capacity;

        // Light from the top-left; shadows fall bottom-right.
        const float SX = 0.45f, SY = 0.65f;
        const float CamH = 2200f;
        const float Ambient = 0.5f, Diffuse = 0.62f;
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

            const int pad = 16;
            OX = (int)Math.Floor(minX) - pad; OY = (int)Math.Floor(minY) - pad;
            W = ((int)Math.Ceiling(maxX) + pad - OX + 15) & ~15;
            H = ((int)Math.Ceiling(maxY) + pad - OY + 15) & ~15;
            int size = W * H;
            SW = W / 2; SH = H / 2;
            int ssize = SW * SH;
            if (size > capacity)
            {
                capacity = size + size / 4;
                Out = new int[capacity]; Hit = new int[capacity];
                shadow = new float[capacity / 4 + 1]; tmp = new float[capacity / 4 + 1]; casterZ = new float[capacity / 4 + 1];
            }
            Array.Clear(shadow, 0, ssize);
            for (int i = 0; i < ssize; i++) casterZ[i] = -1;
            for (int i = 0; i < size; i++) Hit[i] = -1;

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
                        float z = w0 * z0 + w1 * z1 + w2 * z2;
                        int sxp = (int)(x + 0.5f + z * SX) >> 1, syp = (int)(py + z * SY) >> 1;
                        if (sxp < SW && syp < SH)
                        {
                            float over = casterZ[syp * SW + sxp] - z - selfGap;
                            if (over > 0) light *= 1 - 0.38f * Math.Min(1, over / 12f + 0.35f);
                        }
                    }

                    sr *= light; sg *= light; sb *= light;
                    if (sr > sa) sr = sa; if (sg > sa) sg = sa; if (sb > sa) sb = sa;

                    int idx = row + x;
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
        bool rotated, breeze;

        readonly System.Windows.Forms.Timer tick = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer breezeTimer = new System.Windows.Forms.Timer();
        readonly Random rnd = new Random();
        int calmFrames;
        float grabOffX, grabOffY, mouseVX, mouseVY;
        Point lastMouse;

        IntPtr memDc, dib, dibBits, oldBmp;
        int dibW, dibH;

        public RugForm(string dir)
        {
            string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WindowsRug");
            Directory.CreateDirectory(appData);
            stateFile = Path.Combine(appData, "state.txt");

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Text = "Windows Rug";
            Location = new Point(-32000, -32000);

            source = new Bitmap(Path.Combine(dir, "rug.png"));
            if (!LoadState()) Rebuild(float.NaN, float.NaN);

            tick.Interval = 15;
            tick.Tick += delegate { Frame(); };
            breezeTimer.Tick += delegate { Gust(); };
            ScheduleBreeze();
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

        // ---- Simulation loop (runs only while something moves) ----
        void Wake() { calmFrames = 0; if (!tick.Enabled) tick.Start(); }

        void Frame()
        {
            float moved = Math.Max(cloth.Step(), cloth.Step());
            Render();
            if (cloth.Grab < 0 && moved < 0.025f) calmFrames++;
            else calmFrames = 0;
            if (calmFrames > 40) Sleep();
        }

        void Sleep()
        {
            tick.Stop();
            for (int i = 0; i < cloth.N; i++)
            {
                cloth.PX[i] = cloth.X[i]; cloth.PY[i] = cloth.Y[i]; cloth.PZ[i] = cloth.Z[i];
            }
            SaveState();
            GC.Collect();
            try { SetProcessWorkingSetSize(GetCurrentProcess(), new IntPtr(-1), new IntPtr(-1)); } catch { }
        }

        void Render()
        {
            if (!IsHandleCreated) return;
            renderer.Draw(cloth, tex);
            int w = renderer.W, h = renderer.H;
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
            Marshal.Copy(renderer.Out, 0, dibBits, w * h);
            var size = new SIZE(w, h);
            var src = new POINT(0, 0);
            var dst = new POINT(renderer.OX, renderer.OY);
            var blend = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
            UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, 2);
            ReleaseDC(IntPtr.Zero, screenDc);
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
            int t = TriangleAt(m);
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
            menu.Items.Add("افرد السجادة", null, delegate { Rebuild(cloth.CenterX, cloth.CenterY); Wake(); });
            menu.Items.Add("كبّر", null, delegate { ScaleRug(1.15f); });
            menu.Items.Add("صغّر", null, delegate { ScaleRug(1 / 1.15f); });
            menu.Items.Add("لف السجادة", null, delegate { rotated = !rotated; Rebuild(cloth.CenterX, cloth.CenterY); Wake(); });
            var breezeItem = new ToolStripMenuItem("هوا خفيف") { CheckOnClick = true, Checked = breeze };
            breezeItem.CheckedChanged += delegate { breeze = breezeItem.Checked; ScheduleBreeze(); SaveState(); };
            menu.Items.Add(breezeItem);
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
            if (cloth.Grab >= 0) return;
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
                    "breeze=" + (breeze ? "1" : "0"), "grid=" + cloth.NX + "x" + cloth.NY,
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
            FreeDib();
            base.OnFormClosed(e);
        }
    }
}
