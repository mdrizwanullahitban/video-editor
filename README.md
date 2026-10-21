# Frame — C# AI Video Editor

A Blazor Interactive Server application built with .NET 10. Upload MP4/MOV/WebM videos, trim them, generate and edit multilingual subtitles, and export MP4, SRT, and VTT. All application logic and UI interaction are written in C# / Razor; video processing uses FFmpeg.

## Run

Install the .NET 10 SDK, FFmpeg (including ffprobe, libx264 and libass), and Noto Sans Devanagari fonts for Hindi rendering.

```sh
export PATH="/workspace/tools/dotnet:$PATH" # cloud installation; omit for system SDK
cd /workspace/video-editor
./scripts/dev.sh
```

`OPENAI_API_KEY` is also accepted for local setups. Set `FRAME_AI_API_KEY` securely in the server process environment to enable AI. Do not commit keys. Optional `OPENAI_TRANSLATION_MODEL` defaults to `gpt-4.1`. Speech recognition defaults to OpenAI `gpt-4o-transcribe-diarize` with timestamped `diarized_json` and automatic audio chunking; a second AI request translates captions to English, Devanagari Hindi, or Roman Hindi/English Hinglish. These are paid remote operations and upload the video's extracted audio and caption text to OpenAI. Without a key, upload, manual captions, trimming, and export still work. Review AI output for transcription and translation errors. Set `FRAME_TRANSCRIPTION_MODEL=whisper-1` only if you need the legacy model; other transcription models are rejected because they do not expose the same timing format. The default model may cost more; account access is required. Live accuracy has not been benchmarked. API contract checks use mock responses, not paid live calls.

Caption timing is relative to the original video. Export clips intersecting captions and shifts their timing relative to the trim's start. Styling applies to burned-in captions; SRT and VTT retain timing/text. Video export re-encodes to H.264/AAC. Uploads accept up to **1 GB (1,024 MB / 1,073,741,824 bytes)** inclusive, streamed directly to disk with a loading screen showing filename, uploaded MB, total MB, percentage, and cancellation. A separate checking stage appears after the bytes finish uploading. AI generation and export show their processing stage without inventing a completion percentage. Keep the browser tab connected during uploads. Leave several GB of free disk space for the source, encoded export, and audio. FFmpeg operations have a two-hour timeout; a large file may take a long time depending on duration, resolution, and CPU. The Blazor stream timeout is 30 minutes; an exceptionally slow upload may need retrying. AI transcription is a separate limit: AI extracted audio must be below 24 MB. Choose videos with audio for AI transcription.

## Windows / VS Code

Open the cloned project folder and run in PowerShell:

```powershell
git pull origin main
dotnet run --project VideoEditor.csproj --urls http://localhost:5000
```

Open http://localhost:5000 and keep the terminal running. Confirm `ffmpeg -version` and `ffprobe -version` work in that terminal. Install Noto Sans Devanagari for Hindi exports, then restart the app. To enable paid AI requests, set `$env:FRAME_AI_API_KEY` securely in the current terminal before starting the server; never share the value or commit it.

## First-version limitations

For a trusted local/single-user environment only. There is no account system or access control; anyone who has an asset URL can access it. Projects are in memory and media is kept in the system temporary directory (`frame-video-editor`). Restarting loses project references; delete that directory when the server is stopped to remove media. Deployments need authentication, per-user storage, cleanup quotas, a background job queue, and durable project storage before public use. AI requests require network access to `api.openai.com`. Fonts use local system fonts.

## Validation

In the cloud set `DOTNET_CLI_HOME=/workspace/tools/dotnet-home`, `NUGET_PACKAGES=/workspace/tools/nuget`, and `XDG_CACHE_HOME=/tmp/frame-cache` for writable caches. The sandbox may require approval for .NET named-mutex operations.

`dotnet build` checks the application. `dotnet run --project checks/Frame.Checks.csproj` runs a dependency-free functional check for actual video upload/probing, trimmed caption timing, invalid input rejection, subtitle burn-in/export, and output streams. It requires FFmpeg and ffprobe.
