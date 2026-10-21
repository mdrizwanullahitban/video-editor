using System.Diagnostics;
using System.Buffers.Binary;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VideoEditor;
static async Task<string> Command(string name, params string[] args) {
 var info=new ProcessStartInfo(name){RedirectStandardOutput=true,RedirectStandardError=true}; foreach(var a in args)info.ArgumentList.Add(a);
 using var p=Process.Start(info)!; var output=p.StandardOutput.ReadToEndAsync(); var errors=p.StandardError.ReadToEndAsync(); await p.WaitForExitAsync(); if(p.ExitCode!=0)throw new Exception(await errors); return await output;
}
static void Check(bool result,string name){if(!result)throw new Exception("FAILED: "+name);Console.WriteLine("PASS: "+name);}
if(args.Contains("--web-assets-only")||args.Contains("--cloud-auth-only")) {
 var authCheck=args.Contains("--cloud-auth-only");
 var directory=new DirectoryInfo(AppContext.BaseDirectory);
 while(directory is not null && !File.Exists(Path.Combine(directory.FullName,"VideoEditor.csproj"))) directory=directory.Parent;
 if(directory is null)throw new Exception("Cannot locate application checkout");
 var listener=new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback,0);listener.Start();var port=((System.Net.IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
 var start=new ProcessStartInfo(Environment.ProcessPath!){WorkingDirectory=directory.FullName,RedirectStandardOutput=true,RedirectStandardError=true};
 // The checks executable can be launched through dotnet or its native apphost.
 start.FileName=Path.Combine(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(),"..","..","..",OperatingSystem.IsWindows()?"dotnet.exe":"dotnet");
 var configuration=AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar+"Release"+Path.DirectorySeparatorChar)?"Release":"Debug";
 start.ArgumentList.Add(Path.Combine(directory.FullName,"bin",configuration,"net10.0","VideoEditor.dll"));start.ArgumentList.Add("--urls");start.ArgumentList.Add($"http://127.0.0.1:{port}");
 start.Environment["ASPNETCORE_ENVIRONMENT"]="Production";start.Environment["DOTNET_ENVIRONMENT"]="Production";
 if(authCheck) {
  start.Environment["FRAME_CLOUD_MODE"]="true";start.Environment["FRAME_ADMIN_PASSWORD"]="test-only-owner-password";start.Environment["FRAME_AI_API_KEY"]="test-only-placeholder";
  start.Environment["R2_ACCOUNT_ID"]=new string('a',32);start.Environment["R2_BUCKET_NAME"]="test-bucket";start.Environment["R2_ACCESS_KEY_ID"]="test-only-placeholder";start.Environment["R2_SECRET_ACCESS_KEY"]="test-only-placeholder";
 }
 using var server=Process.Start(start)!;var logs=server.StandardOutput.ReadToEndAsync();var errors=server.StandardError.ReadToEndAsync();
 try {
  using var client=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){BaseAddress=new Uri($"http://127.0.0.1:{port}"),Timeout=TimeSpan.FromSeconds(2)};bool ready=false;
  for(var attempt=0;attempt<100;attempt++) {
   if(server.HasExited)throw new Exception("Application exited: "+await errors);
   try {using var response=await client.GetAsync("/health");if(response.IsSuccessStatusCode){ready=true;break;}}catch(HttpRequestException){}catch(TaskCanceledException){}
   await Task.Delay(100);
  }
  Check(ready,"application starts in Production");
  using var script=await client.GetAsync("/_framework/blazor.web.js");Check(script.IsSuccessStatusCode && (await script.Content.ReadAsStringAsync()).Length>1000,"Blazor framework script available in Production");
  if(authCheck) {
   using var blocked=await client.GetAsync("/media/00000000-0000-0000-0000-000000000000");Check(blocked.StatusCode==System.Net.HttpStatusCode.Redirect && blocked.Headers.Location!.ToString().Contains("/login"),"private media requires sign-in");
   using var home=await client.GetAsync("/");Check(home.StatusCode==System.Net.HttpStatusCode.Redirect,"editor requires sign-in");
   using var csrf=await client.PostAsync("/login",new FormUrlEncodedContent(new Dictionary<string,string>{{"password","test-only-owner-password"}}));Check(csrf.StatusCode==System.Net.HttpStatusCode.BadRequest,"login rejects missing antiforgery token");
   var login=await client.GetStringAsync("/login");
   var token=System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Match(login,"name=\"__RequestVerificationToken\" value=\"([^\"]+)\"").Groups[1].Value);
   using var denied=await client.PostAsync("/login",new FormUrlEncodedContent(new Dictionary<string,string>{{"password","incorrect-password"},{"__RequestVerificationToken",token}}));Check(denied.StatusCode==System.Net.HttpStatusCode.Unauthorized,"wrong workspace password rejected");
   using var signedIn=await client.PostAsync("/login",new FormUrlEncodedContent(new Dictionary<string,string>{{"password","test-only-owner-password"},{"__RequestVerificationToken",token}}));
   Check(signedIn.StatusCode==System.Net.HttpStatusCode.Redirect,"owner can sign in");
   var cookie=signedIn.Headers.GetValues("Set-Cookie").Single(x=>x.StartsWith("frame.session="));Check(cookie.Contains("secure",StringComparison.OrdinalIgnoreCase)&&cookie.Contains("httponly",StringComparison.OrdinalIgnoreCase),"session cookie requires HTTPS and blocks JavaScript access");
   using var authenticated=new HttpRequestMessage(HttpMethod.Get,"/media/00000000-0000-0000-0000-000000000000");authenticated.Headers.Add("Cookie",cookie.Split(';')[0]);
   using var permitted=await client.SendAsync(authenticated);Check(permitted.StatusCode==System.Net.HttpStatusCode.NotFound,"authenticated owner reaches private media endpoint");
   System.Net.HttpStatusCode last=0;for(var i=0;i<4;i++){using var throttled=await client.PostAsync("/login",new FormUrlEncodedContent(new Dictionary<string,string>{{"password","incorrect-password"},{"__RequestVerificationToken",token}}));last=throttled.StatusCode;}
   Check(last==System.Net.HttpStatusCode.TooManyRequests,"repeated sign-in attempts are rate limited");
  } else {
  var html=await client.GetStringAsync("/");Check(html.Contains("Generate subtitles") && html.Contains("blazor.web"),"interactive editor page references framework script");
  }
 } finally {if(!server.HasExited)server.Kill(true);await server.WaitForExitAsync();await logs;await errors;}
 return;
}
var fixture=Path.Combine(Path.GetTempPath(),"frame-test-"+Guid.NewGuid()+".mp4");
await Command("ffmpeg","-y","-f","lavfi","-i","color=c=blue:s=320x240:r=25:d=4","-f","lavfi","-i","sine=frequency=440:duration=4","-c:v","libx264","-threads","2","-c:a","aac","-shortest",fixture);
var services=new ServiceCollection().AddHttpClient().BuildServiceProvider();
var editor=new EditorService(services.GetRequiredService<IHttpClientFactory>(),new ConfigurationBuilder().Build());
Project? project=null;
try {
 var oversized=new BrowserFile(fixture,EditorService.MaxUploadBytes+1);
 bool sizeRejected=false; try {await editor.Upload(oversized);} catch(InvalidOperationException ex){sizeRejected=ex.Message.Contains("1 GB");}
 Check(sizeRejected && !oversized.Opened,"1 GB plus one byte rejected before reading");
 using(var cancelled=new CancellationTokenSource()) {
  cancelled.Cancel(); bool cancellationRejected=false;
  try {await editor.Upload(new BrowserFile(fixture), cancellationToken:cancelled.Token);} catch(OperationCanceledException){cancellationRejected=true;}
  Check(cancellationRejected,"cancelled upload stops streaming");
 }
 var largeFixture=fixture+".large.mp4";
 Project? large=null;
 try {
  File.Copy(fixture,largeFixture);
  using(var padded=File.Open(largeFixture,FileMode.Open,FileAccess.Write)) {
   var originalLength=padded.Length; padded.Position=originalLength;
   var header=new byte[8]; BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0,4),(uint)(EditorService.MaxUploadBytes-originalLength));
   "free"u8.CopyTo(header.AsSpan(4)); padded.Write(header); padded.SetLength(EditorService.MaxUploadBytes);
  }
  var file=new BrowserFile(largeFixture); long uploaded=0;
  large=await editor.Upload(file,bytes=>uploaded=bytes);
  Check(file.RequestedLimit==EditorService.MaxUploadBytes && uploaded==EditorService.MaxUploadBytes && new FileInfo(large.Source).Length==EditorService.MaxUploadBytes,"actual 1 GB video streamed to disk with progress");
  Check(Math.Abs(large.Duration-4)<.2,"1 GB video successfully probed");
  await editor.Export(large,0,1,28,"#ffffff"); Check(File.Exists(large.Output),"1 GB source successfully trimmed and exported");
 } finally { File.Delete(largeFixture); if(large is not null)Directory.Delete(Path.GetDirectoryName(large.Source)!,true); }
 project=await editor.Upload(new BrowserFile(fixture)); Check(Math.Abs(project.Duration-4)<0.2,"upload and actual ffprobe duration");
 project.Segments=[new(){Start=0,End=2,Text="Hello नमस्ते"},new(){Start=2.5,End=3.5,Text="Aap kaise ho?"}];
 await editor.Export(project,1,3,28,"#ffffff");
 var folder=Path.GetDirectoryName(project.Output!)!;
 var srt=await File.ReadAllTextAsync(Path.Combine(folder,"export.srt")); var vtt=await File.ReadAllTextAsync(Path.Combine(folder,"export.vtt"));
 Check(srt.Contains("00:00:00,000 --> 00:00:01,000")&&srt.Contains("00:00:01,500 --> 00:00:02,000"),"captions clipped and shifted after trim");
 Check(vtt.StartsWith("WEBVTT")&&vtt.Contains("00:00:01.500 --> 00:00:02.000"),"WebVTT timing format");
 using var probe=JsonDocument.Parse(await Command("ffprobe","-v","error","-show_entries","format=duration:stream=codec_type","-of","json",project.Output!));
 Check(Math.Abs(double.Parse(probe.RootElement.GetProperty("format").GetProperty("duration").GetString()!,System.Globalization.CultureInfo.InvariantCulture)-2)<.2,"export duration equals selected trim");
 Check(probe.RootElement.GetProperty("streams").EnumerateArray().Count()==2,"export retains video and audio streams");
 var cloudStore=new MemoryCloud();
 var cloudEditor=new EditorService(services.GetRequiredService<IHttpClientFactory>(),new ConfigurationBuilder().Build(),cloudStore);
 var persisted=await cloudEditor.Upload(new BrowserFile(fixture));
 try {
  persisted.Segments=[new(){Start=0,End=1,Text="Saved cloud caption"}];await cloudEditor.SaveProject(persisted);
  Directory.Delete(Path.GetDirectoryName(persisted.Source)!,true);
  var restarted=new EditorService(services.GetRequiredService<IHttpClientFactory>(),new ConfigurationBuilder().Build(),cloudStore);
  var records=await restarted.ListProjects();Check(records.Count==1&&records[0].Id==persisted.Id,"cloud library lists persisted project metadata (mock object store)");
  var restored=await restarted.OpenProject(persisted.Id);Check(File.Exists(restored.Source)&&restored.Segments[0].Text=="Saved cloud caption","project and source restore after service restart and local deletion (mock object store)");
  await restarted.Export(restored,0,1,28,"#ffffff");Check(cloudStore.Exported&&cloudStore.Record!.HasExport,"cloud export uploads video/subtitles and saves metadata (mock object store)");
  Check(await restarted.Download(restored.Id,"mp4") is Microsoft.AspNetCore.Http.HttpResults.RedirectHttpResult redirect&&redirect.Url=="https://storage.example.test/signed-video","private download uses expiring storage URL (mock object store)");
  await restarted.DeleteProject(restored.Id);Check((await restarted.ListProjects()).Count==0&&!File.Exists(restored.Source),"project deletion removes cloud metadata and local cached media (mock object store)");
 } finally {if(Directory.Exists(Path.GetDirectoryName(persisted.Source)!))Directory.Delete(Path.GetDirectoryName(persisted.Source)!,true);}
 var aiStages=new List<string>();
 foreach(var model in new[]{"gpt-4o-transcribe-diarize","whisper-1"}) {
  var handler=new SubtitleHandler(model);
  var ai=new EditorService(new TestClients(handler),new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["FRAME_AI_API_KEY"]="test-only-placeholder",["FRAME_TRANSCRIPTION_MODEL"]=model}).Build());
  await ai.Transcribe(project,"Hinglish",aiStages.Add);
  Check(handler.Calls==2 && project.Segments.Count==1 && project.Segments[0].Text=="Namaste dosto" && project.Segments[0].Start==0 && project.Segments[0].End==1,$"{model} request format and translated caption timestamps (mock API)");
 }
 Check(aiStages.Any(x=>x.Contains("Preparing audio"))&&aiStages.Any(x=>x.Contains("Recognizing speech"))&&aiStages.Any(x=>x.Contains("Translating")),"AI stages report extraction, recognition, and translation");
 bool rejected=false;try{await editor.Export(project,3,1,28,"#ffffff");}catch(InvalidOperationException){rejected=true;}Check(rejected,"invalid trim rejected");
 rejected=false;project.Segments[0].End=99;try{await editor.Export(project,0,3,28,"#ffffff");}catch(InvalidOperationException){rejected=true;}Check(rejected,"invalid caption timing rejected");
 rejected=false;try{await editor.Transcribe(project,"Hindi");}catch(InvalidOperationException ex){rejected=ex.Message.Contains("FRAME_AI_API_KEY");}Check(rejected,"missing AI credentials produce actionable message");
 project.Segments=[];await editor.Export(project,0,1,28,"#ffffff");Check(File.Exists(project.Output),"video export without subtitles");
} finally { File.Delete(fixture);if(project is not null)Directory.Delete(Path.GetDirectoryName(project.Source)!,true); }
sealed class BrowserFile(string path,long? reportedSize=null):IBrowserFile {
 public bool Opened {get;private set;} public long RequestedLimit {get;private set;}
 public string Name=>Path.GetFileName(path);public DateTimeOffset LastModified=>DateTimeOffset.Now;public long Size=>reportedSize??new FileInfo(path).Length;public string ContentType=>"video/mp4";
 public Stream OpenReadStream(long maxAllowedSize=512000,CancellationToken cancellationToken=default){Opened=true;RequestedLimit=maxAllowedSize;cancellationToken.ThrowIfCancellationRequested();if(Size>maxAllowedSize)throw new IOException("Too large");return File.OpenRead(path);}
}

