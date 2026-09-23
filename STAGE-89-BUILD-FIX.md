# Stage 89 build fix

The reported error `System.IO.IOException: The directory is not empty` is thrown by Xamarin.Android's generated-directory cleanup task (`RemoveDirFixed`). It normally means a stale `bin/obj` Android intermediate directory is still locked or contains files from a previous deployment.

This package keeps the app project unchanged and includes `RESET-ANDROID-BUILD.cmd` to remove only generated `bin`, `obj`, and `.vs` folders before rebuilding.

## Correct recovery
1. Close Visual Studio completely.
2. Run `RESET-ANDROID-BUILD.cmd`.
3. Open `Himo.sln`.
4. Select **Rebuild All**.

`Himo.csproj` already disables Android Fast Deployment (`AndroidFastDeployment=false`, `AndroidUseSharedRuntime=false`, `EmbedAssembliesIntoApk=true`), so the app is not intentionally using the problematic fast-deployment override directory.
