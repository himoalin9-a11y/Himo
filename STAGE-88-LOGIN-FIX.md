Stage 88 Login - Safe Visual Fix

This version restores the known-working LoginPage structure from the previous fixed stage and applies only visual changes that use the same MAUI controls/properties already used by that working page.

Preserved:
- LoginPage.xaml.cs unchanged
- Account/API/authentication logic unchanged
- Password show/hide handler unchanged
- All existing named controls and Clicked handlers preserved
- Existing logo asset preserved to avoid startup/resource regressions

Visual changes:
- Deeper purple gradient background
- Larger premium logo area
- Refined card spacing and corner radius
- More pronounced purple accent/button gradient
- Additional subtle decorative glow
- Refined field/card proportions
