using System;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;

namespace Himo.Views;

/// <summary>
/// Shared icon treatment for Himo settings pages. Uses the professional monoline
/// icon set, a theme-aware soft badge and a consistent rounded-square silhouette.
/// Visual-only helpers: they do not own navigation or business logic.
/// </summary>
internal static class SettingsVisuals
{
    public static Border IconBadge(string assetName, double size = 36, double iconSize = 20)
    {
        var safeIconSize = Math.Max(16, Math.Min(iconSize, size - 12));
        var badge = new Border
        {
            WidthRequest = size,
            HeightRequest = size,
            Padding = Math.Max(5, (size - safeIconSize) / 2),
            BackgroundColor = Color.FromArgb("#39204F"),
            Stroke = new SolidColorBrush(Color.FromArgb("#594768")),
            StrokeThickness = 0.8,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(Math.Max(9, size * 0.27)) },
            Content = new Image
            {
                Source = assetName,
                WidthRequest = safeIconSize,
                HeightRequest = safeIconSize,
                Aspect = Aspect.AspectFit,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            }
        };

        // Keep the badge in sync with the chosen Himo interface palette.
        badge.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoSoft");
        badge.SetDynamicResource(Border.StrokeProperty, "HimoBorder");
        return badge;
    }

    public static View TitleRow(string assetName, string text, double fontSize = 16)
    {
        var title = new Label
        {
            Text = text,
            FontSize = fontSize,
            FontAttributes = FontAttributes.Bold,
            TextColor = GetThemeColor("HimoPrimary", Color.FromArgb("#F7E9FF")),
            VerticalTextAlignment = TextAlignment.Center,
            HorizontalTextAlignment = TextAlignment.Start,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            LineBreakMode = LineBreakMode.WordWrap
        };
        title.SetDynamicResource(Label.TextColorProperty, "HimoPrimary");

        return new HorizontalStackLayout
        {
            FlowDirection = FlowDirection.RightToLeft,
            Spacing = 9,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Fill,
            Children = { IconBadge(assetName, 36, 20), title }
        };
    }

    private static Color GetThemeColor(string key, Color fallback)
    {
        var application = Application.Current;
        if (application is not null && application.Resources.TryGetValue(key, out var value) && value is Color color)
            return color;
        return fallback;
    }

    public static string IconForSetting(string title)
    {
        if (title.Contains("صورة الملف الشخصي", StringComparison.Ordinal) || title.Contains("ملف شخصي", StringComparison.Ordinal)) return "himo_pro_ui_settings_icon_user.png";
        if (title.Contains("محظور", StringComparison.Ordinal) || title.Contains("حظر", StringComparison.Ordinal)) return "himo_pro_ui_settings_icon_blocked.png";
        if (title.Contains("التحقق من الخصوصية", StringComparison.Ordinal)) return "himo_pro_ui_settings_icon_check.png";
        if (title.Contains("خصوص", StringComparison.Ordinal)) return "himo_pro_ui_settings_icon_privacy.png";
        if (title.Contains("أمان", StringComparison.Ordinal) || title.Contains("حماية", StringComparison.Ordinal)) return "himo_pro_ui_settings_icon_shield.png";
        if (title.Contains("إشعار", StringComparison.Ordinal) || title.Contains("تنبيه", StringComparison.Ordinal) || title.Contains("تذكير", StringComparison.Ordinal)) return "himo_pro_ui_settings_icon_bell.png";
        if (title.Contains("اهتزاز", StringComparison.Ordinal)) return "himo_pro_ui_settings_icon_vibration.png";
        if (title.Contains("صوت", StringComparison.Ordinal) || title.Contains("نغمة", StringComparison.Ordinal)) return "himo_pro_ui_settings_icon_sound.png";
        if (title.Contains("لغة", StringComparison.Ordinal)) return "himo_pro_ui_settings_icon_globe.png";
        if (title.Contains("مظهر", StringComparison.Ordinal) || title.Contains("سمة", StringComparison.Ordinal) || title.Contains("لون", StringComparison.Ordinal) || title.Contains("خلفية", StringComparison.Ordinal)) return "himo_pro_ui_settings_icon_palette.png";
        if (title.Contains("حجم الخط", StringComparison.Ordinal) || title.Contains("خط", StringComparison.Ordinal) || title.Contains("إدخال", StringComparison.Ordinal)) return "himo_pro_ui_settings_icon_text.png";
        if (title.Contains("نسخ", StringComparison.Ordinal) || title.Contains("احتياطي", StringComparison.Ordinal) || title.Contains("Google", StringComparison.OrdinalIgnoreCase)) return "himo_pro_ui_settings_icon_backup.png";
        if (title.Contains("شبكة", StringComparison.Ordinal) || title.Contains("تجوال", StringComparison.Ordinal) || title.Contains("بيانات مستلمة", StringComparison.Ordinal) || title.Contains("بيانات مرسلة", StringComparison.Ordinal) || title.Contains("استخدام الشبكة", StringComparison.Ordinal)) return "himo_pro_ui_settings_icon_network.png";
        if (title.Contains("تخزين", StringComparison.Ordinal) || title.Contains("ملفات", StringComparison.Ordinal) || title.Contains("بيانات التطبيق", StringComparison.Ordinal)) return "himo_pro_ui_settings_icon_storage.png";
        if (title.Contains("مكالمة", StringComparison.Ordinal) || title.Contains("مكالمات", StringComparison.Ordinal) || title.Contains("هاتف", StringComparison.Ordinal) || title.Contains("رقم الهاتف", StringComparison.Ordinal)) return "himo_pro_ui_settings_icon_phone.png";
        if (title.Contains("جهات", StringComparison.Ordinal) || title.Contains("مجموعة", StringComparison.Ordinal) || title.Contains("أصدقاء", StringComparison.Ordinal)) return "himo_pro_ui_settings_icon_contacts.png";
        if (title.Contains("دردشة", StringComparison.Ordinal) || title.Contains("محادث", StringComparison.Ordinal) || title.Contains("رسائل", StringComparison.Ordinal) || title.Contains("فقاعة", StringComparison.Ordinal)) return "himo_pro_ui_icon_chat.png";
        if (title.Contains("وسائط", StringComparison.Ordinal) || title.Contains("صورة", StringComparison.Ordinal) || title.Contains("فيديو", StringComparison.Ordinal) || title.Contains("كاميرا", StringComparison.Ordinal) || title.Contains("ملصق", StringComparison.Ordinal)) return "himo_pro_ui_settings_icon_media.png";
        if (title.Contains("أرشيف", StringComparison.Ordinal) || title.Contains("احتفاظ", StringComparison.Ordinal)) return "himo_pro_ui_settings_icon_archive.png";
        if (title.Contains("قفل", StringComparison.Ordinal) || title.Contains("كلمة المرور", StringComparison.Ordinal)) return "himo_pro_ui_settings_icon_lock.png";
        if (title.Contains("بريد", StringComparison.Ordinal) || title.Contains("إلكتروني", StringComparison.Ordinal) || title.Contains("دخول", StringComparison.Ordinal)) return "himo_pro_ui_settings_icon_mail.png";
        if (title.Contains("حساب", StringComparison.Ordinal) || title.Contains("مستخدم", StringComparison.Ordinal) || title.Contains("اسم المستخدم", StringComparison.Ordinal)) return "himo_pro_ui_settings_icon_user.png";
        if (title.Contains("حذف", StringComparison.Ordinal)) return "settings_icon_delete.png";
        if (title.Contains("إضافة", StringComparison.Ordinal)) return "himo_pro_ui_icon_plus.png";
        if (title.Contains("آخر ظهور", StringComparison.Ordinal) || title == "الحالة") return "himo_pro_ui_settings_icon_eye.png";
        return "himo_pro_ui_settings_icon_user.png";
    }
}
