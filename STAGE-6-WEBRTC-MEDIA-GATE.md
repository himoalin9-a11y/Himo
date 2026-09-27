# Himo — Stage 6: WebRTC Media Engine Gate

## هدف المرحلة
إنشاء حدّ ثابت لمحرك WebRTC الحقيقي بدون إدخال مكتبة Native غير مختبرة على Android.

## ما تم
- إضافة `IWebRtcMediaEngine` كواجهة مستقلة عن Android.
- إضافة `NoOpWebRtcMediaEngine` كتنفيذ آمن مؤقت.
- تسجيل المحرك عبر Dependency Injection.
- إبقاء `WebRtcSession` مسؤولاً عن Offer/Answer/ICE فقط.
- إبقاء `ICallMediaController` مسؤولاً عن AudioManager فقط.
- عدم إدخال SpawnDev.RTC أو أي Native WebRTC package في هذه المرحلة حتى يتم اختبار توافق Android فعلياً.

## لماذا
حزمة `SpawnDev.RTC` الحالية 2.2.4 تعلن توافقاً محسوباً مع `net10.0-android`، لكنها تصف التنفيذ الأساسي بأنه Browser/Desktop، لذلك لا نعتبر التقاط الكاميرا والميكروفون على Android مضموناً قبل اختبار جهاز حقيقي.

## بوابة المرحلة التالية
قبل تشغيل الفيديو والصوت الحقيقيين:
1. Build نظيف على Windows مع Android workload.
2. تثبيت APK على جهازين Android.
3. اختبار Permission + AudioManager.
4. اختيار Native WebRTC binding بعد نجاح اختبار Android.
5. بعدها فقط يتم ربط microphone/camera tracks وPeerConnection.

هذه المرحلة لا تدّعي أن RTP/WebRTC media أصبح يعمل؛ هدفها منع ربط UI وAudioManager وSignaling بمحرك غير مؤكد.
