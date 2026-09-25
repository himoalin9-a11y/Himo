# Stage 101 — Home Login-Safe Rollback

Purpose: restore the last known working HomePage XAML while preserving the login/authentication code and all other project files.

## Important
- `Views/LoginPage.xaml` unchanged.
- `Views/LoginPage.xaml.cs` unchanged.
- `Himo.csproj` unchanged.
- `AppShell.xaml` and `AppShell.xaml.cs` unchanged.
- `Services/AccountService.cs` and `Services/HimoApiClient.cs` unchanged.
- Only `Views/HomePage.xaml` was restored to the prior known-good version because AppShell constructs HomePage immediately after successful login. A HomePage XAML initialization exception can be caught by `LoginPage.PrimaryClicked` and appear to the user as "تعذر تسجيل الدخول".

This package intentionally prioritizes restoring sign-in stability. The Home visual redesign should be re-applied incrementally using only XAML elements already proven safe in the project.
