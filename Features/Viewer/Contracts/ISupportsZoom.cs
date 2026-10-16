using System;
using DocGlance.Core.Enums;

namespace DocGlance.Features.Viewer.Contracts;

public interface ISupportsZoom
{
    event EventHandler<double>? ZoomChanged;
    double CurrentZoom { get; }
    FitMode CurrentFitMode { get; }
    void SetZoom(double zoom);
    void SetFitMode(FitMode mode);
}