sealed class TestClients(HttpMessageHandler handler):IHttpClientFactory {
 public HttpClient CreateClient(string name)=>new(handler,disposeHandler:false);
}
sealed class SubtitleHandler(string model):HttpMessageHandler {
 public int Calls {get;private set;}
 protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken) {
  Calls++;
  if(request.RequestUri!.AbsolutePath.EndsWith("transcriptions")) {
   var form=(MultipartFormDataContent)request.Content!;
   var fields=new Dictionary<string,string>();
   foreach(var part in form) if(part.Headers.ContentDisposition!.FileName is null)fields[part.Headers.ContentDisposition.Name!.Trim('"')]=await part.ReadAsStringAsync(cancellationToken);
   if(fields["model"]!=model)throw new Exception("Wrong transcription model");
   if(model=="whisper-1") {if(fields["response_format"]!="verbose_json"||fields["timestamp_granularities[]"]!="segment")throw new Exception("Wrong legacy timestamp format");}
   else if(fields["response_format"]!="diarized_json"||fields["chunking_strategy"]!="auto"||fields.ContainsKey("timestamp_granularities[]"))throw new Exception("Wrong diarization format");
   return new(System.Net.HttpStatusCode.OK){Content=new StringContent("{\"segments\":[{\"start\":0,\"end\":1,\"text\":\"नमस्ते दोस्तों\",\"speaker\":\"A\"}]}")};
  }
  using var payload=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
  if(payload.RootElement.GetProperty("model").GetString()!="gpt-4.1")throw new Exception("Wrong translation model");
  var content=JsonSerializer.Serialize(new{choices=new[]{new{message=new{content=JsonSerializer.Serialize(new{texts=new[]{"Namaste dosto"}})}}}});
  return new(System.Net.HttpStatusCode.OK){Content=new StringContent(content)};
 }
}

