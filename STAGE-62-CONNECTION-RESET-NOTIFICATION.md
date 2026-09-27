# Stage 62 — Connection Reset Notification

- عند بدء جلسة WebRTC جديدة يتم إرسال `ConnectionEstablishedChanged(false)` إذا كانت الجلسة السابقة متصلة فعليًا.
- يمنع بقاء واجهة المكالمة على حالة Connected أثناء الانتقال بين جلسات أو أوضاع الاتصال.
