using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;

namespace OpaGachi
{
    // One .opaclip file (written by tools/build_assets.py). The compressed bytes stay in memory;
    // frames are inflated one at a time by a ClipPlayer, so a playing clip costs ~2 MB, not ~20 MB.
    sealed class SpriteClip
    {
        public readonly string Path;
        public readonly int Width, Height, Fps, FrameCount;
        public readonly float TieValueRef;
        readonly byte[] data;
        readonly int[] offsets;
        readonly int dataStart;

        public bool Standing;   // full-body standing pose (poses.json); drawn larger by the standing boost
        public int[] RestFrames = new int[0];   // frames back in the starting pose (rest.json): safe hand-over points

        // Did playback pass a rest frame going from frame `after` (exclusive) to `upTo` (inclusive)?
        public bool RestBetween(int after, int upTo)
        {
            foreach (int r in RestFrames) if (r > after && r <= upTo) return true;
            return false;
        }

        public int PixelBytes { get { return Width * Height * 4; } }
        public double Duration { get { return FrameCount / (double)Fps; } }

        SpriteClip(string path, byte[] bytes)
        {
            Path = path;
            data = bytes;
            if (bytes.Length < 20 || bytes[0] != 'O' || bytes[1] != 'P' || bytes[2] != 'A' || bytes[3] != 'C')
                throw new InvalidDataException("Not an .opaclip file: " + path);
            int version = BitConverter.ToUInt16(bytes, 4);
            if (version != 1) throw new InvalidDataException("Unsupported .opaclip version " + version + ": " + path);
            Width = BitConverter.ToUInt16(bytes, 6);
            Height = BitConverter.ToUInt16(bytes, 8);
            Fps = BitConverter.ToUInt16(bytes, 10);
            FrameCount = (int)BitConverter.ToUInt32(bytes, 12);
            TieValueRef = BitConverter.ToSingle(bytes, 16);
            offsets = new int[FrameCount + 1];
            for (int i = 0; i <= FrameCount; i++) offsets[i] = (int)BitConverter.ToUInt32(bytes, 20 + 4 * i);
            dataStart = 20 + 4 * (FrameCount + 1);
        }

        public static SpriteClip Load(string path)
        {
            return new SpriteClip(path, File.ReadAllBytes(path));
        }

        public Stream OpenFrame(int index)
        {
            var ms = new MemoryStream(data, dataStart + offsets[index], offsets[index + 1] - offsets[index], false);
            return new DeflateStream(ms, CompressionMode.Decompress);
        }
    }

    // Per-pet decoding state for the clip it is currently playing.
    // Frames are XOR deltas of the previous frame, so decoding always moves forward from frame 0.
    sealed class ClipPlayer
    {
        public SpriteClip Clip { get; private set; }
        public int Decoded { get; private set; }
        public byte[] Frame { get; private set; }      // premultiplied BGRA of frame `Decoded`
        public byte[] Ties { get; private set; }       // tie entries for frame `Decoded`: (u32 index, u8 weight, u8 value)
        public int TieCount { get; private set; }

        byte[] scratch = new byte[0];
        readonly byte[] count = new byte[4];

        public ClipPlayer()
        {
            Frame = new byte[0];
            Ties = new byte[0];
            Decoded = -1;
        }

        public void Start(SpriteClip clip)
        {
            Clip = clip;
            if (Frame.Length != clip.PixelBytes)
            {
                Frame = new byte[clip.PixelBytes];
                scratch = new byte[clip.PixelBytes];
            }
            Decoded = -1;
        }

        // Decodes forward to `target` (clamped to the clip). Returns true if the frame changed.
        public bool SeekTo(int target)
        {
            if (Clip == null) return false;
            if (target >= Clip.FrameCount) target = Clip.FrameCount - 1;
            if (target == Decoded) return false;
            if (target < Decoded) Decoded = -1;
            if (Decoded < 0) Array.Clear(Frame, 0, Frame.Length);
            while (Decoded < target) DecodeNext();
            return true;
        }

        void DecodeNext()
        {
            int index = Decoded + 1;
            using (var s = Clip.OpenFrame(index))
            {
                ReadExactly(s, scratch, scratch.Length);
                Xor(Frame, scratch);
                ReadExactly(s, count, 4);
                TieCount = (int)BitConverter.ToUInt32(count, 0);
                int tieBytes = TieCount * 6;
                if (Ties.Length < tieBytes) Ties = new byte[tieBytes];
                ReadExactly(s, Ties, tieBytes);
            }
            Decoded = index;
        }

