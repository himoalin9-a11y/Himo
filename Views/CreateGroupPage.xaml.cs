using System.Collections.ObjectModel;
using Himo.Models;
using Himo.Services;
using Himo.ViewModels;
using Microsoft.Maui.Controls;

namespace Himo.Views;

[QueryProperty(nameof(ExistingGroupId), "groupId")]
[QueryProperty(nameof(ExistingGroupName), "groupName")]
public partial class CreateGroupPage : ContentPage
{
    private readonly ObservableCollection<GroupMemberDraft> _members = new();
    private readonly HimoApiClient _api;
    private readonly HomeViewModel _home;
    private bool _searching;
    private bool _creating;
    private string? _existingGroupId;
    private string? _existingGroupName;

    public string ExistingGroupId
    {
        get => _existingGroupId ?? string.Empty;
        set
        {
            _existingGroupId = value?.Trim();
            UpdateModeUi();
        }
    }

    public string ExistingGroupName
    {
        get => _existingGroupName ?? string.Empty;
        set
        {
            _existingGroupName = value?.Trim();
            UpdateModeUi();
        }
    }

    private bool IsAddingToExistingGroup =>
        Guid.TryParse(_existingGroupId, out var id) && id != Guid.Empty;

    public CreateGroupPage(HimoApiClient api, HomeViewModel home)
    {
        InitializeComponent();
        _api = api;
        _home = home;
        MembersView.ItemsSource = _members;
        UpdateModeUi();
        UpdateMembersCount();
    }

    private void UpdateModeUi()
    {
        // Query properties are assigned by Shell after the page constructor.
        if (GroupNameContainer is null || GroupTitleLabel is null || GroupSubtitleLabel is null ||
            MemberLookupEntry is null || CreateGroupButton is null)
            return;

        var adding = IsAddingToExistingGroup;
        GroupNameContainer.IsVisible = !adding;
        GroupTitleLabel.Text = adding ? "إضافة أعضاء للمجموعة" : "إنشاء مجموعة";
        GroupSubtitleLabel.Text = adding
            ? (string.IsNullOrWhiteSpace(_existingGroupName)
                ? "أضف أشخاصًا إلى المجموعة الحالية"
                : $"إضافة أشخاص إلى {_existingGroupName}")
            : "ابدأ محادثة جماعية مع أصدقائك";
        MemberLookupEntry.Placeholder = "الاسم أو البريد الإلكتروني أو رقم الهاتف";
        CreateGroupButton.Text = adding ? "إضافة الأعضاء" : "إنشاء المجموعة";
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
            await DisplayAlertAsync("إضافة عضو", "أدخل ثلاثة أحرف على الأقل من الاسم، أو بريدًا إلكترونيًا أو رقم هاتف صحيحًا.", "حسنًا");
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
                var labels = newMatches.Select(u => string.IsNullOrWhiteSpace(u.Email)
                    ? u.Name
                    : $"{u.Name} — {u.Email}").ToArray();
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
        if (sender is Element element && element.BindingContext is GroupMemberDraft member)
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

        var addingToExisting = IsAddingToExistingGroup;
        var name = GroupNameEntry.Text?.Trim() ?? string.Empty;
        if (!addingToExisting && (name.Length is < 2 or > 60))
        {
            await DisplayAlertAsync("المجموعة", "اكتب اسمًا من حرفين إلى 60 حرفًا.", "حسنًا");
            return;
        }
        if (_members.Count == 0)
        {
            await DisplayAlertAsync(addingToExisting ? "إضافة أعضاء" : "المجموعة",
                "أضف شخصًا واحدًا على الأقل قبل المتابعة.", "حسنًا");
            return;
        }

        try
        {
            _creating = true;
            CreateGroupButton.IsEnabled = false;
            FindMemberButton.IsEnabled = false;
            GroupBusyIndicator.IsVisible = true;
            GroupBusyIndicator.IsRunning = true;
            CreateGroupButton.Text = addingToExisting ? "جارٍ إضافة الأعضاء..." : "جارٍ إنشاء المجموعة...";

            var userIds = _members.Select(member => member.UserId).Distinct().ToArray();
            if (addingToExisting)
            {
                if (!Guid.TryParse(_existingGroupId, out var existingGroupId) || existingGroupId == Guid.Empty)
                    throw new InvalidOperationException("معرّف المجموعة الحالية غير صالح.");

                var result = await _api.AddGroupMembersAsync(existingGroupId, userIds);
                try { await _home.RefreshFromServerAsync(); }
                catch (Exception refreshError)
                {
                    System.Diagnostics.Debug.WriteLine($"[Himo Groups] Refresh after adding members failed: {refreshError}");
                }

                var resultMessage = result.AddedCount > 0
                    ? $"تمت إضافة {result.AddedCount} عضو إلى المجموعة."
                    : "الأشخاص المحددون أعضاء في المجموعة بالفعل؛ لم تتم إضافة أعضاء جدد.";
                await DisplayAlertAsync("إضافة أعضاء", resultMessage, "حسنًا");
                if (Shell.Current is { } existingShell)
                    await existingShell.GoToAsync("..", false);
                return;
            }

            var group = await _api.CreateGroupAsync(name, userIds);
            try { await _home.RefreshFromServerAsync(); }
            catch (Exception refreshError)
            {
                System.Diagnostics.Debug.WriteLine($"[Himo Groups] Refresh after group creation failed: {refreshError}");
            }

            var shell = Shell.Current;
            if (shell is null)
            {
                await DisplayAlertAsync("المجموعة", "تم إنشاء المجموعة بنجاح، لكن تعذر فتح المحادثة الآن. ارجع إلى قائمة المحادثات وحدّثها.", "حسنًا");
                return;
            }

            await shell.GoToAsync($"chat?id={group.Id:D}", false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo Groups] Create/add failed: {ex}");
            await DisplayAlertAsync(addingToExisting ? "تعذرت إضافة الأعضاء" : "تعذر إنشاء المجموعة", ex.Message, "حسنًا");
        }
        finally
        {
            _creating = false;
            GroupBusyIndicator.IsRunning = false;
            GroupBusyIndicator.IsVisible = false;
            FindMemberButton.IsEnabled = true;
            UpdateModeUi();
            UpdateMembersCount();
        }
    }

}
