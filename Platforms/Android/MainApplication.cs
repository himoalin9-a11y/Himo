using System;
using Microsoft.Maui;
using Microsoft.Maui.Hosting;
using Plugin.Firebase.CloudMessaging;

namespace Himo;

[global::Android.App.Application]
public class MainApplication : MauiApplication
{
    public MainApplication(IntPtr handle, global::Android.Runtime.JniHandleOwnership ownership)
        : base(handle, ownership)
    {
        // Set the FCM channel identifier as early as possible. Android can create
        // the notification while the application process is cold, before MainActivity
        // has been created, so the channel must not depend on Activity startup.
        FirebaseCloudMessagingImplementation.ChannelId = "himo_messages";
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
