using System;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;

namespace Himo.Views;

/// <summary>
/// Shared icon treatment for Himo settings pages. Uses the same violet gradient
/// artwork, soft lavender badge, thin border and subtle shadow across all pages.
/// Visual-only helpers: they do not own navigation or business logic.
/// </summary>
internal static class SettingsVisuals
{
    public static Border IconBadge(string assetName, double size = 42, double iconSize = 24)
    {
        var safeIconSize = Math.Max(16, Math.Min(iconSize, size - 8));
        return new Border
        {
            WidthRequest = size,
            HeightRequest = size,
            Padding = Math.Max(3, (size - safeIconSize) / 2),
            Background = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb("#FFFFFF"), 0.0f),
                    new GradientStop(Color.FromArgb("#F4EBFF"), 0.58f),
                    new GradientStop(Color.FromArgb("#E9D7FF"), 1.0f)
                }
            },
            Stroke = Color.FromArgb("#D5B5FF"),
            StrokeThickness = 1.1,
            Shadow = new Shadow
            {
                Brush = new SolidColorBrush(Color.FromArgb("#332D087A")),
                Offset = new Point(0, 2), Radius = 4, Opacity = 0.16f
            },
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(size / 2) },
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
    }

    public static View TitleRow(string assetName, string text, double fontSize = 20)
    {
        return new HorizontalStackLayout
        {
            FlowDirection = FlowDirection.RightToLeft,
            Spacing = 10,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Fill,
            Children =
            {
                IconBadge(assetName, 42, 24),
                new Label
                {
                    Text = text,
                    FontSize = fontSize,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#300653"),
                    VerticalTextAlignment = TextAlignment.Center,
                    HorizontalTextAlignment = TextAlignment.Start,
                    VerticalOptions = LayoutOptions.Center,
                    HorizontalOptions = LayoutOptions.Fill,
                    FlowDirection = FlowDirection.RightToLeft,
                    LineBreakMode = LineBreakMode.WordWrap
                }
            }
        };
    }

    public static string IconForSetting(string title)
    {
        if (title.Contains("صورة الملف الشخصي", StringComparison.Ordinal) || title.Contains("ملف شخصي", StringComparison.Ordinal)) return "settings_icon_user.png";
        if (title.Contains("محظور", StringComparison.Ordinal) || title.Contains("حظر", StringComparison.Ordinal)) return "settings_icon_blocked.png";
        if (title.Contains("التحقق من الخصوصية", StringComparison.Ordinal)) return "settings_icon_check.png";
        if (title.Contains("خصوص", StringComparison.Ordinal)) return "settings_icon_privacy.png";
        if (title.Contains("أمان", StringComparison.Ordinal) || title.Contains("حماية", StringComparison.Ordinal)) return "settings_icon_shield.png";
        if (title.Contains("إشعار", StringComparison.Ordinal) || title.Contains("تنبيه", StringComparison.Ordinal) || title.Contains("تذكير", StringComparison.Ordinal)) return "settings_icon_bell.png";
        if (title.Contains("اهتزاز", StringComparison.Ordinal)) return "settings_icon_vibration.png";
        if (title.Contains("صوت", StringComparison.Ordinal) || title.Contains("نغمة", StringComparison.Ordinal)) return "settings_icon_sound.png";
        if (title.Contains("لغة", StringComparison.Ordinal)) return "settings_icon_globe.png";
        if (title.Contains("مظهر", StringComparison.Ordinal) || title.Contains("سمة", StringComparison.Ordinal) || title.Contains("لون", StringComparison.Ordinal) || title.Contains("خلفية", StringComparison.Ordinal)) return "settings_icon_palette.png";
        if (title.Contains("حجم الخط", StringComparison.Ordinal) || title.Contains("خط", StringComparison.Ordinal) || title.Contains("إدخال", StringComparison.Ordinal)) return "settings_icon_text.png";
        if (title.Contains("نسخ", StringComparison.Ordinal) || title.Contains("احتياطي", StringComparison.Ordinal) || title.Contains("Google", StringComparison.OrdinalIgnoreCase)) return "settings_icon_backup.png";
        if (title.Contains("شبكة", StringComparison.Ordinal) || title.Contains("تجوال", StringComparison.Ordinal) || title.Contains("بيانات مستلمة", StringComparison.Ordinal) || title.Contains("بيانات مرسلة", StringComparison.Ordinal) || title.Contains("استخدام الشبكة", StringComparison.Ordinal)) return "settings_icon_network.png";
        if (title.Contains("تخزين", StringComparison.Ordinal) || title.Contains("ملفات", StringComparison.Ordinal) || title.Contains("بيانات التطبيق", StringComparison.Ordinal)) return "settings_icon_storage.png";
        if (title.Contains("مكالمة", StringComparison.Ordinal) || title.Contains("مكالمات", StringComparison.Ordinal) || title.Contains("هاتف", StringComparison.Ordinal) || title.Contains("رقم الهاتف", StringComparison.Ordinal)) return "settings_icon_phone.png";
        if (title.Contains("جهات", StringComparison.Ordinal) || title.Contains("مجموعة", StringComparison.Ordinal) || title.Contains("أصدقاء", StringComparison.Ordinal)) return "settings_icon_contacts.png";
        if (title.Contains("دردشة", StringComparison.Ordinal) || title.Contains("محادث", StringComparison.Ordinal) || title.Contains("رسائل", StringComparison.Ordinal) || title.Contains("فقاعة", StringComparison.Ordinal)) return "settings_icon_chat.png";
        if (title.Contains("وسائط", StringComparison.Ordinal) || title.Contains("صورة", StringComparison.Ordinal) || title.Contains("فيديو", StringComparison.Ordinal) || title.Contains("كاميرا", StringComparison.Ordinal) || title.Contains("ملصق", StringComparison.Ordinal)) return "settings_icon_media.png";
        if (title.Contains("أرشيف", StringComparison.Ordinal) || title.Contains("احتفاظ", StringComparison.Ordinal)) return "settings_icon_archive.png";
        if (title.Contains("قفل", StringComparison.Ordinal) || title.Contains("كلمة المرور", StringComparison.Ordinal)) return "settings_icon_lock.png";
        if (title.Contains("بريد", StringComparison.Ordinal) || title.Contains("إلكتروني", StringComparison.Ordinal) || title.Contains("دخول", StringComparison.Ordinal)) return "settings_icon_mail.png";
        if (title.Contains("حساب", StringComparison.Ordinal) || title.Contains("مستخدم", StringComparison.Ordinal) || title.Contains("اسم المستخدم", StringComparison.Ordinal)) return "settings_icon_user.png";
        if (title.Contains("حذف", StringComparison.Ordinal)) return "settings_icon_delete.png";
        if (title.Contains("إضافة", StringComparison.Ordinal)) return "settings_icon_plus.png";
        if (title.Contains("آخر ظهور", StringComparison.Ordinal) || title == "الحالة") return "settings_icon_eye.png";
        return "settings_icon_user.png";
    }
}
