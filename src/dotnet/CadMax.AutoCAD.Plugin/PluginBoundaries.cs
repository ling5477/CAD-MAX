using CadMax.Contracts;

namespace CadMax.AutoCAD.Plugin;

/// <summary>
/// Future AutoCAD plugin lifecycle boundary.
/// Autodesk IExtensionApplication binding belongs in a local SDK-enabled adapter.
/// </summary>
public interface IAutoCadPluginLifecycle
{
    /// <summary>Initialize plugin-owned queues and registrations.</summary>
    Task InitializeAsync(CancellationToken cancellationToken);

    /// <summary>Stop accepting work and release plugin-owned resources.</summary>
    Task TerminateAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Future document-context dispatcher boundary.
/// Implementations must marshal work to AutoCAD's supported application/document context.
/// </summary>
public interface IAutoCadDocumentDispatcher
{
    /// <summary>
    /// Queue one validated command for execution in the owning AutoCAD context.
    /// The bootstrap intentionally provides no implementation.
    /// </summary>
    Task<CadResultEnvelope> ExecuteInDocumentContextAsync(
        CadCommandRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Describes the compile-time plugin boundary without claiming an Autodesk SDK binding.
/// </summary>
public static class PluginBoundary
{
    /// <summary>Whether this repository-only build is linked to Autodesk assemblies.</summary>
    public const bool IsAutodeskSdkBound = false;
}
