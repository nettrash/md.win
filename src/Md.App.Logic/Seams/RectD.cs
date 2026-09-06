namespace Md.App.Logic.Seams;

/// <summary>A CSS-pixel rectangle (the rich-element rects the EPUB snapshotter reads, §7.5).
/// FROZEN — shell-final.md §13.2.</summary>
public readonly record struct RectD(double X, double Y, double Width, double Height);
