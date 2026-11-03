using System;

namespace OpaGachi
{
    // Resamples a premultiplied BGRA image into a rectangle of a destination surface (clipped).
    // Bilinear; when shrinking below 60% each output pixel averages a 2x2 grid of bilinear taps so
    // small sizes don't shimmer. Lookup tables are rebuilt only when the geometry changes.
    sealed class Scaler
    {
        int lastSw, lastSh, lastW, lastH, taps;
        int[] xi, xf, yi, yf;   // per output column/row and tap: source index and 8-bit weight

        public unsafe void Draw(byte* src, int sw, int sh, byte* dst, int dw, int dh, int x0, int y0, int w, int h, bool over)
        {
            if (w <= 0 || h <= 0) return;
            if (sw != lastSw || sh != lastSh || w != lastW || h != lastH) Build(sw, sh, w, h);
            int n = taps, n2 = n * n;
            int colStart = Math.Max(0, -x0), colEnd = Math.Min(w, dw - x0);
            int rowStart = Math.Max(0, -y0), rowEnd = Math.Min(h, dh - y0);
            int stride = sw * 4;
            for (int r = rowStart; r < rowEnd; r++)
            {
                byte* outRow = dst + ((y0 + r) * dw + x0) * 4;
                for (int c = colStart; c < colEnd; c++)
                {
                    int b = 0, g = 0, rr = 0, a = 0;
                    for (int ty = 0; ty < n; ty++)
                    {
                        int yo = (r * n + ty);
                        int sy = yi[yo], fy = yf[yo];
                        byte* row0 = src + sy * stride;
                        byte* row1 = sy + 1 < sh ? row0 + stride : row0;
                        for (int tx = 0; tx < n; tx++)
                        {
                            int xo = (c * n + tx);
                            int sx = xi[xo], fx = xf[xo];
                            byte* p00 = row0 + sx * 4;
                            byte* p10 = row1 + sx * 4;
                            int step = sx + 1 < sw ? 4 : 0;
                            if ((p00[3] | p00[step + 3] | p10[3] | p10[step + 3]) == 0) continue;   // empty space around the figure
                            int w00 = (256 - fx) * (256 - fy), w01 = fx * (256 - fy), w10 = (256 - fx) * fy, w11 = fx * fy;
                            b += p00[0] * w00 + p00[step] * w01 + p10[0] * w10 + p10[step] * w11;
                            g += p00[1] * w00 + p00[step + 1] * w01 + p10[1] * w10 + p10[step + 1] * w11;
                            rr += p00[2] * w00 + p00[step + 2] * w01 + p10[2] * w10 + p10[step + 2] * w11;
                            a += p00[3] * w00 + p00[step + 3] * w01 + p10[3] * w10 + p10[step + 3] * w11;
                        }
                    }
                    int shift = 16;  // weights sum to 65536 per tap
                    byte* o = outRow + c * 4;
                    int ob = (b >> shift) / n2, og = (g >> shift) / n2, or = (rr >> shift) / n2, oa = (a >> shift) / n2;
                    if (oa == 0 && ob == 0) continue;   // fully transparent: the surface is already clear (or unchanged for "over")
                    if (over)
                    {
                        int inv = 255 - oa;
                        o[0] = (byte)(ob + o[0] * inv / 255);
                        o[1] = (byte)(og + o[1] * inv / 255);
                        o[2] = (byte)(or + o[2] * inv / 255);
                        o[3] = (byte)(oa + o[3] * inv / 255);
                    }
                    else
                    {
                        o[0] = (byte)ob; o[1] = (byte)og; o[2] = (byte)or; o[3] = (byte)oa;
                    }
                }
            }
        }

        void Build(int sw, int sh, int w, int h)
        {
            lastSw = sw; lastSh = sh; lastW = w; lastH = h;
            taps = (w < sw * 0.6 || h < sh * 0.6) ? 2 : 1;
            Axis(sw, w, out xi, out xf);
            Axis(sh, h, out yi, out yf);
        }

        void Axis(int srcLen, int dstLen, out int[] index, out int[] weight)
        {
            index = new int[dstLen * taps];
            weight = new int[dstLen * taps];
            double ratio = srcLen / (double)dstLen;
            for (int i = 0; i < dstLen; i++)
            {
                for (int t = 0; t < taps; t++)
                {
                    double u = (i + (t + 0.5) / taps) * ratio - 0.5;
                    if (u < 0) u = 0;
                    if (u > srcLen - 1) u = srcLen - 1;
                    int i0 = (int)u;
                    index[i * taps + t] = i0;
                    weight[i * taps + t] = (int)((u - i0) * 256);
                }
            }
        }
    }
}
