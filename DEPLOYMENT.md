# Deploy Frame online with Render + Cloudflare R2 + OpenAI

Visitors only need a browser. The Dockerfile includes .NET 10, FFmpeg/ffprobe, and Hindi fonts. No local installation is needed for the hosted version.

## What is implemented

- Private workspace protected by an owner password, HTTPS-only session cookie, antiforgery protection, and sign-in rate limiting.
- Private R2 storage for uploaded source videos, editable project metadata (captions, trim points, and subtitle style), and MP4/SRT/VTT exports.
- Cloud library for reopening projects after an app restart, saving edits, and permanently deleting projects.
- Short-lived signed export download links. Keep the bucket private; public bucket URLs are unnecessary.
- A default 8 GiB application storage allowance, controlled by `FRAME_STORAGE_LIMIT_BYTES`. This is a conservative application limit, **not a billing guarantee**. Existing objects are counted before uploads. Replacing an export temporarily requires enough allowance for another copy. Delete old projects from the editor to reclaim storage.
- One transcription/export at a time per running instance. Use one instance with this prototype.
- Cloud mode refuses startup without the owner password, AI key, and all four R2 settings.

## 1. Create storage in Cloudflare

1. Sign in to Cloudflare and enable R2. Review current billing conditions and free allowances; billing details may be required.
2. Create a private bucket, for example `frame-videos`. Leave public access disabled.
3. Create an R2 S3 API token with **Object Read & Write**, scoped to this bucket. Retain its access key ID and secret access key securely. These are S3 credentials, not a Cloudflare global API key.
4. Copy your account ID from R2's account details. The app constructs the standard endpoint `https://ACCOUNT_ID.r2.cloudflarestorage.com`. Use a standard global bucket for this configuration.

Do not paste credentials in chat, GitHub, or source files.

## 2. Get an OpenAI API key

Create a key in your OpenAI API account and enable API billing. A ChatGPT subscription does not include API credit. Default models are `gpt-4o-transcribe-diarize` for timestamped speech recognition and `gpt-4.1` for translation. Usage is paid. Model availability and language quality need verification with your own account and sample videos.

## 3. Deploy from GitHub on Render

1. Sign in to Render, connect GitHub, and choose **New → Blueprint**.
2. Select `mdrizwanullahitban/video-editor`, branch `main`. Render reads `render.yaml`.
3. Enter the following securely in Render's environment settings before creating the service:

| Setting | Value |
| --- | --- |
| `FRAME_ADMIN_PASSWORD` | A unique password or passphrase with at least 16 characters |
| `FRAME_AI_API_KEY` | Your OpenAI API key |
| `R2_ACCOUNT_ID` | Cloudflare's 32-character account ID |
| `R2_BUCKET_NAME` | Your private bucket name |
| `R2_ACCESS_KEY_ID` | Bucket-scoped S3 access key ID |
| `R2_SECRET_ACCESS_KEY` | Matching S3 secret access key |

The blueprint sets `FRAME_CLOUD_MODE=true`, an 8 GiB storage allowance, and a free web-service plan. Review the plan's **current** availability and resource limits before creating it. It disables automatic deployments; use Render's manual deploy action for updates. Render supplies `PORT`; the container listens on it. `/health` is the liveness endpoint and does not verify external API credentials.

4. Deploy and use the public HTTPS URL shown by Render. That provider-assigned address is stable while you retain the service; deleting/recreating the service or provider policy changes can affect it. A custom domain is optional and usually has its own cost.
5. Sign in with the workspace password. Upload a small video first, generate subtitles, save it, and export. Restart the service and confirm the project reopens from R2. Then test a larger video within your host's resources.

## Costs and limits

Free tiers are limited and can change. Free Render instances may sleep, restart, and impose request/resource limits. There is **no promise of always-on free hosting or reliable 1 GB processing on the free plan**. Upgrade if its CPU, memory, temporary disk, upload duration, or WebSocket limits are insufficient. The upload code accepts 1 GiB and streams to disk; decoding/export still needs local disk and CPU. R2 does not replace processing scratch space. Keep the browser connected for uploads and long operations.

R2 storage/operations above its allowances and OpenAI generation can cost money. Set provider budgets/alerts. The app's size cap is not a substitute for provider billing limits. Audio sent for AI is currently limited to 24 MB after compression.

Projects are durable in R2; processing uses temporary local files, and work interrupted by a service restart must be retried. Click **Save project to cloud** after manual edits. AI subtitles and exports save automatically. Local-only mode without R2 remains temporary. One shared owner login is supported; this is a private workspace, not a public multi-user video service. Session cookies may be invalidated after a restart, requiring sign-in again.

## Validation and current deployment status

Run `dotnet run --project checks/Frame.Checks.csproj` for media and cloud-store contract checks; cloud persistence checks use an in-memory mock store. `-- --cloud-auth-only` validates a real Production HTTP instance's login, antiforgery, and cookie behavior using test-only credentials without contacting R2. `-- --web-assets-only` validates browser asset availability.

The application builds and publishes. A container build in the setup environment was blocked by a 403 from Microsoft's image CDN (`westus.data.mcr.microsoft.com`); that domain was added to the environment configuration draft for review. The full container has not been validated here. No Render service or R2 bucket has been created by the setup assistant, no credentials have been supplied, and no live cloud URL or live OpenAI/R2 validation is claimed. Completing the provider-account steps above is required before the app is online.
