# Stage 2 — WebRTC / 16 KB

## تم الإصلاح
- لم نعد نستبدل `libwebrtc.aar` بالكامل بإصدار WebRTC مختلف.
- السكربت الآن يحافظ على Java classes/resources الخاصة بـ `FsWebRTC.Bindings.Maui.Android 0.9.3.15`.
- يتم استبدال `libjingle_peerconnection_so.so` فقط في `arm64-v8a` و`x86_64`.
- يتم فحص ELF alignment قبل إدخال المكتبات الجديدة.
- يتم الاحتفاظ بنسخة احتياطية من AAR الأصلي.

## السبب
استبدال AAR بالكامل قد يغيّر Java API الذي تعتمد عليه الـbindings، بينما المطلوب هنا معالجة المكتبة native التي تسبب XA0141.

## التنفيذ على جهاز التطوير
بعد `dotnet restore`:

```powershell
powershell -ExecutionPolicy Bypass -File .\Tools\Install-WebRTC16KB.ps1
```

ثم:

```powershell
dotnet clean .\Himo.sln
dotnet build .\Himo.sln
```

بعد إنتاج APK يجب فحص `lib/arm64-v8a/libjingle_peerconnection_so.so` داخل APK نفسه؛ نجاح فحص AAR وحده لا يكفي.
