# Stage 106 — Home Reference Match

This stage focuses on **HomePage only**.

## Changed
- `Views/HomePage.xaml` only.
- Reworked the visual structure to closely follow the supplied Home reference image:
  - purple gradient header with rounded lower corners
  - logo on the left, Himo title centered, settings on the right
  - sparkle tile + "محادثاتك" hero section
  - large white rounded search field
  - conversations heading and filters
  - clean empty conversation area
  - white rounded bottom navigation with active conversations tab on the right

## Preserved
- `Views/HomePage.xaml.cs` unchanged.
- Login/authentication unchanged.
- API/services/database unchanged.
- Event handlers and x:Name contracts used by HomePage code-behind preserved.
- Other UI pages are not modified in this stage; their existing files from Stage 105 are retained unchanged.
