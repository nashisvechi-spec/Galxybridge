using System.Runtime.InteropServices;
using GalaxyBridge.Core;

namespace GalaxyBridge.Windows;

internal interface IShellReceiveProgress : IDisposable
{
    void Start(nint owner);
    void Update(LanReceiveState state);
    bool Cancelled { get; }
}

// UI-thread presentation only. The TLS reader continues writing and hashing independently.
internal sealed class ReceiveProgressWindow(Func<IShellReceiveProgress> create) : IDisposable
{
    private IShellReceiveProgress? dialog;
    private object? source, suppressedSource;
    private string? transfer, suppressedTransfer;
    private bool disposed;

    public void Update(object? current, LanReceiveState state, nint owner, Action<string> cancel)
    {
        if (disposed) return;
        if (current is null || !state.Busy || state.OperationId is null)
        {
            // Only a verified publish is a successful completion, never cancellation or disconnect.
            if (current == source && state.OperationId == transfer && state.SavedPath is not null && dialog is not null)
                try { dialog.Update(state); } catch (Exception ex) when (ShellFailure(ex)) { }
            Close();return;
        }
        if (current != source || state.OperationId != transfer)
        {
            Close();source = current;transfer = state.OperationId;
        }
        if (current == suppressedSource && state.OperationId == suppressedTransfer) return;
        try
        {
            if (dialog is null) { dialog = create();dialog.Start(owner); }
            if (dialog.Cancelled)
            {
                string id = state.OperationId;
                Suppress(current, id);Close();cancel(id);return;
            }
            dialog.Update(state);
        }
        catch (Exception ex) when (ShellFailure(ex))
        {
            // Shell UI failure does not interrupt a working transfer. The existing status stays available.
            Suppress(current, state.OperationId);Close();
        }
    }
    private void Suppress(object current, string id) { suppressedSource = current;suppressedTransfer = id; }
    private static bool ShellFailure(Exception ex) => ex is ExternalException or InvalidCastException or UnauthorizedAccessException;
    private void Close()
    {
        IShellReceiveProgress? old = dialog;dialog = null;source = null;transfer = null;
        try { old?.Dispose(); } catch (Exception ex) when (ShellFailure(ex)) { }
    }
    public void Dispose() { disposed = true;Close();suppressedSource = null;suppressedTransfer = null; }
}
