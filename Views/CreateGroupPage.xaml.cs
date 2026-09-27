using System.Collections.ObjectModel;
using Himo.Models;

namespace Himo.Views;

public partial class CreateGroupPage : ContentPage
{
    private readonly ObservableCollection<GroupMemberDraft> _members = new();

    public CreateGroupPage()
    {
        InitializeComponent();
        MembersView.ItemsSource = _members;
    }

    private async void BackClicked(object? sender, EventArgs e) => await Shell.Current.GoToAsync("..");

    private async void AddMembersClicked(object? sender, EventArgs e)
    {
        await DisplayAlertAsync("إضافة أعضاء", "واجهة اختيار الأعضاء ستُربط بالبحث والمستخدمين في المرحلة التالية.", "حسنًا");
    }

    private void RemoveMemberClicked(object? sender, EventArgs e)
    {
        if (sender is Button button && button.BindingContext is GroupMemberDraft member)
            _members.Remove(member);
    }

    private async void CreateClicked(object? sender, EventArgs e)
    {
        var name = GroupNameEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            await DisplayAlertAsync("المجموعة", "اكتب اسم المجموعة أولًا.", "حسنًا");
            return;
        }

        await DisplayAlertAsync("المجموعة", "تم تجهيز بيانات المجموعة. ربط الإنشاء بالخادم سيكون في المرحلة التالية.", "حسنًا");
        await Shell.Current.GoToAsync("..");
    }
}
