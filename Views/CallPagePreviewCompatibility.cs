using Microsoft.Maui.Controls;

namespace Himo.Views;

/// <summary>
/// Keeps the movable/zoomable local-preview name expected by CallPage's gesture
/// handlers mapped to the same Border that the call layout uses for sizing/placement.
/// This avoids duplicating the WebRTC renderer or changing call-state logic.
/// </summary>
public partial class CallPage
{
    private Border LocalPreviewFrame => LocalVideoContainer;
}
