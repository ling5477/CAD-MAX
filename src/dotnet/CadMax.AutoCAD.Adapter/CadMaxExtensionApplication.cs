using System.Diagnostics;
using System.Reflection;
using Autodesk.AutoCAD.Runtime;
using CadMax.AutoCAD.Plugin;
using AutoCADApplication = Autodesk.AutoCAD.ApplicationServices.Core.Application;

[assembly: ExtensionApplication(typeof(CadMax.AutoCAD.Adapter.CadMaxExtensionApplication))]
[assembly: CommandClass(typeof(CadMax.AutoCAD.Adapter.CadMaxStatusCommands))]

namespace CadMax.AutoCAD.Adapter;

/// <summary>
/// AutoCAD 2025/2026 Managed .NET bootstrap adapter.
/// It forwards only process lifecycle and one fixed status command to the SDK-free core.
/// It is stateless across processes and thread-safe through the core controller. The SDK-free
/// core owns one bounded loopback listener but no Document, Database, Editor, timer, transaction,
/// or Autodesk object.
/// </summary>
public sealed class CadMaxExtensionApplication : IExtensionApplication
{
    /// <summary>
    /// Initializes the SDK-free lifecycle. Failures are classified and contained so no
    /// unhandled exception can escape into AutoCAD startup.
    /// </summary>
    public void Initialize()
    {
        try
        {
            _ = AdapterLifecycle.Initialize();
        }
        catch (System.Exception)
        {
            // AutoCAD must remain usable even if adapter bootstrap encounters an unknown fault.
        }
    }

    /// <summary>
    /// Terminates the SDK-free lifecycle. Listener shutdown is bounded and all unexpected faults
    /// are contained so AutoCAD shutdown cannot wait indefinitely.
    /// </summary>
    public void Terminate()
    {
        try
        {
            _ = AdapterLifecycle.Terminate();
        }
        catch (System.Exception)
        {
            // IExtensionApplication.Terminate must never destabilize host shutdown.
        }
    }
}

/// <summary>
/// Registers the sole manual bootstrap command. The command displays an allowlisted status DTO
/// and deliberately avoids DocumentManager, Document, Database, Editor, and arbitrary input.
/// </summary>
public sealed class CadMaxStatusCommands
{
    /// <summary>
    /// Displays plugin identity, schema, lifecycle, product year, and immutable safety flags.
    /// It is idempotent, accepts no parameters, and has no DWG or network side effect.
    /// </summary>
    [CommandMethod("CADMAXPLUGINSTATUS", CommandFlags.Modal | CommandFlags.NoHistory)]
    public static void ShowPluginStatus()
    {
        try
        {
            var statusJson = PluginJson.SerializeStatus(AdapterLifecycle.GetStatus());
            AutoCADApplication.ShowAlertDialog(statusJson);
        }
        catch (System.Exception)
        {
            // UI/reporting failure must not affect the drawing or AutoCAD process.
        }
    }
}

internal static class AdapterLifecycle
{
    private static readonly PluginMetadata Metadata = PluginMetadata.CreateDefault();
    private static readonly DocumentContextDispatcher ContextQueue = new();
    private static readonly PluginLifecycleController Controller = new(
        Metadata,
        new BoundedJsonLineEvidenceWriter(),
        contextDispatcher: ContextQueue);
    private static readonly AutoCadDocumentContextAdapter ContextAdapter = new(ContextQueue);
    private static readonly int TargetAutoCADYear = ReadTargetAutoCADYear();

    internal static bool Initialize()
    {
        if (!Controller.Initialize(CreateRuntimeInfo(), Environment.ProcessId))
        {
            return false;
        }

        if (ContextAdapter.Initialize())
        {
            return true;
        }

        _ = Controller.Terminate(Environment.ProcessId);
        return false;
    }

    internal static bool Terminate()
    {
        var contextStopped = ContextAdapter.Terminate();
        var lifecycleStopped = Controller.Terminate(Environment.ProcessId);
        return contextStopped && lifecycleStopped;
    }

    internal static PluginStatus GetStatus() =>
        Controller.GetStatus();

    private static PluginRuntimeInfo CreateRuntimeInfo() =>
        new(
            TargetAutoCADYear,
            ReadAutoCADProductVersion(),
            ReadAdapterVersion(),
            IsAutoCADHostProcess());

    private static bool IsAutoCADHostProcess()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            return string.Equals(process.ProcessName, "acad", StringComparison.OrdinalIgnoreCase);
        }
        catch (System.Exception)
        {
            return false;
        }
    }

    private static int ReadTargetAutoCADYear()
    {
        var value = typeof(AdapterLifecycle).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute =>
                string.Equals(
                    attribute.Key,
                    "CadMaxAutoCADYear",
                    StringComparison.Ordinal))?
            .Value;
        return int.TryParse(value, out var year) ? year : 0;
    }

    private static string ReadAdapterVersion() =>
        typeof(AdapterLifecycle).Assembly.GetName().Version?.ToString(3) ?? "UNKNOWN";

    private static string ReadAutoCADProductVersion()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            return process.MainModule?.FileVersionInfo.ProductVersion ?? "UNKNOWN";
        }
        catch (System.Exception)
        {
            return "UNKNOWN";
        }
    }
}
