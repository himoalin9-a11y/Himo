# Himo — Stage 8: Real Android WebRTC Media

تم تحويل `IWebRtcMediaEngine` من placeholder إلى محرك Android فعلي يعتمد على
`FsWebRTC.Bindings.Maui.Android`.

## المكتمل
- إنشاء `PeerConnectionFactory`.
- تهيئة AudioDeviceModule الافتراضي.
- إنشاء AudioSource/AudioTrack للميكروفون.
- إنشاء Camera2Capturer وVideoSource/VideoTrack لمكالمات الفيديو.
- إنشاء PeerConnection باستخدام Unified Plan.
- Offer / Answer / ICE أصبح مرتبطًا بمحرك WebRTC الحقيقي.
- دعم كتم الميكروفون وإيقاف/تشغيل الكاميرا وتبديل الكاميرا.
- استقبال المسار الصوتي والفيديو البعيد.
- تنظيف PeerConnection والكاميرا والـ EGL والـ tracks عند نهاية المكالمة.
- إضافة SurfaceViewRenderer محلي وبعيد داخل واجهة MAUI.

## ملاحظة تشغيلية
يوجد STUN افتراضي للتجربة. الاتصالات خلف NAT مقيد أو شبكات مؤسسية قد تحتاج TURN.
تمت إضافة إعدادات TURN إلى Preferences حتى لا توضع بيانات الاعتماد داخل المصدر.
