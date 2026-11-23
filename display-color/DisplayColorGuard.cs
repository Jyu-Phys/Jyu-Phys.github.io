// SPDX-License-Identifier: GPL-3.0-or-later
// DisplayColorGuard: current-user SDR color backup and recovery.
// The target-gamma calculation and private API protocol are adapted from
// nvBrightness, Copyright (c) 2025 Pete Batard, GPL-3.0-or-later:
// https://github.com/pbatard/nvBrightness
// No third-party runtime packages. Only the two captured ARZOPA identities
// are apply targets. The BOE reference panel is checked for side effects.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32;

public sealed class ApprovedMonitor {
    public int originalDisplayNumber;
    public string label, registryKey;
    public int[] brightness, contrast, gammaPercent;
    public double[] gamma;
}
public sealed class ApprovedProfile {
    public int schemaVersion;
    public string name;
    public List<ApprovedMonitor> monitors;
    public Dictionary<string, Dictionary<string, int>> colorRegistryValues;
}
public sealed class SavedMonitor {
    public int originalDisplayNumber;
    public string label, registryKey, monitorPath, edidHash;
    public ushort[] ramp;
    public float[] targetGamma;
}
public sealed class RuntimeProfile {
    public int schemaVersion = 2;
    public string backend = "nvapi-target-gamma-v1-plus-gdi-baseline";
    public string approvedCalibrationHash, capturedUtc, userSid, referenceEdidHash;
    public List<SavedMonitor> monitors = new List<SavedMonitor>();
}
public sealed class LiveMonitor {
    public string gdiName, monitorPath, friendlyName, edidHash;
    public int ordinal, width, height, x, y;
    public uint nvidiaDisplayId;
    public string nvidiaRegistryKey;
    public bool advancedColorKnown, advancedColorEnabled;
}

