using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;

namespace OpaGachi
{
    // Experimental custom sprite (user, 2026-11-01): a .png or a .webm with alpha becomes the one generic sprite
    // every agent uses, saved as an ordinary clip (<data>/sprites/custom/<action>/ACTION_N.opaclip). A .webm is decoded
    // by Microsoft Edge (it ships with Windows) running hidden on a temporary local page: each frame is drawn to
    // a canvas and printed to Edge's log, so nothing extra is installed and the file never leaves the PC.
    static class CustomSprite
    {
        // Absolute, in the user's data folder: Path.Combine(spritesRoot, Folder) then lands here, so updates that
        // replace assets/sprites keep the user's clips.
        public static string Folder { get { return Path.Combine(Path.Combine(Program.Data, "sprites"), "custom"); } }
        const int Fps = 24, MaxFrames = 240, StillFrames = 48;   // up to 10 s of video; a still loops every 2 s

        public static string ClipPath(string spritesRoot)
        {
            return Path.Combine(Path.Combine(Path.Combine(spritesRoot, Folder), "idle"), "custom.opaclip");
        }

        // Returns the number of frames saved.
        public static int Import(string file, string spritesRoot, int width, int height)
        {
            return ImportSlot(file, spritesRoot, width, height, Anim.Idle);
        }

        public static int ImportSlot(string file, string spritesRoot, int width, int height, Anim action)
        {
            string stem = Path.GetFileNameWithoutExtension(file);
            string prefix = AnimSpec.Of(action).Folder.ToUpperInvariant() + "_";
            int slot;
            if (!stem.StartsWith(prefix, StringComparison.Ordinal) || !int.TryParse(stem.Substring(prefix.Length), out slot) || slot < 1 || slot > 5 || stem != prefix + slot)
                throw new InvalidDataException("Name this file " + prefix + "1 through " + prefix + "5 (uppercase). Each action allows five files.");
            string dir = Path.Combine(spritesRoot, Folder);
            string format = Path.Combine(dir, "format.txt");
            string extension = Path.GetExtension(file).ToLowerInvariant();
            if (File.Exists(ClipPath(spritesRoot))) throw new InvalidDataException("Clear the old generic custom sprite first, then add named action slots.");
            if (File.Exists(format) && File.ReadAllText(format) != extension)
                throw new InvalidDataException("All custom actions must use the same format. Clear all custom sprites before switching PNG/WebM.");
            string ext = Path.GetExtension(file).ToLowerInvariant();
            List<byte[]> frames;
            if (ext == ".webm") frames = FromWebm(file, width, height);
            else if (ext == ".png") { byte[] still; using (var img = Image.FromFile(file)) still = Fit(img, width, height); frames = Enumerable.Repeat(still, StillFrames).ToList(); }
            else throw new InvalidDataException("Choose a .png or a .webm file.");
            if (ext == ".webm" && !frames.Any(f => Enumerable.Range(0, f.Length / 4).Any(i => f[i * 4 + 3] < 255)))
                throw new InvalidDataException("The WebM must contain a transparent alpha background.");
            if (frames.Count == 0) throw new InvalidDataException("No frames could be read from that file.");
            string path = Path.Combine(Path.Combine(dir, AnimSpec.Of(action).Folder), stem + ".opaclip");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string pending = path + ".pending";
            Write(pending, frames, width, height);
            if (File.Exists(path)) File.Replace(pending, path, null); else File.Move(pending, path);
            File.WriteAllText(format, ext);
            return frames.Count;
        }

