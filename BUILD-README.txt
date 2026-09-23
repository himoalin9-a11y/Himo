Himo v9 - clean build package

This package is based on the previously working v5 project structure.
No custom Directory.Build.props, custom intermediate-output paths, or manual Compile globs are used.
The MAUI project uses the normal .NET SDK item rules and explicitly excludes Himo.Api, which is a separate ASP.NET Core project.

IMPORTANT:
1. Extract this ZIP to a NEW folder, for example E:\Downloads\Himo-v157-v9.
2. Do not copy obj, bin, .vs, or other generated folders from an older Himo project into this folder.
3. Open Himo.sln from this v9 folder.
4. Choose Build > Build Solution. Do NOT use Rebuild for the first build.
5. If Visual Studio reports an error, keep the first build output and send the first actual error (not only the warnings).


المرحلة 2: تم تثبيت FCM notification channel مبكرًا داخل MainApplication حتى يبقى ChannelId متاحًا عند تشغيل خدمة FCM والتطبيق في حالة الإغلاق الكامل.

--- Himo v10.6 / build 97 ---
Stage 24 started: attachments foundation.
- Added attachment metadata to Messages with backward-compatible SQLite migrations.
- Added authenticated multipart upload endpoint (25 MB limit, selected safe MIME types).
- Added authenticated attachment download endpoint with conversation membership checks.
- Added Android file picker attachment button in ChatPage.
- Added local attachment caching and OS-level file opening.
- Attachment messages sync through the existing message polling and FCM notification path.
- Image/audio/video preview and voice recording remain future stages; this stage provides the secure file/image transport foundation.
