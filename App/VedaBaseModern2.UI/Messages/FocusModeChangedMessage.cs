using CommunityToolkit.Mvvm.Messaging.Messages;

namespace VedaBaseModern.UI.Messages
{
    /// <summary>
    /// Sent by ReadingViewModel when Focus Mode is toggled from within Reading
    /// View. MainPage (which owns the NavigationView) is the only subscriber -
    /// it collapses/restores the nav pane. Uses WeakReferenceMessenger so no
    /// explicit unregister is required (see MILESTONE_6_SETTINGS_ARCHITECTURE.md).
    /// </summary>
    public sealed class FocusModeChangedMessage : ValueChangedMessage<bool>
    {
        public FocusModeChangedMessage(bool isFocusMode) : base(isFocusMode) { }
    }
}
