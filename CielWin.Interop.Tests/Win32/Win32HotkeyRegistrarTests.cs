using CielWin.Interop.Win32;

namespace CielWin.Interop.Tests.Win32;

/// <summary>
/// <see cref="Win32HotkeyRegistrar"/>: global hotkeys through <c>RegisterHotKey</c> on a message-only
/// window. The pure parts (modifier values, WM_HOTKEY routing) run anywhere; the real registration
/// facts need an interactive session.
/// </summary>
public sealed class Win32HotkeyRegistrarTests
{
    /// <summary>The <c>MOD_*</c> values <c>RegisterHotKey</c> expects, bit for bit.</summary>
    [Theory]
    [InlineData(HotkeyModifiers.Alt, 0x0001u)]
    [InlineData(HotkeyModifiers.Control, 0x0002u)]
    [InlineData(HotkeyModifiers.Shift, 0x0004u)]
    [InlineData(HotkeyModifiers.Win, 0x0008u)]
    [InlineData(HotkeyModifiers.NoRepeat, 0x4000u)]
    public void ModifierValuesMatchWin32(HotkeyModifiers modifier, uint expected)
    {
        Assert.Equal(expected, (uint)modifier);
    }

    [Fact]
    public void AWmHotkeyMessage_RaisesPressedWithItsId()
    {
        using var registrar = new Win32HotkeyRegistrar();
        var pressed = new List<int>();
        registrar.Pressed += pressed.Add;

        var handled = registrar.HandleMessage(Win32HotkeyRegistrar.WmHotkey, 7);

        Assert.True(handled);
        Assert.Equal([7], pressed);
    }

    [Fact]
    public void AnyOtherMessage_RaisesNothing()
    {
        using var registrar = new Win32HotkeyRegistrar();
        var pressed = new List<int>();
        registrar.Pressed += pressed.Add;

        var handled = registrar.HandleMessage(0x0010, 7);

        Assert.False(handled);
        Assert.Empty(pressed);
    }

    [Fact]
    public void AHandlerThatThrows_DoesNotEscapeTheWindowProcedure()
    {
        using var registrar = new Win32HotkeyRegistrar();
        registrar.Pressed += _ => throw new InvalidOperationException("boom");

        var handled = registrar.HandleMessage(Win32HotkeyRegistrar.WmHotkey, 1);

        Assert.True(handled);
    }

    /// <summary>
    /// The swallowed failure is still reported: the callback gets the exception TYPE name only (a
    /// message can hold an absolute path), and later presses are still delivered.
    /// </summary>
    [Fact]
    public void AHandlerThatThrows_IsReportedByTypeName_AndLaterPressesStillArrive()
    {
        var failures = new List<string>();
        using var registrar = new Win32HotkeyRegistrar(failures.Add);
        var pressed = new List<int>();
        var fail = true;
        registrar.Pressed += id =>
        {
            if (fail) throw new InvalidOperationException("C:\\secret\\path");
            pressed.Add(id);
        };

        registrar.HandleMessage(Win32HotkeyRegistrar.WmHotkey, 1);
        fail = false;
        registrar.HandleMessage(Win32HotkeyRegistrar.WmHotkey, 2);

        Assert.Equal(["InvalidOperationException"], failures);
        Assert.Equal([2], pressed);
    }

    [Fact]
    public void AFailureCallbackThatThrows_DoesNotEscapeTheWindowProcedure()
    {
        using var registrar = new Win32HotkeyRegistrar(_ => throw new IOException("trace sink down"));
        registrar.Pressed += _ => throw new InvalidOperationException("boom");

        var handled = registrar.HandleMessage(Win32HotkeyRegistrar.WmHotkey, 1);

        Assert.True(handled);
    }

    [Fact]
    public void Register_AfterDispose_ReturnsFalse()
    {
        var registrar = new Win32HotkeyRegistrar();
        registrar.Dispose();

        Assert.False(registrar.Register(1, HotkeyModifiers.Alt, 0x4D));
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var registrar = new Win32HotkeyRegistrar();
        registrar.Dispose();
        registrar.Dispose();
    }

    /// <summary>
    /// The message-only window really routes WM_HOTKEY to <see cref="Win32HotkeyRegistrar.Pressed"/>:
    /// a synchronous SendMessage on the owning thread calls the window procedure directly.
    /// </summary>
    [RequiresDesktopSessionFact]
    public void TheMessageOnlyWindow_RoutesWmHotkeyToPressed()
    {
        using var registrar = new Win32HotkeyRegistrar();
        Assert.True(registrar.EnsureWindow());
        var pressed = new List<int>();
        registrar.Pressed += pressed.Add;

        registrar.SendToWindow(Win32HotkeyRegistrar.WmHotkey, 3);

        Assert.Equal([3], pressed);
    }

    /// <summary>
    /// A combination nobody else plausibly owns registers once; a second registrar asking for the
    /// same combination is refused; after the first is disposed the combination is free again.
    /// </summary>
    [RequiresDesktopSessionFact]
    public void RealRegistration_IsExclusiveAndReleasedOnDispose()
    {
        const uint vkF24 = 0x87;
        const HotkeyModifiers modifiers = HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift;

        var first = new Win32HotkeyRegistrar();
        Assert.True(first.Register(1, modifiers, vkF24));

        using (var second = new Win32HotkeyRegistrar())
        {
            Assert.False(second.Register(1, modifiers, vkF24));
        }

        first.Dispose();

        using var third = new Win32HotkeyRegistrar();
        Assert.True(third.Register(1, modifiers, vkF24));
    }
}
