using CielWin.App.Composition;

namespace CielWin.App.Tests.Composition;

/// <summary>
/// <see cref="ProductionComposition.TraceStartupFailure{T}"/>: the seam <see cref="ProductionComposition.Wire"/>
/// runs its wiring through, so a startup that throws leaves a trace line before the process goes
/// down. Observability only: the failure still propagates unchanged.
/// </summary>
public sealed class StartupFailureTraceTests
{
    [Fact]
    public void AThrowingWire_IsTracedByTypeName_AndRethrownUnchanged()
    {
        var trace = new List<string>();
        var failure = new InvalidOperationException("C:\\secret\\path");

        var thrown = Assert.Throws<InvalidOperationException>(
            () => ProductionComposition.TraceStartupFailure<object>(() => throw failure, trace.Add));

        Assert.Same(failure, thrown);
        var line = Assert.Single(trace);
        Assert.Equal("startup wire-failed error=InvalidOperationException", line);
    }

    [Fact]
    public void ASuccessfulWire_ReturnsItsResult_WithoutTracing()
    {
        var trace = new List<string>();
        var result = new object();

        var returned = ProductionComposition.TraceStartupFailure(() => result, trace.Add);

        Assert.Same(result, returned);
        Assert.Empty(trace);
    }

    [Fact]
    public void ATraceThatAlsoThrows_StillLetsTheOriginalFailurePropagate()
    {
        var failure = new InvalidOperationException("boom");

        var thrown = Assert.Throws<InvalidOperationException>(
            () => ProductionComposition.TraceStartupFailure<object>(
                () => throw failure, _ => throw new IOException("sink down")));

        Assert.Same(failure, thrown);
    }
}