public static class DisplayColorGuard {
    static string CurrentMode;
    static readonly string Root = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
    static readonly string Exe = Assembly.GetExecutingAssembly().Location;
    static readonly string ApprovedFile = Path.Combine(Root, "display-color-approved.json");
    static readonly string RuntimeFile = Path.Combine(Root, "display-color-runtime.json");
    static readonly string StatusFile = Path.Combine(Root, "display-color-status.json");
    static readonly string PauseFile = Path.Combine(Root, "display-color.paused");
    static readonly string StopFile = Path.Combine(Root, "display-color.stop");
    static readonly string Sid = WindowsIdentity.GetCurrent().User.Value;
    const string TaskName = "Jyu_DisplayColorGuard";
    const string RegBase = @"Software\NVIDIA Corporation\Global\NVTweak\Devices\";
    static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 1048576 };

    [STAThread]
    public static int Main(string[] args) {
        string mode = args.Length == 0 ? "status" : args[0].ToLowerInvariant();
        CurrentMode=mode;
        try {
            if (mode == "inspect") { Report(mode, true, "Read-only display inventory and NVIDIA ramp probe.", Native.Displays().Select(m => Probe(m)).ToArray()); return 0; }
            if (mode == "capture") { Capture(); Report(mode, true, "Current GDI baselines captured; NVIDIA target curves rebuilt from approved RGB settings.", LoadRuntime()); return 0; }
            if (mode == "selftest") { SelfTest(); Report(mode, true, "NVIDIA accepted both target curves; GDI baselines read back; reference display unchanged.", LoadRuntime().monitors); return 0; }
            if (mode == "verifyguard") { VerifyGuard(); return 0; }
            if (mode == "restore") { int n = Apply(true); if (n != 2) throw new Exception("Both saved SDR displays must be connected for a manual full restore. Applied: " + n); Report(mode, true, "Approved profile restored and read back.", n); return 0; }
            if (mode == "enable") { Enable(); return 0; }
            if (mode == "watch") { Watch(); return 0; }
            if (mode == "pause") { File.WriteAllText(PauseFile, DateTime.UtcNow.ToString("o")); Report(mode, true, "Automatic restoration paused; current colors left unchanged.", null); return 0; }
            if (mode == "resume") { if (File.Exists(PauseFile)) File.Delete(PauseFile); Apply(true); Report(mode, true, "Automatic restoration resumed.", null); return 0; }
            if (mode == "disable") { Disable(); return 0; }
            if (mode != "status") throw new Exception("Unknown mode: " + mode);
            Report(mode, true, "Status only; no color settings changed.", new { runtimeCaptured = File.Exists(RuntimeFile), paused = File.Exists(PauseFile), taskInstalled = TaskExists() });
            return 0;
        } catch (Exception e) {
            Report(mode, false, e.Message, null);
            Log(mode + ": " + e);
            return 1;
        }
    }

    static string Hash(byte[] data) {
        using (SHA256 s = SHA256.Create()) return BitConverter.ToString(s.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
    }
    static object Probe(LiveMonitor monitor) {
        try {
            ushort[] ramp = Native.GetGdiRamp(monitor.gdiName);
            return new { monitor = monitor, backend = "nvapi-target-gamma-v1-plus-gdi-baseline", gdiEndpoints = new int[] { ramp[255], ramp[511], ramp[767] }, gdiMidpoints = new int[] { ramp[128], ramp[384], ramp[640] }, error = (string)null };
        } catch (Exception e) { return new { monitor = monitor, error = e.Message }; }
    }
    static void SaveJson(string path, object value) {
        string temp = path + "." + Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture) + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temp, Json.Serialize(value), new UTF8Encoding(false));
        if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
    }
    static void Report(string mode, bool ok, string message, object details) {
        SaveJson(StatusFile, new { mode = mode, ok = ok, message = message, utc = DateTime.UtcNow.ToString("o"), processId = Process.GetCurrentProcess().Id, taskName = TaskName, details = details });
    }
    static void Log(string text) {
        File.AppendAllText(Path.Combine(Root, "display-color-guard.log"), DateTime.UtcNow.ToString("o") + " " + text + Environment.NewLine, Encoding.UTF8);
    }
    static ApprovedProfile LoadApproved() {
        ApprovedProfile a = Json.Deserialize<ApprovedProfile>(File.ReadAllText(ApprovedFile, Encoding.UTF8));
        if (a == null || a.schemaVersion != 1 || a.monitors == null || a.monitors.Count != 2) throw new Exception("Invalid approved profile.");
        if (!a.monitors.Select(m => m.originalDisplayNumber).OrderBy(n => n).SequenceEqual(new int[] { 2, 3 })) throw new Exception("Only original displays 2 and 3 are allowed.");
        if (a.monitors.Select(m => m.registryKey).Distinct().Count() != 2) throw new Exception("Duplicate saved registry identity.");
        foreach (ApprovedMonitor m in a.monitors) {
            if (!Regex.IsMatch(m.registryKey, @"^\d+-0$") || !a.colorRegistryValues.ContainsKey(m.registryKey)) throw new Exception("Invalid registry binding.");
            if (m.brightness.Length != 3 || m.contrast.Length != 3 || m.gammaPercent.Length != 3) throw new Exception("Invalid RGB settings.");
        }
        return a;
    }
    static string CalibrationHash(ApprovedProfile a) {
        StringBuilder canonical=new StringBuilder();
        canonical.Append(a.schemaVersion.ToString(CultureInfo.InvariantCulture)).Append('|');
        foreach(ApprovedMonitor m in a.monitors.OrderBy(v=>v.originalDisplayNumber)) {
            canonical.Append(m.originalDisplayNumber.ToString(CultureInfo.InvariantCulture)).Append(':').Append(m.registryKey).Append(':');
            foreach(int v in m.brightness) canonical.Append(v.ToString(CultureInfo.InvariantCulture)).Append(',');
            canonical.Append(':');
            foreach(int v in m.contrast) canonical.Append(v.ToString(CultureInfo.InvariantCulture)).Append(',');
            canonical.Append(':');
            foreach(int v in m.gammaPercent) canonical.Append(v.ToString(CultureInfo.InvariantCulture)).Append(',');
            foreach(KeyValuePair<string,int> value in a.colorRegistryValues[m.registryKey].OrderBy(v=>v.Key,StringComparer.Ordinal))
                canonical.Append('|').Append(value.Key).Append('=').Append(value.Value.ToString(CultureInfo.InvariantCulture));
            canonical.Append(';');
        }
        return Hash(Encoding.UTF8.GetBytes(canonical.ToString()));
    }
    static RuntimeProfile LoadRuntime() {
        RuntimeProfile p = Json.Deserialize<RuntimeProfile>(File.ReadAllText(RuntimeFile, Encoding.UTF8));
        if (p == null || p.schemaVersion != 2 || p.userSid != Sid || p.monitors.Count != 2) throw new Exception("Profile belongs to another user or is incomplete.");
        if (p.approvedCalibrationHash != CalibrationHash(LoadApproved())) throw new Exception("Approved RGB calibration changed; do not reuse old runtime ramps.");
        foreach (SavedMonitor m in p.monitors) {
            if ((m.originalDisplayNumber != 2 && m.originalDisplayNumber != 3) || String.IsNullOrEmpty(m.edidHash) || !IsArzopaPath(m.monitorPath)) throw new Exception("Unsafe display binding.");
            ValidateRamp(m.ramp);
            ValidateTargetGamma(m.targetGamma);
        }
        return p;
    }
    static bool IsArzopaPath(string path) { return path != null && path.IndexOf("#GWD1161#", StringComparison.OrdinalIgnoreCase) >= 0; }
    static void ValidateRamp(ushort[] ramp) {
        if (ramp == null || ramp.Length != 768) throw new Exception("Incomplete gamma ramp.");
        for (int c = 0; c != 3; ++c) {
            if (ramp[c * 256 + 255] < 32768) throw new Exception("Unexpectedly dark ramp; refusing to install.");
            for (int i = 1; i != 256; ++i) if (ramp[c * 256 + i] < ramp[c * 256 + i - 1]) throw new Exception("Non-monotonic ramp.");
        }
    }
    static void ValidateTargetGamma(float[] values) {
        if (values == null || values.Length != 3072) throw new Exception("Incomplete NVIDIA target curve.");
        for (int i=0; i<values.Length; ++i) {
            if (Single.IsNaN(values[i]) || Single.IsInfinity(values[i]) || values[i]<0 || values[i]>1) throw new Exception("Invalid target curve.");
            if (i>=3 && values[i]<values[i-3]) throw new Exception("Non-monotonic target curve.");
        }
    }
    // Protocol/math ported from nvBrightness/src/nvDisplay.cpp (GPL-3.0-or-later).
    static float[] BuildTargetGamma(ApprovedMonitor m) {
        float[] result = new float[3072];
        for (int index=0; index<1024; ++index) for (int color=0; color<3; ++color) {
            float contrast=(m.contrast[color]-100.0f)/100.0f;
            float shaped=contrast<=0 ? (contrast+1.0f)*(index/1023.0f-0.5f) : (index/1023.0f-0.5f)/(1.0f-contrast);
            float value=(m.brightness[color]-100.0f)/100.0f+shaped+0.5f;
            value=Math.Max(0.0f,Math.Min(1.0f,value));
            result[3*index+color]=(float)Math.Max(0.0,Math.Min(1.0,Math.Pow(value,1.0/(m.gammaPercent[color]/100.0))));
        }
        ValidateTargetGamma(result);
        return result;
    }
    static bool Same(ushort[] a, ushort[] b, int tolerance) {
        return a != null && b != null && a.Length == b.Length && !a.Where((v, i) => Math.Abs((int)v - b[i]) > tolerance).Any();
    }
    static LiveMonitor Match(SavedMonitor saved, List<LiveMonitor> live) {
        List<LiveMonitor> matches = live.Where(m => IsArzopaPath(m.monitorPath) && m.edidHash == saved.edidHash).ToList();
        if (matches.Count > 1) throw new Exception("Ambiguous monitor identity; refusing to choose by display number.");
        return matches.Count == 1 ? matches[0] : null;
    }
    static bool CheckColorRegistry(ApprovedProfile a, string sourceId, string destinationId, bool write) {
        bool changed=false;
        if (!Regex.IsMatch(destinationId ?? "", @"^\d+-0$")) throw new Exception("Invalid live NVIDIA registry identity.");
            Dictionary<string, int> values = a.colorRegistryValues[sourceId];
            using (RegistryKey key = write ? Registry.CurrentUser.CreateSubKey(RegBase + destinationId + @"\Color") : Registry.CurrentUser.OpenSubKey(RegBase + destinationId + @"\Color")) {
                if (key == null) throw new Exception("Missing saved NVIDIA color registry key: " + destinationId);
                foreach (KeyValuePair<string, int> v in values) {
                    object current = key.GetValue(v.Key);
                    if (current == null || Convert.ToInt32(current, CultureInfo.InvariantCulture) != v.Value) {
                        if (!write) throw new Exception("Current NVIDIA values no longer match the user-approved backup: " + destinationId + "/" + v.Key);
                        key.SetValue(v.Key, v.Value, RegistryValueKind.DWord);
                        changed=true;
                    }
                }
            }
        return changed;
    }
    static void Capture() {
        if (File.Exists(RuntimeFile)) throw new Exception("Runtime snapshot already exists; it is never overwritten automatically.");
        ApprovedProfile a = LoadApproved();
        foreach (ApprovedMonitor monitor in a.monitors) CheckColorRegistry(a,monitor.registryKey,monitor.registryKey,false);
        List<LiveMonitor> live = Native.Displays();
        RuntimeProfile p = new RuntimeProfile { userSid = Sid, capturedUtc = DateTime.UtcNow.ToString("o"), approvedCalibrationHash = CalibrationHash(a) };
        LiveMonitor reference = live.SingleOrDefault(candidate => candidate.monitorPath != null && candidate.monitorPath.IndexOf("#BOE0A1C#", StringComparison.OrdinalIgnoreCase) >= 0);
        if (reference == null || String.IsNullOrEmpty(reference.edidHash)) throw new Exception("Cannot identify the unchanged BOE reference panel.");
        p.referenceEdidHash = reference.edidHash;
        foreach (ApprovedMonitor m in a.monitors) {
            // User-facing labels differ from GDI ordinals. The native NVIDIA GUID
            // resolves the actual per-display registry identity; EDID persists the bind.
            List<LiveMonitor> candidates = live.Where(candidate => IsArzopaPath(candidate.monitorPath) && candidate.nvidiaRegistryKey==m.registryKey).ToList();
            if (candidates.Count != 1) throw new Exception("Original screen " + m.originalDisplayNumber + " is not uniquely identified as the expected ARZOPA.");
            LiveMonitor d = candidates[0];
            if (!d.advancedColorKnown || d.advancedColorEnabled) throw new Exception("Capture requires confirmed SDR mode.");
            if (String.IsNullOrEmpty(d.edidHash)) throw new Exception("Cannot read unique EDID identity.");
            if (d.nvidiaDisplayId==reference.nvidiaDisplayId) throw new Exception("Reference/target NVIDIA identity collision.");
            ushort[] ramp = Native.GetGdiRamp(d.gdiName);
            ValidateRamp(ramp);
            p.monitors.Add(new SavedMonitor { originalDisplayNumber = m.originalDisplayNumber, label = m.label, registryKey = m.registryKey, monitorPath = d.monitorPath, edidHash = d.edidHash, ramp = ramp, targetGamma=BuildTargetGamma(m) });
        }
        if (p.monitors[0].edidHash == p.monitors[1].edidHash) throw new Exception("The two display identities are indistinguishable; no automatic startup task will be installed.");
        SaveJson(RuntimeFile, p);
    }
    static int Apply(bool force) {
        RuntimeProfile p = LoadRuntime();
        ApprovedProfile a = LoadApproved();
        List<LiveMonitor> live = Native.Displays();
        int applied = 0;
        foreach (SavedMonitor saved in p.monitors) {
            LiveMonitor d = Match(saved, live);
            if (d == null || !d.advancedColorKnown || d.advancedColorEnabled) continue;
            ushort[] actual = Native.GetGdiRamp(d.gdiName);
            bool drift=!Same(actual,saved.ramp,257);
            bool metadataChanged=CheckColorRegistry(a,saved.registryKey,d.nvidiaRegistryKey,true);
            if (drift) Native.SetGdiRamp(d.gdiName,saved.ramp);
            if (force || drift || metadataChanged) {
                Native.SetTargetGamma(d.nvidiaDisplayId,saved.targetGamma);
                if (!Same(Native.GetGdiRamp(d.gdiName), saved.ramp, 257)) throw new Exception("GDI baseline read-back verification failed for " + saved.label);
                if(drift || metadataChanged) SaveJson(Path.Combine(Root,"display-color-last-recovery.json"),new {
                    utc=DateTime.UtcNow.ToString("o"),processId=Process.GetCurrentProcess().Id,mode=CurrentMode,
                    originalDisplayNumber=saved.originalDisplayNumber,gdiDrift=drift,registryDrift=metadataChanged,nativeApplyAccepted=true
                });
            }
            applied++;
        }
        return applied;
    }
    static void SelfTest() {
        RuntimeProfile p = LoadRuntime();
        List<LiveMonitor> live = Native.Displays();
        LiveMonitor reference = live.SingleOrDefault(m => m.edidHash == p.referenceEdidHash && !IsArzopaPath(m.monitorPath));
        if (reference == null) throw new Exception("Cannot verify the untouched reference display.");
        ushort[] before = Native.GetGdiRamp(reference.gdiName);
        if (Apply(true) != 2) throw new Exception("Both captured SDR displays must be connected for verification.");
        ushort[] after = Native.GetGdiRamp(reference.gdiName);
        if (!Same(before, after, 0)) {
            Native.SetGdiRamp(reference.gdiName,before);
            throw new Exception("Unexpected reference-display side effect; original ramp restored, startup task not installed.");
        }
    }
    static void VerifyGuard() {
        if (File.Exists(PauseFile) || !Native.IsNormalDesktop() || Native.IsForegroundFullscreen()) throw new Exception("Guard recovery test requires an unlocked normal desktop and unpaused guard.");
        RuntimeProfile p=LoadRuntime(); List<LiveMonitor> live=Native.Displays();
        SavedMonitor saved=p.monitors[0]; LiveMonitor target=Match(saved,live);
        if (target==null) throw new Exception("Guard test target not connected.");
        LiveMonitor reference=live.Single(m=>m.edidHash==p.referenceEdidHash);
        ushort[] beforeReference=Native.GetGdiRamp(reference.gdiName);
        ushort[] perturb=Array.ConvertAll(saved.ramp,v=>(ushort)(v*0.99));
        ApprovedProfile approved=LoadApproved();
        int expectedGamma=approved.colorRegistryValues[saved.registryKey]["3538952"];
        DateTime started=DateTime.UtcNow;
        bool recovered=false;
        try {
            using(RegistryKey key=Registry.CurrentUser.OpenSubKey(RegBase+target.nvidiaRegistryKey+@"\Color",true)) key.SetValue("3538952",expectedGamma-1,RegistryValueKind.DWord);
            Native.SetGdiRamp(target.gdiName,perturb);
            DateTime until=DateTime.UtcNow.AddSeconds(18);
            while (DateTime.UtcNow<until) {
                bool gammaRestored;
                using(RegistryKey key=Registry.CurrentUser.OpenSubKey(RegBase+target.nvidiaRegistryKey+@"\Color")) gammaRestored=Convert.ToInt32(key.GetValue("3538952"))==expectedGamma;
                string evidenceFile=Path.Combine(Root,"display-color-last-recovery.json");
                bool watcherEvidence=false;
                if(File.Exists(evidenceFile)) {
                    Dictionary<string,object> evidence=Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(evidenceFile,Encoding.UTF8));
                    watcherEvidence=Convert.ToString(evidence["mode"])=="watch" && Convert.ToInt32(evidence["originalDisplayNumber"])==saved.originalDisplayNumber &&
                        DateTime.Parse(Convert.ToString(evidence["utc"]),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind).ToUniversalTime()>=started &&
                        Convert.ToInt32(evidence["processId"])!=Process.GetCurrentProcess().Id && Convert.ToBoolean(evidence["nativeApplyAccepted"]);
                }
                if (gammaRestored && watcherEvidence && Same(Native.GetGdiRamp(target.gdiName),saved.ramp,257)) { recovered=true; break; }
                Thread.Sleep(250);
            }
            if (!recovered) throw new Exception("Background guard did not recover the small diagnostic drift in time.");
            if (!Same(beforeReference,Native.GetGdiRamp(reference.gdiName),0)) throw new Exception("Reference display changed during guard test.");
            Report("verifyguard",true,"Background watcher evidence confirmed: temporary GDI and registry drift recovered; reference display unchanged.",null);
        } finally { Apply(true); }
    }
    static void Enable() {
        if (!File.Exists(RuntimeFile)) Capture();
        SelfTest();
        if (File.Exists(StopFile)) File.Delete(StopFile);
        if (File.Exists(PauseFile)) File.Delete(PauseFile);
        dynamic service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service"));
        service.Connect();
        dynamic folder = service.GetFolder(@"\");
        dynamic existing = null;
        try { existing = folder.GetTask(TaskName); } catch (COMException) { } catch (FileNotFoundException) { }
        if (existing != null && !String.Equals((string)existing.Definition.Actions[1].Path, Exe, StringComparison.OrdinalIgnoreCase)) throw new Exception("A different task already owns this name.");
        string xml = "<?xml version=\"1.0\"?><Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">" +
            "<RegistrationInfo><Author>Jyu</Author><Description>Restore the user-approved SDR color snapshot for the two identified ARZOPA monitors.</Description></RegistrationInfo>" +
            "<Triggers><LogonTrigger><Enabled>true</Enabled><UserId>" + Sid + "</UserId><Delay>PT15S</Delay></LogonTrigger></Triggers>" +
            "<Principals><Principal id=\"User\"><UserId>" + Sid + "</UserId><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>" +
            "<Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><StartWhenAvailable>true</StartWhenAvailable><ExecutionTimeLimit>PT0S</ExecutionTimeLimit><RestartOnFailure><Interval>PT1M</Interval><Count>3</Count></RestartOnFailure></Settings>" +
            "<Actions Context=\"User\"><Exec><Command>" + SecurityElement.Escape(Exe) + "</Command><Arguments>watch</Arguments><WorkingDirectory>" + SecurityElement.Escape(Root) + "</WorkingDirectory></Exec></Actions></Task>";
        File.WriteAllText(Path.Combine(Root, "display-color-startup-task.xml"), xml, new UTF8Encoding(false));
        dynamic task = folder.RegisterTask(TaskName, xml, 6, Sid, null, 3, null);
        task.Run(null);
        Report("enable", true, "Verified and registered current-user logon task. Watcher launch requested.", new { task = TaskName, executable = Exe, checks = "distinct identities; exact captured ramps; verified restore; display 1 unchanged" });
    }
    static bool TaskExists() {
        try { dynamic s = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")); s.Connect(); dynamic t = s.GetFolder(@"\").GetTask(TaskName); return t != null; }
        catch (COMException) { return false; } catch (FileNotFoundException) { return false; }
    }
    static void Disable() {
        File.WriteAllText(StopFile, DateTime.UtcNow.ToString("o"));
        dynamic s = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")); s.Connect();
        dynamic folder = s.GetFolder(@"\");
        dynamic task = null; try { task = folder.GetTask(TaskName); } catch (COMException) { } catch (FileNotFoundException) { }
        if (task != null) {
            if (!String.Equals((string)task.Definition.Actions[1].Path, Exe, StringComparison.OrdinalIgnoreCase)) throw new Exception("Task path mismatch; refusing to alter another task.");
            task.Stop(0);
            folder.DeleteTask(TaskName, 0);
        }
        Report("disable", true, "Startup task removed; stop marker written. Existing colors and backups retained.", null);
    }
    static void Watch() {
        using (Mutex mutex = new Mutex(false, @"Local\JyuDisplayColorGuard_" + Sid)) {
            if (!mutex.WaitOne(0, false)) return;
            try {
                LoadRuntime(); LoadApproved();
                DateTime lastApply = DateTime.MinValue, previousTick = DateTime.UtcNow;
                string topology = "", previousError = "";
                bool wasUsable = false;
                Log("watch started");
                while (!File.Exists(StopFile)) {
                    try {
                        DateTime now = DateTime.UtcNow;
                        List<LiveMonitor> live = Native.Displays();
                        string signature = String.Join("|", live.Select(m => m.monitorPath + ":" + m.ordinal + ":" + m.advancedColorEnabled).OrderBy(v => v));
                        bool usable = !File.Exists(PauseFile) && Native.IsNormalDesktop() && !Native.IsForegroundFullscreen();
                        bool force = signature != topology || (now - previousTick).TotalSeconds > 30 || !wasUsable || (now - lastApply).TotalSeconds >= 60;
                        if (usable) {
                            int n = Apply(force);
                            if (force) { lastApply = now; Report("watch", true, "Watch running; matching SDR displays checked/restored.", new { matchedTargets = n, intervalSeconds = 10, fallbackRestoreSeconds = 60 }); }
                        }
                        topology = signature; previousTick = now; wasUsable = usable; previousError = "";
                    } catch (Exception e) {
                        if (previousError != e.Message) { Log(e.ToString()); Report("watch", false, e.Message, null); previousError = e.Message; }
                    }
                    for (int i = 0; i < 10 && !File.Exists(StopFile); ++i) Thread.Sleep(1000);
                }
                Log("watch stopped");
            } finally { mutex.ReleaseMutex(); }
        }
    }
}

public static class Native {
    [StructLayout(LayoutKind.Sequential)] struct Luid { public uint low; public int high; }
    [StructLayout(LayoutKind.Sequential)] struct Rational { public uint numerator, denominator; }
    [StructLayout(LayoutKind.Sequential)] struct Source { public Luid adapter; public uint id, modeIndex, flags; }
    [StructLayout(LayoutKind.Sequential)] struct Target {
        public Luid adapter; public uint id, modeIndex, technology, rotation, scaling;
        public Rational refresh; public uint scanline; public int available; public uint flags;
    }
    [StructLayout(LayoutKind.Sequential)] struct PathInfo { public Source source; public Target target; public uint flags; }
    [StructLayout(LayoutKind.Sequential)] struct Header { public uint type, size; public Luid adapter; public uint id; }
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] struct SourceName { public Header header; [MarshalAs(UnmanagedType.ByValTStr, SizeConst=32)] public string name; }
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] struct TargetName {
        public Header header; public uint flags, technology; public ushort manufacturer, product; public uint connector;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=64)] public string name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=128)] public string path;
    }
    [StructLayout(LayoutKind.Sequential)] struct AdvancedColor { public Header header; public uint value, encoding, bits; }
    [StructLayout(LayoutKind.Sequential)] struct Rect { public int left, top, right, bottom; }
    [StructLayout(LayoutKind.Sequential)] struct MonitorInfo { public uint cb; public Rect monitor, work; public uint flags; }
    [DllImport("user32.dll")] static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
    [DllImport("user32.dll")] static extern int QueryDisplayConfig(uint flags, ref uint paths, [In, Out] PathInfo[] info, ref uint modes, IntPtr modeInfo, IntPtr topology);
    [DllImport("user32.dll", EntryPoint="DisplayConfigGetDeviceInfo")] static extern int DeviceInfo(ref SourceName name);
    [DllImport("user32.dll", EntryPoint="DisplayConfigGetDeviceInfo")] static extern int DeviceInfo(ref TargetName name);
    [DllImport("user32.dll", EntryPoint="DisplayConfigGetDeviceInfo")] static extern int DeviceInfo(ref AdvancedColor info);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern IntPtr LoadLibraryEx(string file, IntPtr reserved, uint flags);
    [DllImport("kernel32.dll", CharSet=CharSet.Ansi)] static extern IntPtr GetProcAddress(IntPtr module, string name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr NvQuery(uint id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int NvInit();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl,CharSet=CharSet.Ansi)] delegate int NvDisplayId([MarshalAs(UnmanagedType.LPStr)] string name,out uint id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int NvRegistryId(uint id,uint selector,out Guid guid);
    [StructLayout(LayoutKind.Sequential)] struct TargetGamma {
        public uint version;
        [MarshalAs(UnmanagedType.ByValArray,SizeConst=3072)] public float[] values;
        public uint flags;
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int NvTargetGamma(uint id,ref TargetGamma ramp);
    static readonly IntPtr NvModule = LoadLibraryEx(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nvapi64.dll"), IntPtr.Zero, 0x800);
    static bool nvInitialized;
    static T NvFunction<T>(uint id) where T : class {
        if (NvModule == IntPtr.Zero) throw new Exception("Installed nvapi64.dll could not be loaded.");
        IntPtr query=GetProcAddress(NvModule,"nvapi_QueryInterface");
        if(query==IntPtr.Zero) throw new Exception("nvapi_QueryInterface is unavailable.");
        IntPtr p=((NvQuery)Marshal.GetDelegateForFunctionPointer(query,typeof(NvQuery)))(id);
        if (p == IntPtr.Zero) throw new Exception("Installed NVIDIA driver does not expose interface 0x"+id.ToString("X8"));
        return Marshal.GetDelegateForFunctionPointer(p, typeof(T)) as T;
    }
    static void EnsureNv() {
        if(nvInitialized) return;
        int result=NvFunction<NvInit>(0x0150E828)();
        if(result!=0) throw new Exception("NvAPI_Initialize failed: "+result);
        NvFunction<NvTargetGamma>(0x7082A053);
        nvInitialized=true;
    }
    static void IdentifyNvidia(LiveMonitor monitor) {
        EnsureNv(); uint id;
        int rc=NvFunction<NvDisplayId>(0xAE457190)(monitor.gdiName,out id);
        if(rc!=0) throw new Exception("Cannot resolve NVIDIA display ID for "+monitor.gdiName+": "+rc);
        Guid guid;
        rc=NvFunction<NvRegistryId>(0xD4A859F2)(id,1,out guid);
        if(rc!=0) throw new Exception("Cannot resolve NVIDIA registry identity: "+rc);
        monitor.nvidiaDisplayId=id;
        monitor.nvidiaRegistryKey=(BitConverter.ToUInt32(guid.ToByteArray(),4)^0xF0000000u).ToString(CultureInfo.InvariantCulture)+"-0";
    }
    public static List<LiveMonitor> Displays() {
        if (Marshal.SizeOf(typeof(PathInfo)) != 72 || Marshal.SizeOf(typeof(SourceName)) != 84 || Marshal.SizeOf(typeof(TargetName)) != 420) throw new Exception("Unexpected Windows interop structure size.");
        for (int retry = 0; retry < 3; ++retry) {
            uint paths, modes;
            int rc = GetDisplayConfigBufferSizes(2, out paths, out modes);
            if (rc != 0) throw new Exception("Display inventory failed: " + rc);
            PathInfo[] list = new PathInfo[paths];
            IntPtr modeBuffer = Marshal.AllocHGlobal(checked((int)Math.Max(1, modes) * 64));
            try {
                rc = QueryDisplayConfig(2, ref paths, list, ref modes, modeBuffer, IntPtr.Zero);
                if (rc == 122) continue;
                if (rc != 0) throw new Exception("QueryDisplayConfig failed: " + rc);
                List<LiveMonitor> result = new List<LiveMonitor>();
                for (int i = 0; i < paths; ++i) {
                    PathInfo p = list[i];
                    if (p.target.available == 0) continue;
                    SourceName s = new SourceName { header = new Header { type=1, size=(uint)Marshal.SizeOf(typeof(SourceName)), adapter=p.source.adapter, id=p.source.id } };
                    TargetName t = new TargetName { header = new Header { type=2, size=(uint)Marshal.SizeOf(typeof(TargetName)), adapter=p.target.adapter, id=p.target.id } };
                    AdvancedColor a = new AdvancedColor { header = new Header { type=9, size=(uint)Marshal.SizeOf(typeof(AdvancedColor)), adapter=p.target.adapter, id=p.target.id } };
                    if (DeviceInfo(ref s) != 0 || DeviceInfo(ref t) != 0) continue;
                    Match number = Regex.Match(s.name ?? "", @"DISPLAY(\d+)$", RegexOptions.IgnoreCase);
                    if (!number.Success) continue;
                    int colorRc = DeviceInfo(ref a);
                    int width=0, height=0, x=0, y=0;
                    if (p.source.modeIndex < modes) {
                        int offset = checked((int)p.source.modeIndex * 64);
                        if (Marshal.ReadInt32(modeBuffer, offset) == 1) {
                            width=Marshal.ReadInt32(modeBuffer,offset+16); height=Marshal.ReadInt32(modeBuffer,offset+20);
                            x=Marshal.ReadInt32(modeBuffer,offset+28); y=Marshal.ReadInt32(modeBuffer,offset+32);
                        }
                    }
                    if (p.target.rotation == 2 || p.target.rotation == 4) { int swap=width; width=height; height=swap; }
                    LiveMonitor monitor=new LiveMonitor { gdiName=s.name, monitorPath=t.path, friendlyName=t.name, ordinal=Int32.Parse(number.Groups[1].Value, CultureInfo.InvariantCulture), width=width, height=height, x=x, y=y, advancedColorKnown=colorRc==0, advancedColorEnabled=(a.value & 2)!=0, edidHash=EdidHash(t.path) };
                    IdentifyNvidia(monitor);
                    result.Add(monitor);
                }
                return result;
            } finally { Marshal.FreeHGlobal(modeBuffer); }
        }
        throw new Exception("Display topology did not settle; retry later.");
    }
    static string EdidHash(string path) {
        try {
            string[] parts = path.Split('#');
            if (parts.Length < 3) return null;
            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\DISPLAY\" + parts[1] + @"\" + parts[2] + @"\Device Parameters")) {
                byte[] edid = key == null ? null : key.GetValue("EDID") as byte[];
                if (edid == null || edid.Length < 128) return null;
                using (SHA256 hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(edid)).Replace("-", "").ToLowerInvariant();
            }
        } catch { return null; }
    }
    public static void SetTargetGamma(uint displayId,float[] values) {
        EnsureNv();
        TargetGamma ramp=new TargetGamma { version=(uint)Marshal.SizeOf(typeof(TargetGamma))|(1u<<16),values=values,flags=1 };
        if(Marshal.SizeOf(typeof(TargetGamma))!=12296) throw new Exception("Unexpected NVIDIA target-gamma structure size.");
        int rc=NvFunction<NvTargetGamma>(0x7082A053)(displayId,ref ramp);
        if(rc!=0) throw new Exception("NVIDIA target gamma restore failed for 0x"+displayId.ToString("X8")+": "+rc);
    }
    [DllImport("gdi32.dll", CharSet=CharSet.Unicode)] static extern IntPtr CreateDC(string driver, string device, string output, IntPtr init);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool GetDeviceGammaRamp(IntPtr dc, IntPtr ramp);
    [DllImport("gdi32.dll")] static extern bool SetDeviceGammaRamp(IntPtr dc, IntPtr ramp);
    public static ushort[] GetGdiRamp(string device) {
        IntPtr dc=CreateDC("DISPLAY",device,null,IntPtr.Zero);
        if (dc==IntPtr.Zero) throw new Exception("Cannot open display DC.");
        IntPtr buffer=Marshal.AllocHGlobal(1536);
        try {
            if(!GetDeviceGammaRamp(dc,buffer)) throw new Exception("Cannot read GDI gamma ramp.");
            short[] signed=new short[768]; Marshal.Copy(buffer,signed,0,768);
            return Array.ConvertAll(signed,v=>unchecked((ushort)v));
        } finally { Marshal.FreeHGlobal(buffer); DeleteDC(dc); }
    }
    public static void SetGdiRamp(string device,ushort[] values) {
        IntPtr dc=CreateDC("DISPLAY",device,null,IntPtr.Zero);
        if(dc==IntPtr.Zero) throw new Exception("Cannot open display DC for restore.");
        IntPtr buffer=Marshal.AllocHGlobal(1536);
        try {
            short[] signed=Array.ConvertAll(values,v=>unchecked((short)v)); Marshal.Copy(signed,0,buffer,768);
            if(!SetDeviceGammaRamp(dc,buffer)) throw new Exception("Windows rejected the captured GDI baseline.");
        } finally { Marshal.FreeHGlobal(buffer); DeleteDC(dc); }
    }
    [DllImport("user32.dll")] static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll")] static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern bool GetUserObjectInformation(IntPtr handle, int index, StringBuilder text, uint size, out uint needed);
    public static bool IsNormalDesktop() {
        IntPtr desktop = OpenInputDesktop(0, false, 1);
        if (desktop == IntPtr.Zero) return false;
        try { StringBuilder name = new StringBuilder(256); uint needed; return GetUserObjectInformation(desktop, 2, name, 512, out needed) && name.ToString() == "Default"; }
        finally { CloseDesktop(desktop); }
    }
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr window, StringBuilder name, int count);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr window,int index);
    public static bool IsForegroundFullscreen() {
        IntPtr window = GetForegroundWindow();
        if (window == IntPtr.Zero) return false;
        StringBuilder cls = new StringBuilder(128); GetClassName(window, cls, cls.Capacity);
        if (cls.ToString() == "Progman" || cls.ToString() == "WorkerW") return false;
        Rect rect; MonitorInfo info = new MonitorInfo { cb=(uint)Marshal.SizeOf(typeof(MonitorInfo)) };
        if (!GetWindowRect(window, out rect) || !GetMonitorInfo(MonitorFromWindow(window, 2), ref info)) return true;
        bool covers=rect.left <= info.monitor.left + 2 && rect.top <= info.monitor.top + 2 && rect.right >= info.monitor.right - 2 && rect.bottom >= info.monitor.bottom - 2;
        return covers && (GetWindowLongPtr(window,-16).ToInt64() & 0x00C40000L)==0;
    }
}
