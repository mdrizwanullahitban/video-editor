using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
namespace VideoEditor;
public static class CloudSecurity {
 public static bool Configure(WebApplicationBuilder builder) {
  var cloud=builder.Configuration.GetValue<bool>("FRAME_CLOUD_MODE");
  if(cloud) {
   if((builder.Configuration["FRAME_ADMIN_PASSWORD"]??"").Length<16)throw new InvalidOperationException("Cloud mode requires FRAME_ADMIN_PASSWORD with at least 16 characters. Set it securely in the hosting dashboard.");
   if(string.IsNullOrWhiteSpace(builder.Configuration["FRAME_AI_API_KEY"]))throw new InvalidOperationException("Cloud mode requires FRAME_AI_API_KEY in the hosting dashboard.");
   using var store=new R2ProjectStore(builder.Configuration);if(!store.Enabled)throw new InvalidOperationException("Cloud mode requires private R2 storage credentials.");
  }
  builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options=>{
   options.LoginPath="/login";options.Cookie.Name="frame.session";options.Cookie.HttpOnly=true;options.Cookie.SameSite=SameSiteMode.Strict;
   options.Cookie.SecurePolicy=cloud?CookieSecurePolicy.Always:CookieSecurePolicy.SameAsRequest;
   options.ExpireTimeSpan=TimeSpan.FromHours(8);options.SlidingExpiration=false;
  });
  builder.Services.AddAuthorization(options=>{if(cloud)options.FallbackPolicy=new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();});
  builder.Services.AddRateLimiter(options=>{
   options.RejectionStatusCode=429;
   options.AddPolicy("login",context=>RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString()??"unknown",_=>new FixedWindowRateLimiterOptions{PermitLimit=5,Window=TimeSpan.FromMinutes(1),QueueLimit=0}));
  });return cloud;
 }
 public static void Map(WebApplication app,bool cloud) {
  if(!cloud)return;
  app.MapGet("/login",(HttpContext context,IAntiforgery antiforgery)=>{
   context.Response.Headers.CacheControl="no-store";
   var token=HtmlEncoder.Default.Encode(antiforgery.GetAndStoreTokens(context).RequestToken!);
   return Results.Content($$"""
   <!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Frame · Sign in</title>
   <style>body{margin:0;background:#111810;color:#e8efdf;font:16px system-ui;min-height:100vh;display:grid;place-items:center}main{max-width:360px;padding:36px;border:1px solid #52603d;border-radius:20px;background:#1c2519}h1{color:#d7ef9c}label,input,button{display:block;box-sizing:border-box;width:100%;margin:18px 0}input,button{padding:14px;border-radius:8px;border:1px solid #63714e}input{background:#10170e;color:white}button{background:#d7ef9c;color:#172110;cursor:pointer}p{line-height:1.6;color:#aebda2}</style>
   <main><h1>◈ Frame Studio</h1><p>Your private video workspace. Sign in to upload videos and edit subtitles.</p>
   <form method="post" action="/login"><input type="hidden" name="__RequestVerificationToken" value="{{token}}"><label for="password">Workspace password</label><input id="password" name="password" type="password" required autocomplete="current-password" maxlength="1024"><button type="submit">Open my editor →</button></form></main></html>
   ""","text/html");
  }).AllowAnonymous();
  app.MapPost("/login",async(HttpContext context,IAntiforgery antiforgery)=>{
   try {await antiforgery.ValidateRequestAsync(context);}catch(AntiforgeryValidationException){return Results.BadRequest("Refresh the sign-in page and try again.");}
   var form=await context.Request.ReadFormAsync();var supplied=form["password"].ToString();
   var expected=app.Configuration["FRAME_ADMIN_PASSWORD"]!;
   if(supplied.Length>1024||!CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(supplied)),SHA256.HashData(Encoding.UTF8.GetBytes(expected))))return Results.Content("Incorrect password. Return to /login to try again.","text/plain",statusCode:401);
   await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ClaimTypes.Name,"owner")},CookieAuthenticationDefaults.AuthenticationScheme)));
   return Results.Redirect("/");
  }).AllowAnonymous().RequireRateLimiting("login");
  app.MapPost("/logout",async(HttpContext context,IAntiforgery antiforgery)=>{
   try {await antiforgery.ValidateRequestAsync(context);}catch(AntiforgeryValidationException){return Results.BadRequest();}
   await context.SignOutAsync();return Results.Redirect("/login");
  });
 }
}
