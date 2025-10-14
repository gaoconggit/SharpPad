#if MACOS
using Foundation;
using WebKit;

namespace SharpPad.Desktop.Platforms.MacOS;

/// <summary>
/// Custom WKUIDelegate implementation to handle file picker dialogs on macOS.
/// This fixes the issue where &lt;input type="file"&gt; elements don't work in WKWebView.
/// </summary>
public class CustomWKUIDelegate : WKUIDelegate
{
    public CustomWKUIDelegate()
    {
    }

    /// <summary>
    /// Handles file picker requests from web pages.
    /// This method is called when a user clicks on an &lt;input type="file"&gt; element.
    /// </summary>
    [Export("webView:runOpenPanelWithParameters:initiatedByFrame:completionHandler:")]
    public override void RunOpenPanel(
        WKWebView webView,
        WKOpenPanelParameters parameters,
        WKFrameInfo frame,
        Action<NSUrl[]?> completionHandler)
    {
        // Create a native macOS file picker
        var openPanel = new NSOpenPanel
        {
            CanChooseFiles = true,
            CanChooseDirectories = false,
            AllowsMultipleSelection = parameters.AllowsMultipleSelection
        };

        // Show the file picker dialog
        openPanel.Begin((result) =>
        {
            if (result == NSModalResponse.OK)
            {
                // User selected files - pass them to the completion handler
                completionHandler(openPanel.Urls);
            }
            else
            {
                // User cancelled - pass empty array
                completionHandler(Array.Empty<NSUrl>());
            }
        });
    }

    /// <summary>
    /// Handles JavaScript alert dialogs.
    /// </summary>
    [Export("webView:runJavaScriptAlertPanelWithMessage:initiatedByFrame:completionHandler:")]
    public override void RunJavaScriptAlertPanel(
        WKWebView webView,
        string message,
        WKFrameInfo frame,
        Action completionHandler)
    {
        var alert = new NSAlert
        {
            AlertStyle = NSAlertStyle.Informational,
            MessageText = "SharpPad",
            InformativeText = message
        };
        alert.AddButton("OK");
        alert.RunModal();
        completionHandler();
    }

    /// <summary>
    /// Handles JavaScript confirm dialogs.
    /// </summary>
    [Export("webView:runJavaScriptConfirmPanelWithMessage:initiatedByFrame:completionHandler:")]
    public override void RunJavaScriptConfirmPanel(
        WKWebView webView,
        string message,
        WKFrameInfo frame,
        Action<bool> completionHandler)
    {
        var alert = new NSAlert
        {
            AlertStyle = NSAlertStyle.Warning,
            MessageText = "SharpPad",
            InformativeText = message
        };
        alert.AddButton("OK");
        alert.AddButton("Cancel");
        var result = alert.RunModal();
        completionHandler(result == NSAlertButtonReturn.First);
    }

    /// <summary>
    /// Handles JavaScript prompt dialogs.
    /// </summary>
    [Export("webView:runJavaScriptTextInputPanelWithPrompt:defaultText:initiatedByFrame:completionHandler:")]
    public override void RunJavaScriptTextInputPanel(
        WKWebView webView,
        string prompt,
        string? defaultText,
        WKFrameInfo frame,
        Action<string?> completionHandler)
    {
        var alert = new NSAlert
        {
            AlertStyle = NSAlertStyle.Informational,
            MessageText = "SharpPad",
            InformativeText = prompt
        };
        alert.AddButton("OK");
        alert.AddButton("Cancel");

        var textField = new NSTextField
        {
            Frame = new CoreGraphics.CGRect(0, 0, 300, 24),
            StringValue = defaultText ?? string.Empty
        };
        alert.AccessoryView = textField;

        var result = alert.RunModal();
        if (result == NSAlertButtonReturn.First)
        {
            completionHandler(textField.StringValue);
        }
        else
        {
            completionHandler(null);
        }
    }
}
#endif
