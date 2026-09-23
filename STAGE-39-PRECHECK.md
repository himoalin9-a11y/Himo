# Stage 39 — Release precheck

- Moved the MAUI `UserSearchDto` used by XAML compiled bindings to `Himo.Models.UserSearchDto`.
- Updated SearchPage to use the top-level model type.
- Replaced deprecated `AutomationProperties.Name` with `SemanticProperties.Description`.
- No runtime behavior was intentionally changed.

Required local verification in Visual Studio:
1. Configuration: Release
2. Rebuild Solution
3. Confirm 0 errors and 0 warnings.
4. Only after that, proceed to APK/AAB packaging.
