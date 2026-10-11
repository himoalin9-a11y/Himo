using System.Collections.ObjectModel;
using Himo.Models;
using Himo.Services;
using Himo.ViewModels;

namespace Himo.Views;

public partial class CreateGroupPage : ContentPage
{
    private readonly ObservableCollection<GroupMemberDraft> _members = new();
    private readonly HimoApiClient _api;
    private readonly HomeViewModel _home;
    private bool _searching;
    private bool _creating;

    public CreateGroupPage(HimoApiClient api, HomeViewModel home)
    {
        InitializeComponent();
        _api = api;
        _home = home;
        MembersView.ItemsSource = _members;
        UpdateMembersCount();
    }

    private async void BackClicked(object? sender, EventArgs e)
    {
        if (Shell.Current is not null)
            await Shell.Current.GoToAsync("..", false);
    }

    private async void MemberLookupCompleted(object? sender, EventArgs e)
        => await FindMemberAsync();

    private async void FindMemberClicked(object? sender, EventArgs e)
        => await FindMemberAsync();

    private async Task FindMemberAsync()
    {
        if (_searching || _creating) return;

        var query = MemberLookupEntry.Text?.Trim() ?? string.Empty;
        if (query.Length < 3)
        {
            await DisplayAlertAsync("إضافة عضو", "أدخل بريدًا إلكترونيًا أو رقم هاتف صحيحًا.", "حسنًا");
            return;
        }

        try
        {
            _searching = true;
            FindMemberButton.IsEnabled = false;
            GroupBusyIndicator.IsVisible = true;
            GroupBusyIndicator.IsRunning = true;

            var matches = await _api.SearchGroupMembersAsync(query);
            var newMatches = matches.Where(candidate =>
                    candidate.Id != Guid.Empty &&
                    !_members.Any(existing => existing.UserId == candidate.Id))
                .ToArray();

            if (newMatches.Length == 0)
            {
                await DisplayAlertAsync("إضافة عضو", matches.Count == 0
                    ? "لم نعثر على حساب مطابق. تأكد من الرقم أو البريد وأن الحساب مسجل في Himo."
                    : "تمت إضافة جميع الحسابات المطابقة بالفعل.", "حسنًا");
                return;
            }

            UserSearchDto? selected;
            if (newMatches.Length == 1)
            {
                selected = newMatches[0];
            }
            else
            {
                var labels = newMatches.Select(u => $"{u.Name} — {u.Email}").ToArray();
                var choice = await DisplayActionSheetAsync("اختر العضو", "إلغاء", null, labels);
                var index = choice is null ? -1 : Array.IndexOf(labels, choice);
                selected = index >= 0 ? newMatches[index] : null;
            }

            if (selected is not null)
            {
                _members.Add(new GroupMemberDraft
                {
                    UserId = selected.Id,
                    Name = selected.Name,
                    Email = selected.Email
                });
                MemberLookupEntry.Text = string.Empty;
                UpdateMembersCount();
            }
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("إضافة عضو", ex.Message, "حسنًا");
        }
        finally
        {
            _searching = false;
            FindMemberButton.IsEnabled = true;
            GroupBusyIndicator.IsRunning = false;
            GroupBusyIndicator.IsVisible = false;
        }
    }

    private void RemoveMemberClicked(object? sender, EventArgs e)
    {
        if (_creating) return;
        if (sender is Button button && button.BindingContext is GroupMemberDraft member)
        {
            _members.Remove(member);
            UpdateMembersCount();
        }
    }

    private void UpdateMembersCount()
    {
        if (MembersCountLabel is not null)
            MembersCountLabel.Text = $"{_members.Count} عضو";
        if (CreateGroupButton is not null)
            CreateGroupButton.IsEnabled = !_creating && _members.Count > 0;
    }

    private async void CreateClicked(object? sender, EventArgs e)
    {
        if (_creating) return;

        var name = GroupNameEntry.Text?.Trim() ?? string.Empty;
        if (name.Length is < 2 or > 60)
        {
            await DisplayAlertAsync("المجموعة", "اكتب اسمًا من حرفين إلى 60 حرفًا.", "حسنًا");
            return;
        }
        if (_members.Count == 0)
        {
            await DisplayAlertAsync("المجموعة", "أضف عضوًا واحدًا على الأقل قبل إنشاء المجموعة.", "حسنًا");
            return;
        }

        try
        {
            _creating = true;
            CreateGroupButton.IsEnabled = false;
            FindMemberButton.IsEnabled = false;
            GroupBusyIndicator.IsVisible = true;
            GroupBusyIndicator.IsRunning = true;
            CreateGroupButton.Text = "جارٍ إنشاء المجموعة...";

            var group = await _api.CreateGroupAsync(name, _members.Select(member => member.UserId).ToArray());

            // Reload the server conversation list to seed the local cache before opening ChatPage.
            await _home.RefreshFromServerAsync();
            var shell = Shell.Current;
            if (shell is null)
            {
                await DisplayAlertAsync("المجموعة", "تم إنشاء المجموعة، لكن تعذر فتح المحادثة الآن. ستظهر في قائمة المحادثات بعد التحديث.", "حسنًا");
                return;
            }

            await shell.GoToAsync($"chat?id={group.Id:D}", false);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("تعذر إنشاء المجموعة", ex.Message, "حسنًا");
        }
        finally
        {
            _creating = false;
            GroupBusyIndicator.IsRunning = false;
            GroupBusyIndicator.IsVisible = false;
            CreateGroupButton.Text = "إنشاء المجموعة";
            FindMemberButton.IsEnabled = true;
            UpdateMembersCount();
        }
    }
}
