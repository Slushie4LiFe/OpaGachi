using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace OpaGachi {
 static class FactoryReset {
 public static void RegisterHookPath(string kind,string path) {
 string file=Path.Combine(Program.Data,"hook-paths.json"); Directory.CreateDirectory(Program.Data);
 var paths=Json.ReadFile(file) ?? new Dictionary<string,object>(); paths[kind+"|"+Path.GetFullPath(path)]=path; Json.WriteFile(file,paths);
 }
 public static void Run(string root,string data) {
 if (!File.Exists(Path.Combine(root,"assets/factory-agents.json"))) throw new IOException("Factory assets are missing. Reinstall OpaGachi before resetting.");
 RemoveHooks(root,data);
 ResetFiles(data);
 Log.Write("factory reset complete; restarting with original defaults");
 }
 // Remove only OpaGachi's entries, never other hooks or the user's unrelated agent configuration.
 public static void RemoveHooks(string root,string data) {
 // With OPAGACHI_HOME set (tests), nothing outside that folder is touched, wherever the code is checked out.
 string sandbox=Environment.GetEnvironmentVariable("OPAGACHI_HOME");
 Func<string,bool> allowed=p=>string.IsNullOrEmpty(sandbox)||Path.GetFullPath(p).StartsWith(Path.GetFullPath(sandbox).TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase);
 var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ClaudeCode.DefaultSettingsPath() };
 var registry=Json.ReadFile(Path.Combine(data,"hook-paths.json"));
 if(registry!=null) foreach(var entry in registry) {
 string path=entry.Value as string; if(path==null) continue;
 if(entry.Key.StartsWith("claude|")) paths.Add(path);
 else if(entry.Key.StartsWith("gemini|")&&allowed(path)) GeminiHooks.Uninstall(path);
 }
 for(var dir=new DirectoryInfo(root);dir!=null;dir=dir.Parent)
 foreach(var file in new[]{"settings.json","settings.local.json"}) { string path=Path.Combine(dir.FullName,Path.Combine(".claude",file)); if(File.Exists(path)) paths.Add(path); }
 foreach(var path in paths.Where(allowed)) { if(ClaudeCode.HooksInstalled(path)) ClaudeCode.UninstallHooks(path); if(StatusLineRelay.Installed(path)) StatusLineRelay.Uninstall(path); }
 CodexHooks.Uninstall(CodexHooks.DefaultPath());
 if(GeminiHooks.Installed(GeminiHooks.DefaultPath())) GeminiHooks.Uninstall(GeminiHooks.DefaultPath());
 GrokHooks.Uninstall();
 }
 // Everything the user changed lives in the data folder, and defaults ship with the app, so a reset empties
 // that folder (keeping the log). Separate from RemoveHooks so regression checks never touch real agent hooks.
 public static void ResetFiles(string data) {
 if(!string.Equals(Path.GetFileName(data.TrimEnd('\\','/')),"OpaGachi",StringComparison.OrdinalIgnoreCase)) throw new IOException("Not an OpaGachi data folder: "+data);
 if(!Directory.Exists(data)) return;
 foreach(var file in Directory.GetFiles(data)) if(!file.EndsWith(".log",StringComparison.OrdinalIgnoreCase)) File.Delete(file);
 foreach(var dir in Directory.GetDirectories(data)) Directory.Delete(dir,true);
 }
 }
}
