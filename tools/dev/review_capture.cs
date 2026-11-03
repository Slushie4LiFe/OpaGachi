using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using System.Windows.Forms;
namespace OpaGachi { static class ReviewCapture {
 static int checks;
 static BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
 static void Check(bool value,string label) { if(!value) throw new Exception(label); Console.WriteLine("PASS "+label);checks++; }
 static object Call(object target,string method,params object[] args) { return target.GetType().GetMethod(method,flags).Invoke(target,args); }
 static void Capture(Form form,string path) {
 typeof(Control).GetMethod("SetState",flags).Invoke(form,new object[]{2,true}); form.CreateControl(); form.PerformLayout();
 using(var bmp=new Bitmap(form.Width,form.Height)) {form.DrawToBitmap(bmp,new Rectangle(Point.Empty,bmp.Size));bmp.Save(path);}
 }
 [STAThread] static void Main() {
 string root = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\.."));   // this exe lives in tools\dev
 typeof(Program).GetProperty("Root").GetSetMethod(true).Invoke(null,new object[]{root});
 Application.EnableVisualStyles(); Theme.Apply(true,false,1); Theme.ReduceMotion=true;
 string scratch=Path.Combine(Path.GetTempPath(),"opagachi-tests\\"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(scratch);
 Directory.CreateDirectory(Path.Combine(root,"tools/dev/captures"));   // not in a fresh checkout
 // Every user-data write (custom sprites, uploads, hook registry) goes to scratch, never %LOCALAPPDATA%\OpaGachi.
 Action<string,string> set=(p,v)=>typeof(Program).GetProperty(p).GetSetMethod(true).Invoke(null,new object[]{v});
 set("Data",Path.Combine(scratch,"OpaGachi"));
 var settings=Settings.Load(Path.Combine(scratch,"settings.json"));   // shipped defaults
 var lib=new SpriteLibrary(Path.Combine(root,"assets/sprites"));
 using(var pets=new PetManager(settings,Settings.LoadAgents(Path.Combine(root,"assets/factory-agents.json")),Path.Combine(scratch,"agents.json"),lib,null)) {
 // Dark default, Light, and Dark at the Larger text size
 foreach(string v in new[]{"","-light","-larger"}) {
 Theme.Apply(v!="-light",false,v=="-larger"?1.3f:1);
 Func<string,string> cap=n=>Path.Combine(root,"tools/dev/captures/"+n+v+".png");
 using(var slots=new SpriteSlotsForm(pets)) Capture(slots,cap("slots"));
 using(var form=new SettingsForm(settings,pets)) { form.ShowPage(1);Capture(form,cap("agents")); }
 settings.CustomSprites=true;
 using(var form=new SettingsForm(settings,pets)) {form.ShowPage(4);form.OpenCategory(4);Capture(form,cap("experimental"));}
 using(var form=new SettingsForm(settings,pets)) {form.ShowPage(4);form.OpenCategory(3);Capture(form,cap("startup"));}
 settings.CustomSprites=false;
 using(var form=new SettingsForm(settings,pets)) {form.ShowPage(4);form.OpenCategory(5);Capture(form,cap("reset"));}
 using(var form=new SettingsForm(settings,pets)) {form.ShowPage(5);form.OpenCategory(1);Capture(form,cap("help"));}
 using(var confirm=new ConfirmForm("Are you sure you want to close this window?","Confirm"))Capture(confirm,cap("close-confirm"));
 using(var confirm=new ConfirmForm("For pets to work properly, OpaGachi needs to restart. Restart now?","Restart","Later"))Capture(confirm,cap("restart"));
 using(var confirm=new ConfirmForm("Hey, we noticed you don't have hooks installed for Claude Code and Codex. For OpaGachi to run its best, we recommend turning them on.","Open Connections","Later","Don't show this again"))Capture(confirm,cap("hook-reminder"));
 }
 Theme.Apply(true,false,1);
 // The popups' controls, operated offscreen: the reminder's switch and main button, the restart prompt's Later.
 using(var confirm=new ConfirmForm("q","Open Connections","Later","Don't show this again")) {
 typeof(Control).GetMethod("SetState",flags).Invoke(confirm,new object[]{2,true});confirm.CreateControl();
 ((ToggleSwitch)typeof(ConfirmForm).GetField("check",flags).GetValue(confirm)).Checked=true;
 typeof(Control).GetMethod("OnClick",flags).Invoke(confirm.Controls.OfType<FlatButton>().First(b=>b.Text=="Open Connections"),new object[]{EventArgs.Empty});
 Check(confirm.DialogResult==DialogResult.OK&&confirm.Checked,"Reminder: Open Connections with Don't show this again returns OK and the switch");
 }
 using(var confirm=new ConfirmForm("q","Restart","Later")) {
 typeof(Control).GetMethod("SetState",flags).Invoke(confirm,new object[]{2,true});confirm.CreateControl();
 typeof(Control).GetMethod("OnClick",flags).Invoke(confirm.Controls.OfType<FlatButton>().First(b=>b.Text=="Later"),new object[]{EventArgs.Empty});
 Check(confirm.DialogResult==DialogResult.Cancel&&!confirm.Checked,"Restart prompt: Later cancels");
 }
 }
 // Local server: what a web page could send (an Origin header, or its own Host after DNS rebinding) is refused.
 var server=new EventServer();string serverError;int port=0;
 for(int attempt=0;attempt<5&&port==0;attempt++){int p=new Random().Next(47400,47900);if(server.Start(p,out serverError))port=p;}
 Check(port!=0,"Event server starts on a test port");
 Func<string,string> raw=request=>{using(var c=new System.Net.Sockets.TcpClient("127.0.0.1",port)){var s=c.GetStream();var b=System.Text.Encoding.ASCII.GetBytes(request);s.Write(b,0,b.Length);var r=new byte[64];int n=s.Read(r,0,r.Length);return System.Text.Encoding.ASCII.GetString(r,0,n);}};
 string evil="{\"snapshot\":\"C:/Users/Public/x.png\"}";
 Check(raw("POST /signal HTTP/1.1\r\nHost: 127.0.0.1:"+port+"\r\nOrigin: https://evil.example\r\nContent-Type: text/plain\r\nContent-Length: "+evil.Length+"\r\n\r\n"+evil).Contains(" 403 "),"A web page's request (Origin header) is refused");
 Check(raw("GET /state HTTP/1.1\r\nHost: evil.example:"+port+"\r\n\r\n").Contains(" 403 "),"A DNS-rebinding request (foreign Host) is refused");
 Check(server.Queue.Count==0,"Refused requests never reach the pets");
 bool posted=EventServer.Post(port,"/claude-code","{\"hook_event_name\":\"Stop\"}",2000);
 for(int wait=0;wait<40&&server.Queue.Count==0;wait++) System.Threading.Thread.Sleep(50);   // the server replies first, then queues
 Check(posted&&server.Queue.Count==1,"Hook requests from local programs still get in");
 server.Stop();
 bool closed;
 var body=Json.Parse("{\"sessionId\":\"test\",\"hookEventName\":\"stop\"}");
 Check(GrokHooks.Map(body,false,out closed).Contains(Signal.Done),"Grok lowercase stop completes");
 body["hookEventName"]="session_end";GrokHooks.Map(body,false,out closed);Check(closed,"Grok lowercase session_end closes");
 body["hookEventName"]="user_prompt_submit";Check(GrokHooks.Map(body,false,out closed).Contains(Signal.UserPrompted),"Grok snake_case prompt starts");
 Check(GrokHooks.IsGrokPayload(Json.Parse("{\"hook_event_name\":\"stop\",\"session_id\":\"grok\"}")),"Grok snake payload cannot become Claude");
 Check(!ClaudeSessions.IsLive("nonexistent-test-session"),"Unbacked Claude hook sessions rejected");
 string clips=Path.Combine(scratch,"sprites");
 for(int i=1;i<=5;i++) {
 string png=Path.Combine(scratch,"IDLE_"+i+".png");using(var bmp=new Bitmap(8,8)){bmp.SetPixel(i,2,Color.Red);bmp.Save(png);}
 Check(CustomSprite.ImportSlot(png,clips,8,8,Anim.Idle)>0,"PNG slot "+i+" imports");
 }
 CustomSprite.ImportSlot(Path.Combine(scratch,"IDLE_1.png"),clips,8,8,Anim.Idle);
 Check(Directory.GetFiles(Path.Combine(CustomSprite.Folder,"idle"),"*.opaclip").Length==5&&CustomSprite.Folder.StartsWith(Path.Combine(scratch,"OpaGachi")),"Custom slots land in the data folder; replacing keeps five-file limit");
 bool blocked=false;try{CustomSprite.ImportSlot(Path.Combine(scratch,"IDLE_6.png"),clips,8,8,Anim.Idle);}catch(InvalidDataException){blocked=true;}Check(blocked,"Slot six rejected");
 blocked=false;try{CustomSprite.ImportSlot(Path.Combine(scratch,"WORK_1.webm"),clips,8,8,Anim.Work);}catch(InvalidDataException){blocked=true;}Check(blocked,"Global PNG lock rejects WebM in another action");
 File.WriteAllText(Path.Combine(CustomSprite.Folder,"format.txt"),".webm");
 blocked=false;try{CustomSprite.ImportSlot(Path.Combine(scratch,"IDLE_1.png"),clips,8,8,Anim.Idle);}catch(InvalidDataException){blocked=true;}Check(blocked,"Global WebM lock rejects PNG");
 var watcher=new CodexWatcher();
 string rollout=Path.Combine(scratch,"rollout.jsonl");
 File.WriteAllText(rollout,"{\"type\":\"session_meta\",\"payload\":{\"originator\":\"Codex Desktop\"}}\n{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\"}}\n");
 Call(watcher,"Track",rollout,true);
 var tracked=(IDictionary)typeof(CodexWatcher).GetField("tracked",flags).GetValue(watcher);var record=tracked[rollout];
 Check((bool)record.GetType().GetField("Busy").GetValue(record),"Codex startup reconstructs active turn");
 Call(watcher,"Handle",record,"{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_complete\"}}");
 Check(!(bool)record.GetType().GetField("Busy").GetValue(record),"Codex completion clears busy");
 string guardian=Path.Combine(scratch,"guardian.jsonl");File.WriteAllText(guardian,"{\"type\":\"session_meta\",\"payload\":{\"originator\":\"Codex Desktop\",\"thread_source\":\"guardian_review\"}}\n");Call(watcher,"Track",guardian,true);Check(tracked.Count==1,"Guardian review threads excluded from pet activity");
 ((JsonlTail)record.GetType().GetField("Tail").GetValue(record)).Dispose();
 string reset=Path.Combine(scratch,"reset/OpaGachi");
 foreach(var d in new[]{"sprites/custom/idle","sounds","ties"}) Directory.CreateDirectory(Path.Combine(reset,d));
 foreach(var f in new[]{"settings.json","agents.json","hook-paths.json","sprites/custom/idle/IDLE_1.opaclip","sounds/upload.wav","ties/claude-custom.png","opagachi.log"}) File.WriteAllText(Path.Combine(reset,f),"x");
 FactoryReset.ResetFiles(reset);
 Check(Directory.GetFileSystemEntries(reset).Select(Path.GetFileName).SequenceEqual(new[]{"opagachi.log"}),"Reset empties the data folder except the log");
 blocked=false;try{FactoryReset.ResetFiles(Path.Combine(scratch,"reset"));}catch(IOException){blocked=true;}Check(blocked&&File.Exists(Path.Combine(reset,"opagachi.log")),"Reset refuses a folder not named OpaGachi");
 Check(Settings.Load(Path.Combine(reset,"settings.json")).CustomSprites==false,"Reset settings return to defaults");
 // One-time copy of pre-1.0 data from the app folder; a second start must not overwrite newer data.
 string legacy=Path.Combine(scratch,"legacy"),moved=Path.Combine(scratch,"moved/OpaGachi");
 Directory.CreateDirectory(Path.Combine(legacy,"assets/sprites/custom/idle"));File.WriteAllText(Path.Combine(legacy,"settings.json"),"{\"textSize\":2}");File.WriteAllText(Path.Combine(legacy,"assets/agents.json"),"{}");File.WriteAllText(Path.Combine(legacy,"assets/sprites/custom/idle/IDLE_1.opaclip"),"clip");
 set("Root",legacy);set("Data",moved);var copyLegacy=typeof(Program).GetMethod("CopyLegacyData",BindingFlags.Static|BindingFlags.NonPublic);copyLegacy.Invoke(null,null);
 Check(Settings.Load(Path.Combine(moved,"settings.json")).TextSize==2&&File.Exists(Path.Combine(moved,"agents.json"))&&File.Exists(Path.Combine(moved,"sprites/custom/idle/IDLE_1.opaclip")),"Pre-1.0 settings, agents and custom sprites copied to the data folder");
 File.WriteAllText(Path.Combine(legacy,"settings.json"),"{\"textSize\":0}");copyLegacy.Invoke(null,null);
 Check(Settings.Load(Path.Combine(moved,"settings.json")).TextSize==2,"Legacy copy runs only once");
 // Full FactoryReset.Run on fixture hooks: OPAGACHI_HOME and Root point into scratch, so real hooks are never read or written.
 string home=Path.Combine(scratch,"home"),runRoot=Path.Combine(scratch,"run"),exe=Path.Combine(runRoot,"bin/OpaGachi.exe");
 string outside=Path.Combine(scratch,".claude/settings.json");   // a parent folder of runRoot, outside OPAGACHI_HOME
 Environment.SetEnvironmentVariable("OPAGACHI_HOME",home);Environment.SetEnvironmentVariable("CODEX_HOME",null);
 string runData=Path.Combine(home,"AppData","Local","OpaGachi");
 set("Root",runRoot);set("Data",runData);
 Directory.CreateDirectory(Path.Combine(runRoot,"assets"));File.Copy(Path.Combine(root,"assets/factory-agents.json"),Path.Combine(runRoot,"assets/factory-agents.json"));
 string claude=ClaudeCode.DefaultSettingsPath(),custom=Path.Combine(home,"project/.claude/settings.local.json");
 Check(claude.StartsWith(home)&&CodexHooks.DefaultPath().StartsWith(home)&&GeminiHooks.Dir.StartsWith(home)&&GrokHooks.FilePath.StartsWith(home),"OPAGACHI_HOME redirects every agent's hook file");
 Check((string)typeof(Program).GetMethod("FindData",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null)==runData,"OPAGACHI_HOME redirects the data folder");
 Directory.CreateDirectory(Path.GetDirectoryName(claude));Directory.CreateDirectory(Path.Combine(home,".codex/sessions"));
 Check(Integrations.MissingHooks().SequenceEqual(new[]{"Claude Code","Codex"}),"Reminder names installed agents without hooks (Claude Code, Codex), not absent ones");
 // Usage page: Grok's session ledgers (9-digit fractions as Grok writes them; a forked copy counts once) and the answer log.
 string ledger="{\"sessionId\":\"s\",\"turns\":[{\"turnNumber\":1,\"endedAt\":\""+DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffffff")+"00+00:00\",\"totalTokens\":1000,\"costUsdTicks\":200000000},"
  +"{\"turnNumber\":2,\"endedAt\":\""+DateTime.UtcNow.AddDays(-2).ToString("yyyy-MM-ddTHH:mm:ss")+"+00:00\",\"totalTokens\":500,\"costUsdTicks\":100000000}]}";
 foreach(var session in new[]{"one","fork"}){Directory.CreateDirectory(Path.Combine(home,".grok/sessions/x/"+session));File.WriteAllText(Path.Combine(home,".grok/sessions/x/"+session+"/usage.json"),ledger);}
 typeof(UsageHistory).GetMethod("Scan",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
 var grokDays=UsageHistory.Grok;
 Check(grokDays[grokDays.Length-1]==1000&&grokDays[grokDays.Length-3]==500&&UsageHistory.GrokCost.Sum()==300000000,"Grok usage: tokens per day and cost from session ledgers, a forked copy counted once");
 Check(UsageText.Dollars(UsageHistory.GrokCost.Sum())=="$0.03","Grok cost shows in dollars");
 AnswerLog.Record("gemini");AnswerLog.Record("gemini");
 typeof(AnswerLog).GetField("agents",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,null);   // forget, as after a restart
 Check(AnswerLog.Series("gemini").Last()==2&&File.Exists(Path.Combine(runData,"answers.json")),"Gemini answers are counted per day and kept across restarts");
 using(var host=new Form{FormBorderStyle=FormBorderStyle.None,BackColor=Theme.Surface,ClientSize=new Size(680,10)}) {
 var panel=new UsagePanel{Width=680,Location=Point.Empty};host.Controls.Add(panel);panel.UpdateFrom(null);panel.PerformLayout();host.ClientSize=new Size(680,panel.Height);
 Capture(host,Path.Combine(root,"tools/dev/captures/usage-all.png"));
 }
 File.WriteAllText(claude,"{\"hooks\":{\"Stop\":[{\"hooks\":[{\"type\":\"command\",\"command\":\"echo keep\"}]}]},\"statusLine\":{\"type\":\"command\",\"command\":\"my-status\"}}");
 ClaudeCode.InstallHooks(claude,47321,exe);StatusLineRelay.Install(claude,exe);ClaudeCode.InstallHooks(custom,47321,exe);ClaudeCode.InstallHooks(outside,47321,exe);
 CodexHooks.Install(CodexHooks.DefaultPath(),exe);GeminiHooks.Install(GeminiHooks.DefaultPath(),exe);GrokHooks.Install(47321);
 string legacyGrok=Path.Combine(GrokHooks.Dir,"hooks/opa-pet.json");File.WriteAllText(legacyGrok,"{}");
 Check(ClaudeCode.HooksInstalled(claude)&&StatusLineRelay.Installed(claude)&&ClaudeCode.HooksInstalled(custom)&&CodexHooks.Installed(CodexHooks.DefaultPath())&&GeminiHooks.Installed(GeminiHooks.DefaultPath())&&GrokHooks.Installed&&File.Exists(Path.Combine(runData,"hook-paths.json")),"Fixture hooks installed for every agent and registered in the data folder");
 Check(Integrations.MissingHooks().Count==0,"No reminder once every installed agent has hooks");
 FactoryReset.Run(runRoot,runData);
 string after=File.ReadAllText(claude);
 Check(!ClaudeCode.HooksInstalled(claude)&&!StatusLineRelay.Installed(claude)&&after.Contains("echo keep")&&after.Contains("my-status"),"Reset removes Claude hooks and relay, keeps the user's hook and status line");
 Check(!ClaudeCode.HooksInstalled(custom),"Reset cleans a registered custom Claude path");
 Check(!CodexHooks.Installed(CodexHooks.DefaultPath())&&!GeminiHooks.Installed(GeminiHooks.DefaultPath())&&!GrokHooks.Installed&&!File.Exists(legacyGrok),"Reset removes Codex, Gemini and Grok hooks, including the pre-rename Grok file");
 Check(!File.Exists(Path.Combine(runData,"hook-paths.json")),"Reset clears the hook registry");
 Check(Integrations.MissingHooks().SequenceEqual(new[]{"Claude Code","Codex","Gemini CLI","Grok Build"}),"Reminder lists every agent after hooks are removed");
 // Run at startup writes the real per-user Run value: only tested when there is none, and always removed again.
 if(!Startup.Enabled) {
 try { Startup.Set(true,false);Check(Startup.Enabled&&!Startup.Windowed,"Run at startup: minimized");Startup.Set(true,true);Check(Startup.Windowed,"Run at startup: Settings window");Startup.Set(false,false);Check(!Startup.Enabled,"Run at startup: off removes the entry"); }
 finally { Startup.Set(false,false); }
 }
 Check(ClaudeCode.HooksInstalled(outside),"With OPAGACHI_HOME set, hooks outside it are never touched (even registered ones in parent folders)");
 set("Root",root);set("Data",Path.Combine(scratch,"webm/OpaGachi"));Environment.SetEnvironmentVariable("OPAGACHI_HOME",null);
 string fixture=Path.Combine(root,"tools/dev/fixtures");
 Check(CustomSprite.ImportSlot(Path.Combine(fixture,"IDLE_1.webm"),Path.Combine(scratch,"webm"),32,32,Anim.Idle)>0,"Alpha WebM decodes through hidden Edge");
 blocked=false;try{CustomSprite.ImportSlot(Path.Combine(fixture,"WORK_1.webm"),Path.Combine(scratch,"opaque"),32,32,Anim.Work);}catch(InvalidDataException){blocked=true;}Check(blocked,"Opaque WebM rejected before padded output can fake alpha");
 Theme.ReduceMotion=false;
 foreach(string agent in new[]{"claude","chatgpt","gemini","grok"}) {
 var fx=new Effects(agent,new Random(1)){Style="Gold",Trigger="On Click"};fx.Interaction(false,0);Check(!fx.Alive,agent+" hover suppressed in click-only mode");fx.Interaction(true,0);Check(fx.Alive,agent+" click sparkles enabled");
 }
 Check(SparklePalette.Names.Length==24,"All 24 sparkle colour choices provided");
 using(var grid=new Bitmap(800,320)) using(var g=Graphics.FromImage(grid)) {
 g.Clear(Color.FromArgb(22,24,30));g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
 for(int i=0;i<4;i++) {var fx=new Effects(new[]{"claude","chatgpt","gemini","grok"}[i],new Random(2)){Style="Pearlescent"};fx.Burst(FxKind.Complete,0);fx.Step(0);fx.Step(0.2);fx.Draw(g,new RectangleF(i*200,70,147,160));g.DrawString(new[]{"Claude","ChatGPT","Gemini","Grok"}[i],SystemFonts.DefaultFont,Brushes.White,i*200+40,270);}
 grid.Save(Path.Combine(root,"tools/dev/captures/sparkles.png"));
 }
 Console.WriteLine("TOTAL "+checks+" checks passed; scratch "+scratch);
 }
} }