        // Scaled to fit, standing on the bottom edge, centred; premultiplied BGRA.
        static byte[] Fit(Image src, int w, int h)
        {
            using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    float s = Math.Min(w / (float)src.Width, h / (float)src.Height), dw = src.Width * s, dh = src.Height * s;
                    g.DrawImage(src, (w - dw) / 2, h - dh, dw, dh);
                }
                var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
                var bytes = new byte[w * h * 4];
                for (int row = 0; row < h; row++) System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + row * data.Stride, bytes, row * w * 4, w * 4);
                bmp.UnlockBits(data);
                return bytes;
            }
        }

        // .opaclip v1 (see SpriteClip): header, frame offsets, then each frame deflated as the XOR of it and the
        // previous frame, followed by a zero count of tie entries (custom sprites have no tie to recolour).
        static void Write(string path, List<byte[]> frames, int w, int h)
        {
            var chunks = new List<byte[]>();
            var prev = new byte[w * h * 4];
            foreach (var f in frames)
            {
                var delta = new byte[f.Length];
                for (int i = 0; i < f.Length; i++) delta[i] = (byte)(f[i] ^ prev[i]);
                prev = f;
                using (var ms = new MemoryStream())
                {
                    using (var z = new DeflateStream(ms, CompressionLevel.Optimal, true)) { z.Write(delta, 0, delta.Length); z.Write(new byte[4], 0, 4); }
                    chunks.Add(ms.ToArray());
                }
            }
            using (var bw = new BinaryWriter(File.Create(path)))
            {
                bw.Write(Encoding.ASCII.GetBytes("OPAC"));
                bw.Write((ushort)1); bw.Write((ushort)w); bw.Write((ushort)h); bw.Write((ushort)Fps);
                bw.Write((uint)frames.Count);
                bw.Write(1f);   // tie brightness reference: unused without a tie
                uint offset = 0;
                bw.Write(offset);
                foreach (var c in chunks) { offset += (uint)c.Length; bw.Write(offset); }
                foreach (var c in chunks) bw.Write(c);
            }
        }

        // ---- .webm through a hidden Edge ------------------------------------------------------------

        const string Page = @"<!doctype html><html><body><script>
