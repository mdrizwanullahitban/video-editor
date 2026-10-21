using System.Diagnostics;
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
 bool rejected=false;try{await editor.Export(project,3,1,28,"#ffffff");}catch(InvalidOperationException){rejected=true;}Check(rejected,"invalid trim rejected");
 rejected=false;project.Segments[0].End=99;try{await editor.Export(project,0,3,28,"#ffffff");}catch(InvalidOperationException){rejected=true;}Check(rejected,"invalid caption timing rejected");
 rejected=false;try{await editor.Transcribe(project,"Hindi");}catch(InvalidOperationException ex){rejected=ex.Message.Contains("FRAME_AI_API_KEY");}Check(rejected,"missing AI credentials produce actionable message");
 project.Segments=[];await editor.Export(project,0,1,28,"#ffffff");Check(File.Exists(project.Output),"video export without subtitles");
} finally { File.Delete(fixture);if(project is not null)Directory.Delete(Path.GetDirectoryName(project.Source)!,true); }
sealed class BrowserFile(string path):IBrowserFile {
 public string Name=>Path.GetFileName(path);public DateTimeOffset LastModified=>DateTimeOffset.Now;public long Size=>new FileInfo(path).Length;public string ContentType=>"video/mp4";
 public Stream OpenReadStream(long maxAllowedSize=512000,CancellationToken cancellationToken=default)=>File.OpenRead(path);
}
