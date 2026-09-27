# Himo Build Verification

The canonical solution is `Himo.sln`. It contains exactly two projects:

- `Himo.csproj` — Android/.NET MAUI client
- `Himo.Api/Himo.Api.csproj` — ASP.NET Core API

The Android project has `Build.0` and `Deploy.0` entries in Debug and Release in the solution configuration.

Before release, run on a machine with the .NET 10 SDK, Android workload, and Visual Studio MAUI workload installed:

```powershell
dotnet workload restore
dotnet restore Himo.sln
dotnet build Himo.sln -c Debug
dotnet build Himo.sln -c Release
```

Then launch `Himo` from Visual Studio on an Android emulator/device.

This archive was statically validated in the packaging environment; that environment does not contain the .NET SDK, so an Android/MSBuild compilation cannot truthfully be claimed from here.
