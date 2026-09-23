# STAGE 40 — Release Package Generation

Release rebuild verification from the latest user build log:
- Himo: succeeded
- Himo.Api: succeeded
- Errors: 0
- XAML warnings: 0
- Rebuild completed successfully.

This stage does not change application logic.

## Generate the Android release package

1. Open the solution/project in Visual Studio.
2. Select `Release` and `Any CPU`.
3. Run `Rebuild Solution` once more if needed.
4. Run `PUBLISH-RELEASE.bat`, or use Visual Studio's Publish workflow.
5. The publish output is under:
   `bin\Release\net10.0-android\publish\`

For Google Play, an Android App Bundle (AAB) is normally the package used for publishing. Before distributing a production AAB/APK, configure a production Android signing keystore and keep its password/key material private.

Do not perform the final two-device functional test until the release package has been generated and installed.
