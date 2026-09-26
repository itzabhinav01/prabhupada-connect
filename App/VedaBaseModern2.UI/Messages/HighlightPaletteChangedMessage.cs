using System.Collections.Generic;
using CommunityToolkit.Mvvm.Messaging.Messages;
using VedaBaseModern.Core.Models;

namespace VedaBaseModern.UI.Messages
{
    /// <summary>
    /// Broadcast when the user modifies highlighting colors or adds/removes color slots in Settings.
    /// Handlers update live views (WebView2 reader, HighlightsPage, SearchPage, App resource brushes)
    /// without requiring an app restart or database record modifications.
    /// </summary>
    public sealed class HighlightPaletteChangedMessage : ValueChangedMessage<List<HighlightColorItem>>
    {
        public HighlightPaletteChangedMessage(List<HighlightColorItem> palette) : base(palette) { }
    }
}