        static void ReadExactly(Stream s, byte[] buffer, int length)
        {
            int read = 0;
            while (read < length)
            {
                int n = s.Read(buffer, read, length - read);
                if (n <= 0) throw new EndOfStreamException("Truncated .opaclip frame");
                read += n;
            }
        }

        static unsafe void Xor(byte[] dst, byte[] src)
        {
            fixed (byte* d0 = dst, s0 = src)
            {
                ulong* d = (ulong*)d0, s = (ulong*)s0;
                int words = dst.Length / 8;
                for (int i = 0; i < words; i++) d[i] ^= s[i];
                for (int i = words * 8; i < dst.Length; i++) d0[i] ^= s0[i];
            }
        }
    }

    // Recolours the template's blue tie with the agent's colour or a tiled pattern. The tie pixel's
    // brightness (relative to the clip's typical tie brightness) shades the result, so folds survive.
    sealed class TieTint
    {

        public readonly Color Color;
        readonly byte[] tile;        // straight BGRA pattern tile, or null for a solid colour
        readonly int tileW, tileH;
        readonly float[] shadeMul = new float[256], shadeLift = new float[256];
        float shadeRef = -1;

        public TieTint(Color color, string texturePath, int tileHeight)
        {
            Color = color;
            if (texturePath != null) tile = LoadTile(texturePath, tileHeight, out tileW, out tileH);   // patterns shrink so a few repeats fit on the tie
        }

        public void Apply(IntPtr dst, ClipPlayer player)
        {
            if (player.TieCount == 0) return;
            float vRef = player.Clip.TieValueRef;
            if (vRef != shadeRef) BuildShade(vRef);
            int width = player.Clip.Width;
            int solidB = Color.B, solidG = Color.G, solidR = Color.R;
            unsafe
            {
                byte* px = (byte*)dst;
                byte[] ties = player.Ties;
                for (int t = 0; t < player.TieCount; t++)
                {
                    int o = t * 6;
                    int index = ties[o] | (ties[o + 1] << 8) | (ties[o + 2] << 16) | (ties[o + 3] << 24);
                    int w = ties[o + 4], v = ties[o + 5];
                    byte* p = px + index * 4;
                    int a = p[3];
                    int b0 = solidB, g0 = solidG, r0 = solidR;
                    if (tile != null)
                    {
                        int ti = ((index / width % tileH) * tileW + index % width % tileW) * 4;
                        int ta = tile[ti + 3];
                        b0 = (solidB * (255 - ta) + tile[ti] * ta) / 255;
                        g0 = (solidG * (255 - ta) + tile[ti + 1] * ta) / 255;
                        r0 = (solidR * (255 - ta) + tile[ti + 2] * ta) / 255;
                    }
                    float mul = shadeMul[v], lift = shadeLift[v];
                    p[0] = Mix(p[0], b0, mul, lift, a, w);
                    p[1] = Mix(p[1], g0, mul, lift, a, w);
                    p[2] = Mix(p[2], r0, mul, lift, a, w);
                }
            }
        }

        static byte Mix(byte original, int baseColor, float mul, float lift, int alpha, int weight)
        {
            float shaded = baseColor * mul + (255 - baseColor) * lift;
            int target = Math.Min(255, (int)(shaded + 0.5f)) * alpha / 255;   // premultiply
            return (byte)((original * (255 - weight) + target * weight) / 255);
        }

        void BuildShade(float vRef)
        {
            shadeRef = vRef;
            if (vRef < 0.05f) vRef = 0.05f;
            float headroom = Math.Max(1f / vRef - 1f, 0.001f);
            for (int v = 0; v < 256; v++)
            {
                float f = v / 255f / vRef;
                shadeMul[v] = Math.Min(f, 1f);
                shadeLift[v] = f <= 1f ? 0f : Math.Min(1f, (f - 1f) / headroom) * 0.3f;   // soft highlights
            }
        }

        static byte[] LoadTile(string path, int tileHeight, out int w, out int h)
        {
            w = h = 0;
            try
            {
                using (var src = new Bitmap(path))
                {
                    h = Math.Max(6, tileHeight);
                    w = Math.Max(1, (int)Math.Round(src.Width * (double)h / src.Height));
                    using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
                    {
                        using (var g = Graphics.FromImage(bmp))
                        {
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                            g.DrawImage(src, new Rectangle(0, 0, w, h));
                        }
                        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                        var bytes = new byte[w * h * 4];
                        for (int row = 0; row < h; row++)
                            System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + row * data.Stride, bytes, row * w * 4, w * 4);
                        bmp.UnlockBits(data);
                        return bytes;
                    }
                }
            }
            catch (Exception e)
            {
                Log.Write("tie texture " + path + ": " + e.Message);
                w = h = 0;
                return null;
            }
        }
    }
}
