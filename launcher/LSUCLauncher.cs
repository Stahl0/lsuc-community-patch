using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

static class SteamNative
{
    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr LoadLibrary(string path);

    [DllImport("steam_api64", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SteamAPI_Init();

    [DllImport("steam_api64", CallingConvention = CallingConvention.Cdecl)]
    public static extern void SteamAPI_Shutdown();

    [DllImport("steam_api64", CallingConvention = CallingConvention.Cdecl)]
    public static extern void SteamAPI_RunCallbacks();

    [DllImport("steam_api64", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr SteamAPI_SteamUserStats_v012();

    [DllImport("steam_api64", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SteamAPI_ISteamUserStats_RequestCurrentStats(IntPtr self);

    [DllImport("steam_api64", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SteamAPI_ISteamUserStats_GetAchievement(IntPtr self, string name, [MarshalAs(UnmanagedType.I1)] out bool achieved);

    [DllImport("steam_api64", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SteamAPI_ISteamUserStats_SetAchievement(IntPtr self, string name);

    [DllImport("steam_api64", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SteamAPI_ISteamUserStats_StoreStats(IntPtr self);
}

static class Launcher
{
    const string Title = "Union City Community Patch";
    const int Port = 47615;
    const string AppId = "1615100";
    static readonly string[] KnownAchievements = { "ACH_13", "ACH_14", "ACH_15", "ACH_16", "ACH_17", "ACH_18", "ACH_21" };

    static readonly string Root = AppDomain.CurrentDomain.BaseDirectory;
    static readonly object QueueLock = new object();
    static readonly Queue<string> Pending = new Queue<string>();
    static StreamWriter logWriter;
    static IntPtr stats = IntPtr.Zero;
    static volatile bool steamReady;
    static bool statsReady;
    static bool debugKeys;

    [STAThread]
    static int Main(string[] args)
    {
        logWriter = new StreamWriter(Path.Combine(Root, "launcher.log"), false, Encoding.UTF8);
        logWriter.AutoFlush = true;
        bool test = Has(args, "--test");
        bool windowed = Has(args, "--windowed");
        bool installOnly = Has(args, "--install-only");
        bool importSaves = Has(args, "--import-saves");
        debugKeys = Has(args, "--debug-keys");
        Log("launcher started, args: " + string.Join(" ", args));

        string gameDir = FindGameDir();
        Log(gameDir == null ? "Steam game folder not found" : "game folder: " + gameDir);

        string swf = PrepareGame(gameDir, installOnly);
        if (swf == null)
        {
            return 1;
        }
        if (importSaves)
        {
            ImportSaves(swf, true);
        }
        if (installOnly)
        {
            Log("install only, done");
            return 0;
        }

        if (gameDir != null)
        {
            InitSteam(gameDir);
        }
        if (test)
        {
            RunTest();
            return 0;
        }

        Thread listener = new Thread(Listen);
        listener.IsBackground = true;
        listener.Start();

        Process ruffle = StartRuffle(swf, windowed);
        if (ruffle == null)
        {
            ShutdownSteam();
            return 1;
        }
        while (!ruffle.HasExited)
        {
            Pump();
            Thread.Sleep(100);
        }
        Pump();
        Log("game closed");
        ShutdownSteam();
        return 0;
    }

    static bool Has(string[] args, string flag)
    {
        return Array.IndexOf(args, flag) >= 0;
    }

    static void Log(string message)
    {
        lock (logWriter)
        {
            logWriter.WriteLine(DateTime.Now.ToString("HH:mm:ss") + "  " + message);
        }
    }

    static void Fail(string message, bool quiet)
    {
        Log("error: " + message);
        if (!quiet)
        {
            MessageBox.Show(message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    static string FindGameDir()
    {
        List<string> libraries = new List<string>();
        string steamPath = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
        if (steamPath != null)
        {
            steamPath = steamPath.Replace('/', '\\');
            libraries.Add(steamPath);
            string vdf = Path.Combine(steamPath, @"steamapps\libraryfolders.vdf");
            if (File.Exists(vdf))
            {
                foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"(.+?)\""))
                {
                    libraries.Add(m.Groups[1].Value.Replace("\\\\", "\\"));
                }
            }
        }
        foreach (string lib in libraries)
        {
            string dir = Path.Combine(lib, @"steamapps\common\The Last Stand Trilogy\TheLastStandLegacyCollection.app");
            if (File.Exists(Path.Combine(dir, @"gamefiles\lsuc.swf")))
            {
                return dir;
            }
        }
        return null;
    }

    static string PrepareGame(string gameDir, bool quiet)
    {
        string delta = Path.Combine(Root, "lsuc.delta");
        if (!File.Exists(delta))
        {
            string dev = Path.Combine(Root, @"build\lsuc.swf");
            if (File.Exists(dev))
            {
                return dev;
            }
            Fail("lsuc.delta is missing from the patch folder. Re-download the patch.", quiet);
            return null;
        }

        byte[] patch = File.ReadAllBytes(delta);
        if (patch.Length < 77 || Encoding.ASCII.GetString(patch, 0, 6) != "LSCPD1")
        {
            Fail("lsuc.delta is damaged. Re-download the patch.", quiet);
            return null;
        }
        byte[] sourceHash = new byte[32];
        byte[] targetHash = new byte[32];
        Buffer.BlockCopy(patch, 8, sourceHash, 0, 32);
        Buffer.BlockCopy(patch, 40, targetHash, 0, 32);

        string target = Path.Combine(Root, @"game\lsuc.swf");
        if (File.Exists(target) && Same(Sha256(File.ReadAllBytes(target)), targetHash))
        {
            return target;
        }

        if (gameDir == null)
        {
            Fail("Could not find The Last Stand: Legacy Collection in your Steam libraries. Install it through Steam first.", quiet);
            return null;
        }
        string sourcePath = Path.Combine(gameDir, @"gamefiles\lsuc.swf");
        byte[] source = File.ReadAllBytes(sourcePath);
        if (!Same(Sha256(source), sourceHash))
        {
            Fail("Your copy of Union City is a different version from the one this patch was made for (maybe a Steam update). Check for a newer patch release.", quiet);
            return null;
        }

        Log("installing patched game from " + sourcePath);
        byte[] result = Apply(source, patch);
        if (result == null || !Same(Sha256(result), targetHash))
        {
            Fail("Patching failed: the result did not match. Re-download the patch.", quiet);
            return null;
        }
        bool firstInstall = !File.Exists(target);
        Directory.CreateDirectory(Path.GetDirectoryName(target));
        string temp = target + ".tmp";
        File.WriteAllBytes(temp, result);
        if (File.Exists(target))
        {
            File.Delete(target);
        }
        File.Move(temp, target);
        Log("installed " + target);
        if (firstInstall && !quiet)
        {
            ImportSaves(target, false);
        }
        return target;
    }

    static byte[] Apply(byte[] source, byte[] patch)
    {
        int length = BitConverter.ToInt32(patch, 72);
        byte[] output = new byte[length];
        int p = 76;
        int o = 0;
        while (p < patch.Length)
        {
            char op = (char)patch[p++];
            if (op == 'C')
            {
                int offset = BitConverter.ToInt32(patch, p);
                int count = BitConverter.ToInt32(patch, p + 4);
                p += 8;
                Buffer.BlockCopy(source, offset, output, o, count);
                o += count;
            }
            else if (op == 'I')
            {
                int count = BitConverter.ToInt32(patch, p);
                p += 4;
                Buffer.BlockCopy(patch, p, output, o, count);
                p += count;
                o += count;
            }
            else
            {
                break;
            }
        }
        return o == length ? output : null;
    }

    static byte[] Sha256(byte[] data)
    {
        using (SHA256 sha = SHA256.Create())
        {
            return sha.ComputeHash(data);
        }
    }

    static bool Same(byte[] a, byte[] b)
    {
        if (a.Length != b.Length)
        {
            return false;
        }
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i])
            {
                return false;
            }
        }
        return true;
    }

    static string RuffleSaveDir(string swf)
    {
        string full = Path.GetFullPath(swf);
        string withoutDrive = full.Substring(Path.GetPathRoot(full).Length);
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"ruffle\SharedObjects\localhost", withoutDrive);
    }

    static void ImportSaves(string swf, bool asked)
    {
        string steamSaves = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"TheLastStandLegacyCollection\Local Store\#SharedObjects\gamefiles\lsuc.swf");
        string[] files = Directory.Exists(steamSaves) ? Directory.GetFiles(steamSaves, "ConArtists_TLSUC*.sol") : new string[0];
        if (files.Length == 0)
        {
            Log("no Steam saves to import");
            if (asked)
            {
                MessageBox.Show("No Union City saves were found from the Steam version.", Title);
            }
            return;
        }
        string target = RuffleSaveDir(swf);
        bool existing = Directory.Exists(target) && Directory.GetFiles(target, "ConArtists_TLSUC*.sol").Length > 0;
        string question = existing
            ? "Copy your Steam saves into the patched game?\n\nThis REPLACES the patched game's save slots with the same numbers. Your Steam saves are not changed."
            : "Copy your " + files.Length + " Steam save slot(s) into the patched game?\n\nYour Steam saves are not changed.";
        if (MessageBox.Show(question, Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            Log("save import declined");
            return;
        }
        Directory.CreateDirectory(target);
        foreach (string file in files)
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
            Log("imported save " + Path.GetFileName(file));
        }
    }

    static void InitSteam(string gameDir)
    {
        Environment.SetEnvironmentVariable("SteamAppId", AppId);
        Environment.SetEnvironmentVariable("SteamGameId", AppId);
        if (SteamNative.LoadLibrary(Path.Combine(gameDir, "steam_api64.dll")) == IntPtr.Zero)
        {
            Log("could not load steam_api64.dll, error " + Marshal.GetLastWin32Error());
            return;
        }
        try
        {
            steamReady = SteamNative.SteamAPI_Init();
        }
        catch (Exception e)
        {
            Log("SteamAPI_Init threw: " + e.Message);
            return;
        }
        if (!steamReady)
        {
            Log("SteamAPI_Init failed (is Steam running and logged in?)");
            return;
        }
        stats = SteamNative.SteamAPI_SteamUserStats_v012();
        bool requested = SteamNative.SteamAPI_ISteamUserStats_RequestCurrentStats(stats);
        Log("Steam connected, stats requested: " + requested);
    }

    static void ShutdownSteam()
    {
        if (steamReady)
        {
            SteamNative.SteamAPI_Shutdown();
            Log("Steam shut down");
        }
    }

    static void CheckStatsReady()
    {
        if (statsReady || !steamReady)
        {
            return;
        }
        bool achieved;
        if (SteamNative.SteamAPI_ISteamUserStats_GetAchievement(stats, KnownAchievements[0], out achieved))
        {
            statsReady = true;
            Log("Steam stats received");
        }
    }

    static void Pump()
    {
        if (!steamReady)
        {
            return;
        }
        SteamNative.SteamAPI_RunCallbacks();
        CheckStatsReady();
        if (!statsReady)
        {
            return;
        }
        bool changed = false;
        while (true)
        {
            string name;
            lock (QueueLock)
            {
                if (Pending.Count == 0)
                {
                    break;
                }
                name = Pending.Dequeue();
            }
            bool already;
            SteamNative.SteamAPI_ISteamUserStats_GetAchievement(stats, name, out already);
            if (already)
            {
                Log(name + " already unlocked on Steam");
                continue;
            }
            bool set = SteamNative.SteamAPI_ISteamUserStats_SetAchievement(stats, name);
            Log("unlock " + name + ": " + set);
            changed = true;
        }
        if (changed)
        {
            Log("store stats: " + SteamNative.SteamAPI_ISteamUserStats_StoreStats(stats));
        }
    }

    static void RunTest()
    {
        DateTime until = DateTime.Now.AddSeconds(8);
        while (steamReady && !statsReady && DateTime.Now < until)
        {
            SteamNative.SteamAPI_RunCallbacks();
            CheckStatsReady();
            Thread.Sleep(100);
        }
        if (statsReady)
        {
            foreach (string name in KnownAchievements)
            {
                bool achieved;
                bool ok = SteamNative.SteamAPI_ISteamUserStats_GetAchievement(stats, name, out achieved);
                Log("test " + name + ": " + (ok ? (achieved ? "unlocked" : "locked") : "unknown id"));
            }
        }
        else
        {
            Log("test: stats not available");
        }
        ShutdownSteam();
    }

    static Process StartRuffle(string swf, bool windowed)
    {
        string ruffle = Path.Combine(Root, @"ruffle\ruffle.exe");
        if (!File.Exists(ruffle))
        {
            ruffle = Path.Combine(Root, @"tools\ruffle\ruffle.exe");
        }
        if (!File.Exists(ruffle))
        {
            Fail("ruffle\\ruffle.exe is missing from the patch folder. Re-download the patch.", false);
            return null;
        }
        ProcessStartInfo info = new ProcessStartInfo(ruffle);
        info.Arguments = (windowed ? "" : "--fullscreen ") + "--socket-allow 127.0.0.1:" + Port + " -PlscpLauncher=1" + (debugKeys ? " -PlscpTest=1" : "") + " \"" + swf + "\"";
        info.WorkingDirectory = Path.GetDirectoryName(swf);
        info.UseShellExecute = false;
        Log("starting ruffle " + info.Arguments);
        return Process.Start(info);
    }

    static void Listen()
    {
        TcpListener server;
        try
        {
            server = new TcpListener(IPAddress.Loopback, Port);
            server.Start();
        }
        catch (Exception e)
        {
            Log("could not open port " + Port + ": " + e.Message);
            return;
        }
        Log("listening on 127.0.0.1:" + Port);
        while (true)
        {
            TcpClient client = server.AcceptTcpClient();
            Log("game connected");
            Thread t = new Thread(Serve);
            t.IsBackground = true;
            t.Start(client);
        }
    }

    static void Serve(object state)
    {
        TcpClient client = (TcpClient)state;
        NetworkStream stream = client.GetStream();
        StringBuilder current = new StringBuilder();
        byte[] buffer = new byte[1024];
        try
        {
            while (true)
            {
                int read = stream.Read(buffer, 0, buffer.Length);
                if (read <= 0)
                {
                    break;
                }
                for (int i = 0; i < read; i++)
                {
                    if (buffer[i] == 0)
                    {
                        Handle(current.ToString().Trim(), stream);
                        current.Length = 0;
                    }
                    else
                    {
                        current.Append((char)buffer[i]);
                    }
                }
            }
        }
        catch (Exception e)
        {
            Log("connection error: " + e.Message);
        }
        Log("game disconnected");
        client.Close();
    }

    static void Handle(string message, NetworkStream stream)
    {
        if (message.Length == 0)
        {
            return;
        }
        if (message == "<policy-file-request/>")
        {
            Send(stream, "<?xml version=\"1.0\"?><cross-domain-policy><allow-access-from domain=\"*\" to-ports=\"" + Port + "\"/></cross-domain-policy>");
            return;
        }
        if (Array.IndexOf(KnownAchievements, message) < 0)
        {
            Log("ignored message: " + message);
            return;
        }
        Log("game reports " + message);
        if (!steamReady)
        {
            Send(stream, "NO " + message);
            return;
        }
        lock (QueueLock)
        {
            Pending.Enqueue(message);
        }
        Send(stream, "OK " + message);
    }

    static void Send(NetworkStream stream, string text)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(text + "\0");
        lock (stream)
        {
            stream.Write(bytes, 0, bytes.Length);
        }
    }
}
