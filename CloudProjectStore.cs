using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using System.Text.Json;
namespace VideoEditor;

public sealed record ProjectRecord(Guid Id, string Name, double Duration, string Extension, List<Segment> Segments, bool HasExport, long SourceBytes, double TrimStart=0, double? TrimEnd=null, int FontSize=28, string Color="#ffffff", string? ExportVersion=null);
public interface ICloudProjectStore {
 bool Enabled {get;}
 Task SaveOriginal(Project project);
 Task SaveProject(Project project);
 Task SaveExports(Project project);
 Task<List<ProjectRecord>> List();
 Task<ProjectRecord?> Find(Guid id);
 Task RestoreOriginal(ProjectRecord record, string destination);
 Task<string?> DownloadUrl(Guid id, string format);
 Task Delete(Guid id);
}
public sealed class R2ProjectStore : ICloudProjectStore, IDisposable {
 readonly IAmazonS3? client; readonly string bucket; readonly long quota;
 public bool Enabled=>client is not null;
 static string Key(Guid id,string file)=>$"projects/{id}/{file}";
 public R2ProjectStore(IConfiguration config) {
  bucket=config["R2_BUCKET_NAME"]??"";
  quota=long.TryParse(config["FRAME_STORAGE_LIMIT_BYTES"],out var limit)&&limit>0?limit:8L*1024*1024*1024;
  var account=config["R2_ACCOUNT_ID"]; var access=config["R2_ACCESS_KEY_ID"]; var secret=config["R2_SECRET_ACCESS_KEY"];
  if(new[]{bucket,account,access,secret}.All(string.IsNullOrWhiteSpace))return;
  if(new[]{bucket,account,access,secret}.Any(string.IsNullOrWhiteSpace))throw new InvalidOperationException("Set all four R2 settings: R2_ACCOUNT_ID, R2_BUCKET_NAME, R2_ACCESS_KEY_ID, R2_SECRET_ACCESS_KEY.");
  if(!System.Text.RegularExpressions.Regex.IsMatch(account!,"^[a-fA-F0-9]{32}$"))throw new InvalidOperationException("R2_ACCOUNT_ID must be the 32-character Cloudflare account ID.");
  client=new AmazonS3Client(new BasicAWSCredentials(access,secret),new AmazonS3Config {
   ServiceURL=$"https://{account}.r2.cloudflarestorage.com",AuthenticationRegion="auto",ForcePathStyle=true,
   RequestChecksumCalculation=RequestChecksumCalculation.WHEN_REQUIRED,
   ResponseChecksumValidation=ResponseChecksumValidation.WHEN_REQUIRED
  });
 }
 async Task CheckQuota(long incoming) {
  long total=0;string? token=null;
  do {
   var page=await client!.ListObjectsV2Async(new(){BucketName=bucket,Prefix="projects/",ContinuationToken=token});
   foreach(var item in page.S3Objects??[])total+=item.Size??0;
   token=page.IsTruncated==true?page.NextContinuationToken:null;
  }while(token is not null);
  if(total+incoming>quota)throw new InvalidOperationException("Your configured cloud storage allowance is full. Remove old projects from the private R2 bucket or increase FRAME_STORAGE_LIMIT_BYTES after reviewing storage costs.");
 }
 async Task PutFile(Guid id,string name,string file,string contentType) {
  await client!.PutObjectAsync(new(){BucketName=bucket,Key=Key(id,name),FilePath=file,ContentType=contentType,UseChunkEncoding=false,DisablePayloadSigning=true});
 }
 public async Task SaveOriginal(Project p) {
  if(!Enabled)return;
  await CheckQuota(new FileInfo(p.Source).Length+64*1024);
  await PutFile(p.Id,"source"+Path.GetExtension(p.Source),p.Source,"application/octet-stream");
  try {await SaveProject(p);}catch {await client!.DeleteObjectAsync(bucket,Key(p.Id,"source"+Path.GetExtension(p.Source)));throw;}
 }
 public async Task SaveProject(Project p) {
  if(!Enabled)return;
  var record=new ProjectRecord(p.Id,p.Name,p.Duration,Path.GetExtension(p.Source),p.Segments.Select(s=>new Segment{Start=s.Start,End=s.End,Text=s.Text}).ToList(),p.Output is not null || p.CloudExportAvailable,new FileInfo(p.Source).Length,p.TrimStart,p.TrimEnd,p.FontSize,p.Color,p.CloudExportVersion);
  await client!.PutObjectAsync(new(){BucketName=bucket,Key=Key(p.Id,"project.json"),ContentBody=JsonSerializer.Serialize(record),ContentType="application/json",UseChunkEncoding=false,DisablePayloadSigning=true});
 }
 public async Task SaveExports(Project p) {
  if(!Enabled)return;
  var folder=Path.GetDirectoryName(p.Source)!;
  await CheckQuota(new[]{"mp4","srt","vtt"}.Sum(ext=>new FileInfo(Path.Combine(folder,"export."+ext)).Length));
  var version=Guid.NewGuid().ToString("N");var previous=p.CloudExportVersion;
  try {
   foreach(var ext in new[]{"mp4","srt","vtt"})await PutFile(p.Id,$"exports/{version}/export.{ext}",Path.Combine(folder,"export."+ext),ext=="mp4"?"video/mp4":ext=="vtt"?"text/vtt":"application/x-subrip");
   p.CloudExportVersion=version;await SaveProject(p);
  } catch {
   p.CloudExportVersion=previous;
   foreach(var ext in new[]{"mp4","srt","vtt"})try {await client!.DeleteObjectAsync(bucket,Key(p.Id,$"exports/{version}/export.{ext}"));}catch { /* A later project deletion also removes orphaned exports. */ }
   throw;
  }
  if(previous is not null)foreach(var ext in new[]{"mp4","srt","vtt"})try {await client!.DeleteObjectAsync(bucket,Key(p.Id,$"exports/{previous}/export.{ext}"));}catch { /* Retain the committed export if cleanup is temporarily unavailable. */ }
 }
 public async Task<ProjectRecord?> Find(Guid id) {
  if(!Enabled)return null;
  try {using var response=await client!.GetObjectAsync(bucket,Key(id,"project.json"));return await JsonSerializer.DeserializeAsync<ProjectRecord>(response.ResponseStream);}
  catch(AmazonS3Exception ex) when(ex.StatusCode==System.Net.HttpStatusCode.NotFound){return null;}
 }
 public async Task<List<ProjectRecord>> List() {
  var result=new List<ProjectRecord>();if(!Enabled)return result;string? token=null;
  do {
   var page=await client!.ListObjectsV2Async(new(){BucketName=bucket,Prefix="projects/",ContinuationToken=token});
   foreach(var item in page.S3Objects??[])if(item.Key.EndsWith("/project.json")&&Guid.TryParse(item.Key.Split('/')[1],out var id)){var record=await Find(id);if(record is not null)result.Add(record);}
   token=page.IsTruncated==true?page.NextContinuationToken:null;
  }while(token is not null);
  return result.OrderBy(p=>p.Name).ToList();
 }
 public async Task RestoreOriginal(ProjectRecord record,string destination) {
  using var response=await client!.GetObjectAsync(bucket,Key(record.Id,"source"+record.Extension));
  await using var file=File.Create(destination);await response.ResponseStream.CopyToAsync(file);
 }
 public async Task<string?> DownloadUrl(Guid id,string format) {
  var record=await Find(id);if(record is null||!record.HasExport)return null;
  return await client!.GetPreSignedURLAsync(new(){BucketName=bucket,Key=Key(id,record.ExportVersion is null?"export."+format:$"exports/{record.ExportVersion}/export.{format}"),Expires=DateTime.UtcNow.AddMinutes(5),Verb=HttpVerb.GET,ResponseHeaderOverrides=new(){ContentDisposition=$"attachment; filename=frame-export.{format}"}});
 }
 public async Task Delete(Guid id) {
  if(!Enabled)return;
  var keys=new List<string>();string? token=null;
  do {
   var page=await client!.ListObjectsV2Async(new(){BucketName=bucket,Prefix=Key(id,""),ContinuationToken=token});
   keys.AddRange((page.S3Objects??[]).Select(item=>item.Key));token=page.IsTruncated==true?page.NextContinuationToken:null;
  }while(token is not null);
  foreach(var key in keys.OrderBy(k=>k.EndsWith("/project.json")?1:0))await client!.DeleteObjectAsync(bucket,key);
 }
 public void Dispose()=>client?.Dispose();
}
