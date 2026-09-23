Stage 46 - FCM credential warning fix

Replaced deprecated GoogleCredential.FromJson with CredentialFactory.FromJson<ServiceAccountCredential>(...).ToGoogleCredential(). No application behavior changes.
