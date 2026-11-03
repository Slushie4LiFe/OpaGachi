using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace OpaGachi
{
    sealed class AgentEvent
    {
        public string Path;
        public IDictionary<string, object> Body;
    }

    // Minimal HTTP/1.1 listener on 127.0.0.1 for agent adapters (Claude Code HTTP hooks, test scripts).
    // Each request is answered before its body is parsed, so a hook never waits on the pets.
    // Events are queued here and handled on the UI thread by PetManager.
    sealed class EventServer
    {
        const int MaxHeaderBytes = 16 * 1024;
        const int MaxBodyBytes = 64 * 1024 * 1024;   // PreToolUse for Write carries whole files

        public readonly ConcurrentQueue<AgentEvent> Queue = new ConcurrentQueue<AgentEvent>();
        public volatile string StateJson = "[]";      // served on GET /state; refreshed by PetManager

        TcpListener listener;
        volatile bool running;

        public bool Start(int port, out string error)
        {
            try
            {
                listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
            }
            catch (SocketException e)
            {
                error = e.Message;
                return false;
            }
            running = true;
            var thread = new Thread(AcceptLoop) { IsBackground = true, Name = "OpaGachi.EventServer" };
            thread.Start();
            error = null;
            return true;
        }

        public void Stop()
        {
            running = false;
            try { listener.Stop(); } catch (Exception) { }
        }

        void AcceptLoop()
        {
            while (running)
            {
                TcpClient client;
                try { client = listener.AcceptTcpClient(); }
                catch (Exception) { if (!running) return; continue; }
                ThreadPool.QueueUserWorkItem(_ => Serve(client));
            }
        }

        void Serve(TcpClient client)
        {
            string method = null, path = null;
            byte[] body = null;
            try
            {
                using (client)
                {
                    client.ReceiveTimeout = 3000;
                    client.SendTimeout = 3000;
                    var stream = client.GetStream();
                    bool local;
                    if (!ReadRequest(stream, out method, out path, out body, out local))
                    {
                        Respond(stream, 400, "text/plain", "bad request");
                        return;
                    }
                    if (!local)
                    {
                        Respond(stream, 403, "text/plain", "forbidden");
                        return;
                    }
                    if (method == "GET" && path == "/state") Respond(stream, 200, "application/json", StateJson);
                    else if (method == "GET") Respond(stream, 200, "text/plain", "OpaGachi is running");
                    else if (method == "POST") Respond(stream, 200, null, null);
                    else Respond(stream, 405, "text/plain", "method not allowed");
                }
                if (method == "POST" && body != null && body.Length > 0)
                {
                    var json = Json.Parse(Encoding.UTF8.GetString(body));
                    if (json != null) Queue.Enqueue(new AgentEvent { Path = path, Body = json });
                }
            }
            catch (Exception e)
            {
                Log.Write("event server: " + (path ?? "?") + " " + e.GetType().Name + ": " + e.Message);
            }
        }

        // local is false for anything a web page could send: browsers add an Origin header to cross-site requests,
        // and a DNS-rebinding page arrives with its own Host name. Agent hooks and our helper send neither.
        static bool ReadRequest(Stream stream, out string method, out string path, out byte[] body, out bool local)
        {
            method = path = null;
            body = null;
            local = true;
            var header = new MemoryStream();
            int matched = 0, b;
            while (matched < 4)                         // read up to the blank line
            {
                b = stream.ReadByte();
                if (b < 0 || header.Length > MaxHeaderBytes) return false;
                header.WriteByte((byte)b);
                matched = (b == (matched % 2 == 0 ? '\r' : '\n')) ? matched + 1 : (b == '\r' ? 1 : 0);
            }
            var lines = Encoding.ASCII.GetString(header.ToArray()).Split(new[] { "\r\n" }, StringSplitOptions.None);
            var first = lines[0].Split(' ');
            if (first.Length < 2) return false;
            method = first[0].ToUpperInvariant();
            path = first[1].Split('?')[0];
            long length = 0;
            foreach (var line in lines)
            {
                int colon = line.IndexOf(':');
                if (colon <= 0) continue;
                string name = line.Substring(0, colon).Trim(), value = line.Substring(colon + 1).Trim();
                if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                    long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out length);
                else if (name.Equals("Origin", StringComparison.OrdinalIgnoreCase)) local = false;
                else if (name.Equals("Host", StringComparison.OrdinalIgnoreCase) && !IsLoopbackHost(value)) local = false;
            }
            if (length < 0 || length > MaxBodyBytes) return false;
            body = new byte[length];
            int read = 0;
            while (read < length)
            {
                int n = stream.Read(body, read, (int)length - read);
                if (n <= 0) return false;
                read += n;
            }
            return true;
        }

        // "127.0.0.1", "localhost" or "[::1]", with or without a port.
        static bool IsLoopbackHost(string host)
        {
            string name = host.StartsWith("[") ? host.Substring(0, host.IndexOf(']') + 1) : host.Split(':')[0];
            return name == "127.0.0.1" || name.Equals("localhost", StringComparison.OrdinalIgnoreCase) || name == "[::1]";
        }

        static void Respond(Stream stream, int status, string contentType, string text)
        {
            byte[] payload = text == null ? new byte[0] : Encoding.UTF8.GetBytes(text);
            string reason = status == 200 ? "OK" : status == 400 ? "Bad Request" : status == 403 ? "Forbidden" : "Method Not Allowed";
            var head = new StringBuilder();
            head.Append("HTTP/1.1 ").Append(status).Append(' ').Append(reason).Append("\r\n");
            if (contentType != null) head.Append("Content-Type: ").Append(contentType).Append("; charset=utf-8\r\n");
            head.Append("Content-Length: ").Append(payload.Length).Append("\r\nConnection: close\r\n\r\n");
            byte[] h = Encoding.ASCII.GetBytes(head.ToString());
            stream.Write(h, 0, h.Length);
            if (payload.Length > 0) stream.Write(payload, 0, payload.Length);
            stream.Flush();
        }

        // Client side, used by the --hook-hostinfo helper process.
        public static bool Post(int port, string path, string json, int timeoutMs)
        {
            try
            {
                using (var client = new TcpClient())
                {
                    var connect = client.BeginConnect(IPAddress.Loopback, port, null, null);
                    if (!connect.AsyncWaitHandle.WaitOne(timeoutMs)) return false;
                    client.EndConnect(connect);
                    client.ReceiveTimeout = timeoutMs;
                    client.SendTimeout = timeoutMs;
                    var stream = client.GetStream();
                    byte[] body = Encoding.UTF8.GetBytes(json);
                    string head = "POST " + path + " HTTP/1.1\r\nHost: 127.0.0.1\r\nContent-Type: application/json\r\nContent-Length: "
                                  + body.Length + "\r\nConnection: close\r\n\r\n";
                    byte[] h = Encoding.ASCII.GetBytes(head);
                    stream.Write(h, 0, h.Length);
                    stream.Write(body, 0, body.Length);
                    var reply = new byte[64];
                    int n = stream.Read(reply, 0, reply.Length);
                    return n > 12 && Encoding.ASCII.GetString(reply, 0, n).Contains(" 200 ");
                }
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
