using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Controls.Shapes;

namespace Himo.Views;

internal sealed record DeleteConversationsConfirmationResult(bool Confirmed, bool LeaveGroups);

/// <summary>
/// Small in-app confirmation sheet. The optional checkbox is shown only when the
/// selected conversations include groups; checking it leaves those groups as part
/// of the same delete operation.
/// </summary>
internal sealed class DeleteConversationsConfirmationPage : ContentPage
{
    private readonly TaskCompletionSource<DeleteConversationsConfirmationResult?> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CheckBox? _leaveGroupsCheckBox;
    private int _isClosing;

    private DeleteConversationsConfirmationPage(string title, string message, int groupCount)
    {
        Shell.SetNavBarIsVisible(this, false);
        NavigationPage.SetHasNavigationBar(this, false);
        BackgroundColor = Colors.Transparent;

        var titleLabel = new Label
        {
            Text = title,
            FontSize = 18,
            FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Start,
            FlowDirection = FlowDirection.RightToLeft
        };
        titleLabel.SetDynamicResource(Label.TextColorProperty, "HimoPrimary");

        var messageLabel = new Label
        {
            Text = message,
            FontSize = 13,
            LineBreakMode = LineBreakMode.WordWrap,
            HorizontalTextAlignment = TextAlignment.Start,
            FlowDirection = FlowDirection.RightToLeft
        };
        messageLabel.SetDynamicResource(Label.TextColorProperty, "HimoTextSecondary");

        var content = new VerticalStackLayout
        {
            Spacing = 14,
            FlowDirection = FlowDirection.RightToLeft
        };
        content.Children.Add(titleLabel);
        content.Children.Add(messageLabel);

        if (groupCount > 0)
        {
            var leaveGroupsCheckBox = new CheckBox
            {
                IsChecked = false,
                Color = Color.FromArgb("#A12AFF"),
                VerticalOptions = LayoutOptions.Center,
                HorizontalOptions = LayoutOptions.End
            };
            _leaveGroupsCheckBox = leaveGroupsCheckBox;

            var leaveLabel = new Label
            {
                Text = groupCount == 1
                    ? "هل تريد الخروج من المجموعة؟"
                    : $"هل تريد الخروج من المجموعات المحددة؟ ({groupCount})",
                FontSize = 13,
                VerticalTextAlignment = TextAlignment.Center,
                HorizontalTextAlignment = TextAlignment.Start,
                FlowDirection = FlowDirection.RightToLeft
            };
            leaveLabel.SetDynamicResource(Label.TextColorProperty, "HimoPrimary");

            var optionGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection
                {
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(new GridLength(42))
                },
                ColumnSpacing = 8,
                Padding = new Thickness(11, 8),
                FlowDirection = FlowDirection.RightToLeft,
                BackgroundColor = Color.FromArgb("#241330")
            };
            optionGrid.Add(leaveLabel, 0, 0);
            optionGrid.Add(leaveGroupsCheckBox, 1, 0);
            // Tapping the label toggles the checkbox; the checkbox keeps its native
            // tap handling so a single touch cannot toggle it twice through bubbling.
            leaveLabel.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(() =>
                    leaveGroupsCheckBox.IsChecked = !leaveGroupsCheckBox.IsChecked)
            });
            content.Children.Add(optionGrid);
        }

        var cancelButton = new Button
        {
            Text = "إلغاء",
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            HeightRequest = 46,
            CornerRadius = 14,
            BackgroundColor = Color.FromArgb("#2B1A3B"),
            TextColor = Colors.White
        };
        cancelButton.Clicked += async (_, _) => await CloseAsync(new DeleteConversationsConfirmationResult(false, false));

        var deleteButton = new Button
        {
            Text = "حذف",
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            HeightRequest = 46,
            CornerRadius = 14,
            BackgroundColor = Color.FromArgb("#C8405B"),
            TextColor = Colors.White
        };
        deleteButton.Clicked += async (_, _) =>
            await CloseAsync(new DeleteConversationsConfirmationResult(true, _leaveGroupsCheckBox?.IsChecked == true));

        var buttons = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 10,
            FlowDirection = FlowDirection.LeftToRight
        };
        buttons.Add(cancelButton, 0, 0);
        buttons.Add(deleteButton, 1, 0);
        content.Children.Add(buttons);

        var card = new Border
        {
            Padding = new Thickness(18),
            WidthRequest = 340,
            MaximumWidthRequest = 420,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            BackgroundColor = Color.FromArgb("#170B27"),
            Stroke = new SolidColorBrush(Color.FromArgb("#54316E")),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(21) },
            Content = content
        };

        var root = new Grid
        {
            Padding = new Thickness(20),
            BackgroundColor = Color.FromRgba(0, 0, 0, 190)
        };
        root.Children.Add(card);
        Content = root;
    }

    public static async Task<DeleteConversationsConfirmationResult?> ShowAsync(
        Page owner,
        string title,
        string message,
        int groupCount)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var page = new DeleteConversationsConfirmationPage(title, message, groupCount);
        await owner.Navigation.PushModalAsync(page, animated: true);
        return await page._completion.Task;
    }

    private async Task CloseAsync(DeleteConversationsConfirmationResult? result)
    {
        if (Interlocked.Exchange(ref _isClosing, 1) != 0)
            return;

        try
        {
            if (Navigation.ModalStack.Count > 0)
                await Navigation.PopModalAsync(animated: true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo Delete Confirmation] Modal close failed: {ex.Message}");
        }
        finally
        {
            _completion.TrySetResult(result);
        }
    }

    protected override bool OnBackButtonPressed()
    {
        _ = CloseAsync(null);
        return true;
    }
}