var v=document.createElement('video');v.muted=true;v.preload='auto';
var c=document.createElement('canvas');c.width=@W;c.height=@H;var g=c.getContext('2d');
var fps=@FPS,i=0,n=0,started=false,hasAlpha=false;var a=document.createElement('canvas'),ag=a.getContext('2d');
function begin(){if(started)return;started=true;if(v.duration>10.05){console.log('ERR duration-over-10s');return;}n=Math.min(@MAX,Math.max(1,Math.round(v.duration*fps)));step();}
function step(){if(i>=n){console.log(hasAlpha?'END '+n:'ERR alpha-required');return;}v.currentTime=Math.min(v.duration-0.001,(i+0.5)/fps);}
v.addEventListener('loadeddata',function(){if(isFinite(v.duration))begin();else v.currentTime=1e7;});
v.addEventListener('durationchange',function(){if(!started&&isFinite(v.duration))begin();});
v.addEventListener('seeked',function(){if(!started)return;if(!hasAlpha){a.width=v.videoWidth;a.height=v.videoHeight;ag.drawImage(v,0,0);var pixels=ag.getImageData(0,0,a.width,a.height).data;for(var j=3;j<pixels.length;j+=4)if(pixels[j]<255){hasAlpha=true;break;}}g.clearRect(0,0,@W,@H);var s=Math.min(@W/v.videoWidth,@H/v.videoHeight),w=v.videoWidth*s,h=v.videoHeight*s;g.drawImage(v,(@W-w)/2,@H-h,w,h);var d=c.toDataURL('image/png');for(var k=0;k<d.length;k+=4000)console.log('F'+i+' '+d.substr(k,4000));i++;step();});
v.addEventListener('error',function(){console.log('ERR '+(v.error?v.error.code:0));});
v.src='data:video/webm;base64,@DATA';
</script></body></html>";

        static List<byte[]> FromWebm(string file, int w, int h)
        {
            if (new FileInfo(file).Length > 40L * 1024 * 1024) throw new InvalidDataException("That video is over 40 MB.");
            string edge = FindEdge();
            if (edge == null) throw new FileNotFoundException("Reading a .webm needs Microsoft Edge, which wasn't found.");
            string work = Path.Combine(Path.GetTempPath(), "opagachi-webm-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                string page = Path.Combine(work, "decode.html");
                File.WriteAllText(page, Page.Replace("@W", w.ToString()).Replace("@H", h.ToString()).Replace("@FPS", Fps.ToString())
                    .Replace("@MAX", MaxFrames.ToString()).Replace("@DATA", Convert.ToBase64String(File.ReadAllBytes(file))));
                var parts = new SortedDictionary<int, StringBuilder>();
                string end = null;
                var done = new ManualResetEvent(false);
                var psi = new ProcessStartInfo(edge, "--headless --disable-gpu --no-first-run --disable-extensions --mute-audio --enable-logging=stderr --v=0"
                    + " --user-data-dir=\"" + Path.Combine(work, "profile") + "\" \"" + new Uri(page).AbsoluteUri + "\"")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
                using (var p = new Process { StartInfo = psi })
                {
                    p.ErrorDataReceived += (s, e) =>
                    {
                        string msg = ConsoleMessage(e.Data);
                        if (msg == null) return;
                        lock (parts)
                        {
                            if (msg.StartsWith("END") || msg.StartsWith("ERR")) { end = msg; done.Set(); return; }
                            int space = msg.IndexOf(' '), index;
                            if (msg[0] != 'F' || space < 2 || !int.TryParse(msg.Substring(1, space - 1), out index)) return;
                            StringBuilder sb;
                            if (!parts.TryGetValue(index, out sb)) parts[index] = sb = new StringBuilder();
                            sb.Append(msg, space + 1, msg.Length - space - 1);
                        }
                    };
                    p.OutputDataReceived += (s, e) => { };
                    p.Start();
                    p.BeginErrorReadLine();
                    p.BeginOutputReadLine();
                    bool finished = done.WaitOne(60000);
                    KillTree(p);
                    if (!finished) throw new TimeoutException("Edge took too long to read the video.");
                }
                if (end.StartsWith("ERR")) throw new InvalidDataException("Edge couldn't play that video (" + end + "). It needs to be a VP8 or VP9 .webm.");
                var frames = new List<byte[]>();
                lock (parts)
                    foreach (var part in parts.Values)
                    {
                        string url = part.ToString();
                        int comma = url.IndexOf(',');
                        using (var ms = new MemoryStream(Convert.FromBase64String(url.Substring(comma + 1))))
                        using (var img = Image.FromStream(ms)) frames.Add(Fit(img, w, h));
                    }
                return frames;
            }
            finally
            {
                for (int tries = 0; tries < 10 && Directory.Exists(work); tries++)   // Edge lets go of its profile a moment after it's killed
                {
                    try { Directory.Delete(work, true); } catch (Exception) { Thread.Sleep(200); }
                }
            }
        }

        // `[...:INFO:CONSOLE:6] "F0 data:...", source: file:///... (6)` → `F0 data:...`
        static string ConsoleMessage(string line)
        {
            if (line == null || line.IndexOf(":CONSOLE", StringComparison.Ordinal) < 0) return null;
            int start = line.IndexOf("] \"", StringComparison.Ordinal), stop = line.LastIndexOf("\", source:", StringComparison.Ordinal);
            return start < 0 || stop <= start + 3 ? null : line.Substring(start + 3, stop - start - 3);
        }

        static void KillTree(Process p)
        {
            try
            {
                if (p.HasExited) return;
                using (var kill = Process.Start(new ProcessStartInfo("taskkill", "/PID " + p.Id + " /T /F") { UseShellExecute = false, CreateNoWindow = true }))
                    kill.WaitForExit(5000);
            }
            catch (Exception e) { Log.Write("custom sprite: couldn't stop Edge: " + e.Message); }
        }

        static string FindEdge()
        {
            foreach (var hive in new[] { Microsoft.Win32.Registry.CurrentUser, Microsoft.Win32.Registry.LocalMachine })
                try
                {
                    using (var key = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\msedge.exe"))
                    {
                        var path = key == null ? null : key.GetValue(null) as string;
                        if (!string.IsNullOrEmpty(path) && File.Exists(path)) return path;
                    }
                }
                catch (Exception) { }
            foreach (var dir in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) })
            {
                string path = Path.Combine(dir, @"Microsoft\Edge\Application\msedge.exe");
                if (File.Exists(path)) return path;
            }
            return null;
        }
    }
}
