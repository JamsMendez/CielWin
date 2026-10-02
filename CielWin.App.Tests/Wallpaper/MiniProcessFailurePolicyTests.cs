using CielWin.App.Wallpaper;
using Microsoft.Web.WebView2.Core;

namespace CielWin.App.Tests.Wallpaper;

/// <summary>
/// Only a failure that leaves the page unusable may spend the bounded recovery budget: WebView2
/// restarts its own helper processes, so a GPU or utility exit must not tear the scene down.
/// </summary>
public sealed class MiniProcessFailurePolicyTests
{
    [Theory]
    [InlineData(CoreWebView2ProcessFailedKind.BrowserProcessExited)]
    [InlineData(CoreWebView2ProcessFailedKind.RenderProcessExited)]
    [InlineData(CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)]
    [InlineData(CoreWebView2ProcessFailedKind.UnknownProcessExited)]
    [InlineData((CoreWebView2ProcessFailedKind)9999)]
    public void PageFatalOrUnknownKinds_RequireRecovery(CoreWebView2ProcessFailedKind kind) =>
        Assert.True(MiniProcessFailurePolicy.RequiresRecovery(kind));

    [Theory]
    [InlineData(CoreWebView2ProcessFailedKind.FrameRenderProcessExited)]
    [InlineData(CoreWebView2ProcessFailedKind.UtilityProcessExited)]
    [InlineData(CoreWebView2ProcessFailedKind.SandboxHelperProcessExited)]
    [InlineData(CoreWebView2ProcessFailedKind.GpuProcessExited)]
    [InlineData(CoreWebView2ProcessFailedKind.PpapiPluginProcessExited)]
    [InlineData(CoreWebView2ProcessFailedKind.PpapiBrokerProcessExited)]
    public void HelperProcessExits_DoNotRequireRecovery(CoreWebView2ProcessFailedKind kind) =>
        Assert.False(MiniProcessFailurePolicy.RequiresRecovery(kind));
}
