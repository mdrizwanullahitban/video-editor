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
