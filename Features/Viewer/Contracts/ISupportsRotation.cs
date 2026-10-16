namespace DocGlance.Features.Viewer.Contracts;

public interface ISupportsRotation
{
    double CurrentRotation { get; }
    void SetRotation(double angle);
}
