using System.Windows.Input;
using Microsoft.Maui.Controls;

namespace Himo.Behaviors;

/// <summary>
/// Android-native long-press behavior for selecting conversation rows.
/// Does not use CommunityToolkit TouchBehavior, which crashed on the observed
/// MAUI 10 Android build with MethodAccessException in ViewExtensions.GetParentOfType.
/// </summary>
public sealed class LongPressBehavior : Behavior<VisualElement>
{
    public static readonly BindableProperty CommandProperty = BindableProperty.Create(
        nameof(Command), typeof(ICommand), typeof(LongPressBehavior));

    public static readonly BindableProperty CommandParameterProperty = BindableProperty.Create(
        nameof(CommandParameter), typeof(object), typeof(LongPressBehavior));

    private VisualElement? _associatedElement;
#if ANDROID
    private Android.Views.View? _nativeView;
#endif

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    protected override void OnAttachedTo(VisualElement bindable)
    {
        base.OnAttachedTo(bindable);
        _associatedElement = bindable;
        bindable.BindingContextChanged += OnAssociatedBindingContextChanged;
#if ANDROID
        bindable.HandlerChanged += OnHandlerChanged;
        AttachNativeView();
#endif
        BindingContext = bindable.BindingContext;
    }

    protected override void OnDetachingFrom(VisualElement bindable)
    {
        bindable.BindingContextChanged -= OnAssociatedBindingContextChanged;
#if ANDROID
        bindable.HandlerChanged -= OnHandlerChanged;
        DetachNativeView();
#endif
        _associatedElement = null;
        BindingContext = null;
        base.OnDetachingFrom(bindable);
    }

    private void OnAssociatedBindingContextChanged(object? sender, EventArgs e)
    {
        if (_associatedElement is not null)
            BindingContext = _associatedElement.BindingContext;
    }

#if ANDROID
    private void OnHandlerChanged(object? sender, EventArgs e) => AttachNativeView();

    private void AttachNativeView()
    {
        DetachNativeView();
        if (_associatedElement?.Handler?.PlatformView is not Android.Views.View view)
            return;

        _nativeView = view;
        _nativeView.LongClickable = true;
        _nativeView.LongClick += OnNativeLongClick;
    }

    private void DetachNativeView()
    {
        if (_nativeView is null)
            return;

        _nativeView.LongClick -= OnNativeLongClick;
        _nativeView.LongClickable = false;
        _nativeView = null;
    }

    private void OnNativeLongClick(object? sender, Android.Views.View.LongClickEventArgs e)
    {
        var command = Command;
        var parameter = CommandParameter;
        if (command?.CanExecute(parameter) == true)
        {
            command.Execute(parameter);
            e.Handled = true;
        }
    }
#endif
}
