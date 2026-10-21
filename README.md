# Frame — C# AI Video Editor

A Blazor Interactive Server application built with .NET 10. Upload MP4/MOV/WebM videos, trim them, generate and edit multilingual subtitles, and export MP4, SRT, and VTT. All application logic and UI interaction are written in C# / Razor; video processing uses FFmpeg.

## Run

Install the .NET 10 SDK, FFmpeg (including ffprobe, libx264 and libass), and Noto Sans Devanagari fonts for Hindi rendering.

```sh
export PATH="/workspace/tools/dotnet:$PATH" # cloud installation; omit for system SDK
cd /workspace/video-editor
./scripts/dev.sh
```

`OPENAI_API_KEY` is also accepted for local setups. Set `FRAME_AI_API_KEY` securely in the server process environment to enable AI. Do not commit keys. Optional `OPENAI_TRANSLATION_MODEL` defaults to `gpt-4o-mini`. Speech recognition calls OpenAI `whisper-1`; a second AI request translates captions to English, Devanagari Hindi, or Roman Hindi/English Hinglish. These are paid remote operations and upload the video's extracted audio and caption text to OpenAI. Without a key, upload, manual captions, trimming, and export still work. Review AI output for transcription and translation errors.

Caption timing is relative to the original video. Export clips intersecting captions and shifts their timing relative to the trim's start. Styling applies to burned-in captions; SRT and VTT retain timing/text. Video export re-encodes to H.264/AAC. Uploads are limited to 500 MB; AI extracted audio must be below 24 MB. Choose videos with audio for AI transcription.

## First-version limitations

For a trusted local/single-user environment only. There is no account system or access control; anyone who has an asset URL can access it. Projects are in memory and media is kept in the system temporary directory (`frame-video-editor`). Restarting loses project references; delete that directory when the server is stopped to remove media. Deployments need authentication, per-user storage, cleanup quotas, a background job queue, and durable project storage before public use. AI requests require network access to `api.openai.com`. Fonts use local system fonts.

## Validation

In the cloud set `DOTNET_CLI_HOME=/workspace/tools/dotnet-home`, `NUGET_PACKAGES=/workspace/tools/nuget`, and `XDG_CACHE_HOME=/tmp/frame-cache` for writable caches. The sandbox may require approval for .NET named-mutex operations.

`dotnet build` checks the application. `dotnet run --project checks/Frame.Checks.csproj` runs a dependency-free functional check for actual video upload/probing, trimmed caption timing, invalid input rejection, subtitle burn-in/export, and output streams. It requires FFmpeg and ffprobe.
