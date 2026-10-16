Himo Android debugger launch fix
================================

Changes in this patch:
- Registers the existing C# MainActivity under the stable Android component name
  com.companyname.himo.LauncherIconDefault so the Visual Studio launch command still
  resolves even after the user changes the launcher icon.
- Moves the visible default home-screen icon to LauncherIconHomeDefault. All selectable
  icon aliases now target the stable activity.
- Updates AndroidLaunchActivity to the stable component and adds a post-Install task
  that re-enables it if an older build persisted the former alias as disabled.

After applying these files:
1. Stop the running debug session.
2. Clean the project and close Visual Studio.
3. Delete Himo\bin and Himo\obj (do not delete user data on the phone).
4. Reopen Himo.csproj and build/deploy Debug to the phone.
5. If Visual Studio still reports LauncherIconDefault disabled, run:
   powershell -ExecutionPolicy Bypass -File .\Tools\Fix-LauncherDebugComponent.ps1
   after deployment, then press F5 again.

The helper script enables only the stable debug Activity, not the visible launcher aliases.
