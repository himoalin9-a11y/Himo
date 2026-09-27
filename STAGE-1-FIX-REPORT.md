# Stage 1 — Build/Project Hygiene Fix

- Removed the duplicate root `Himo.Api.csproj`; the solution already references `Himo.Api/Himo.Api.csproj`.
- Kept the MAUI project target at `net10.0-android36.0` with minimum Android API 30 (Android 11).
- Enabled Android 16 `pageSizeCompat` in `Platforms/Android/AndroidManifest.xml` as a compatibility bridge for the current WebRTC native library.
- Static audit found no remaining `Brush.FromArgb`, `Ellipse.Color`, `ApiUrlEntry`, `ApiStatusLabel`, or `TestApiButton` references.

Build validation could not be executed in this environment because the .NET SDK is not installed here. Run the project build on the Windows development machine before proceeding to the native WebRTC replacement.