sealed class MemoryCloud:ICloudProjectStore {
 public Task Delete(Guid id){if(Record?.Id==id){Record=null;source=[];}return Task.CompletedTask;}
 public bool Enabled=>true;public ProjectRecord? Record;byte[] source=[];public bool Exported;
 public async Task SaveOriginal(Project p){source=await File.ReadAllBytesAsync(p.Source);await SaveProject(p);}
 public Task SaveProject(Project p){Record=new(p.Id,p.Name,p.Duration,Path.GetExtension(p.Source),p.Segments.Select(x=>new Segment{Start=x.Start,End=x.End,Text=x.Text}).ToList(),p.Output is not null,new FileInfo(p.Source).Length);return Task.CompletedTask;}
 public async Task SaveExports(Project p){Exported=File.Exists(p.Output)&&File.Exists(Path.ChangeExtension(p.Output,"srt"))&&File.Exists(Path.ChangeExtension(p.Output,"vtt"));await SaveProject(p);}
 public Task<List<ProjectRecord>> List()=>Task.FromResult(Record is null?new List<ProjectRecord>():[Record]);
 public Task<ProjectRecord?> Find(Guid id)=>Task.FromResult(Record?.Id==id?Record:null);
 public Task RestoreOriginal(ProjectRecord record,string destination)=>File.WriteAllBytesAsync(destination,source);
 public Task<string?> DownloadUrl(Guid id,string format)=>Task.FromResult<string?>("https://storage.example.test/signed-video");
}
