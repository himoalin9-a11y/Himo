Himo — المشروع الكامل بعد تحديث واجهة الاتصال وإصلاح هدف التشغيل في Debug

المحتويات
- مشروع .NET MAUI Android كامل (Himo.sln و Himo.csproj وجميع ملفات Views/Services/Resources/Platforms وملفات Himo.Api).
- واجهة المكالمات في Views/CallPage.xaml مع الإبقاء على أسماء العناصر المستخدمة في CallPage.xaml.cs.
- AndroidManifest.Debug.xml: يزيل مرشحات التشغيل من aliases في Debug فقط.
- MainActivity: يجعل MainActivity هدف تشغيل في Debug فقط؛ تبقى MainLauncher=false في Release.
- Himo.csproj يختار Manifest الخاص بـ Debug فقط. ملف AndroidManifest.xml الأصلي لم يتغير.

طريقة الاستخدام
1) فك الضغط إلى مجلد جديد، مثل E:\Downloads\Himo-Full. لا تدمج فوق مجلد مشروعك الحالي مباشرة.
2) افتح Himo.sln.
3) اختر Debug و net10.0-android36.0 والجهاز الصحيح من Visual Studio.
4) نفّذ Rebuild Solution ثم Deploy ثم F5.
5) إذا ظل Visual Studio يحاول تشغيل LauncherIconHomeDefault، أغلق VS وافتح المشروع من المجلد الجديد، ثم تحقق من Configuration Manager أن المشروع محدد له Deploy، وأرسل سجل Output الجديد.

مهم
- هذه حزمة المصدر الكامل، وليست APK جاهزًا. لم يتم تشغيل بناء Android أو اختبار على جهاز فعلي في بيئة إنشاء الحزمة.
- متطلبات بناء المشروع تشمل .NET MAUI workload المطابق وAndroid SDK، وقد تتطلب ملفات WebRTC الموضحة في Tools.
- قبل استبدال مشروعك الحالي، احتفظ بنسخة منه؛ لا تحذف بيانات التطبيق على الهاتف.
- Release يستخدم AndroidManifest.xml الأصلي ويحتفظ بسلوك أيقونات التشغيل الديناميكية. في Debug ستظهر MainActivity كمدخل التشغيل بدل aliases.
