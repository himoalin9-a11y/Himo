# Stage 41 — APK Publish Fix

The Release rebuild is clean. The Visual Studio Archive attempt failed inside Android `bundletool`, which is the AAB/bundle packaging path.

For this stage the project explicitly selects APK packaging so the APK publish does not invoke the AAB bundle packaging path.

## Publish

Run `PUBLISH-RELEASE.bat`, or publish with:

`dotnet publish Himo.csproj -c Release -f net10.0-android -p:AndroidPackageFormat=apk`

Expected output folder:
`bin\Release\net10.0-android\publish\`

Do not start the final two-device functional test until the APK is generated and installed.
