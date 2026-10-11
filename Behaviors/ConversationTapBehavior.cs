using System.Windows.Input;
using Microsoft.Maui.Controls;

namespace Himo.Behaviors;

/// <summary>
/// Delivers conversation row taps directly from the Android native view.
/// This is independent of CollectionView SelectionChanged, which did not appear
/// in the user's diagnostic log. Other targets use a regular MAUI tap recognizer.
/// </summary>
public sealed class ConversationTapBehavior : Behavior<VisualElement>
{
    public static readonly BindableProperty CommandProperty = BindableProperty.Create(
        nameof(Command), typeof(ICommand), typeof(ConversationTapBehavior));

    public static readonly BindableProperty CommandParameterProperty = BindableProperty.Create(
        nameof(CommandParameter), typeof(object), typeof(ConversationTapBehavior));

    private VisualElement? _associatedElement;
    private TapGestureRecognizer? _fallbackTap;
#if ANDROID
    private Android.Views.View? _nativeView;
    private bool _wasClickable;
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
        bindable.HandlerChanged += OnHandlerChanged;
        bindable.BindingContextChanged += OnBindingContextChanged;
        BindingContext = bindable.BindingContext;
        AttachTapHandler();
    }

    protected override void OnDetachingFrom(VisualElement bindable)
    {
        bindable.HandlerChanged -= OnHandlerChanged;
        bindable.BindingContextChanged -= OnBindingContextChanged;
        DetachTapHandler();
        _associatedElement = null;
        BindingContext = null;
        base.OnDetachingFrom(bindable);
    }

    private void OnBindingContextChanged(object? sender, EventArgs e)
    {
        if (_associatedElement is not null)
            BindingContext = _associatedElement.BindingContext;
    }

    private void OnHandlerChanged(object? sender, EventArgs e) => AttachTapHandler();

    private void AttachTapHandler()
    {
        DetachTapHandler();
        var element = _associatedElement;
        if (element is null)
            return;

#if ANDROID
        if (element.Handler?.PlatformView is Android.Views.View nativeView)
        {
            _nativeView = nativeView;
            _wasClickable = nativeView.Clickable;
            nativeView.Clickable = true;
            nativeView.Click += OnNativeClick;
            System.Diagnostics.Debug.WriteLine("[Himo ChatTap] Native row click handler attached");
        }
#else
        _fallbackTap = new TapGestureRecognizer();
        _fallbackTap.Tapped += OnFallbackTapped;
        element.GestureRecognizers.Add(_fallbackTap);
#endif
    }

    private void DetachTapHandler()
    {
#if ANDROID
        if (_nativeView is not null)
        {
            _nativeView.Click -= OnNativeClick;
            _nativeView.Clickable = _wasClickable;
            _nativeView = null;
        }
#else
        if (_associatedElement is not null && _fallbackTap is not null)
            _associatedElement.GestureRecognizers.Remove(_fallbackTap);
        if (_fallbackTap is not null)
            _fallbackTap.Tapped -= OnFallbackTapped;
        _fallbackTap = null;
#endif
    }

#if ANDROID
    private void OnNativeClick(object? sender, EventArgs e) => ExecuteCommand();
#else
    private void OnFallbackTapped(object? sender, TappedEventArgs e) => ExecuteCommand();
#endif

    private void ExecuteCommand()
    {
        var command = Command;
        var parameter = CommandParameter;
        System.Diagnostics.Debug.WriteLine($"[Himo ChatTap] Click received; parameterType={parameter?.GetType().FullName ?? "(null)"}");
        if (command?.CanExecute(parameter) == true)
            command.Execute(parameter);
        else
            System.Diagnostics.Debug.WriteLine("[Himo ChatTap] Command cannot execute");
    }
}
