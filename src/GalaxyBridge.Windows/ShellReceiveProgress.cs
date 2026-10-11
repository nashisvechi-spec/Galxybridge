using System.Runtime.InteropServices;
using GalaxyBridge.Core;

namespace GalaxyBridge.Windows;

// Native Vista-and-later Shell operations dialog. No simulated copy or second disk copy.
internal sealed class ShellReceiveProgress : IShellReceiveProgress
{
    private IOperationsProgressDialog? dialog;
    private bool started;
    private uint mode = uint.MaxValue;
    public void Start(nint owner)
    {
        Guid clsid = new("F8383852-FCD3-11D1-A6B9-006097DF5BD4");
        Guid iid = typeof(IOperationsProgressDialog).GUID;
        Marshal.ThrowExceptionForHR(CoCreateInstance(ref clsid, 0, 1, ref iid, out var created));
        dialog = created;
        // Modeless, Cancel enabled, Pause omitted because the wire protocol cannot pause.
        // Android content URIs are not Windows Shell paths; do not invent source locations.
        dialog.StartProgressDialog(owner, 0x1000);started = true;
        dialog.SetOperation(2); // SPACTION_COPYING
    }
    public void Update(LanReceiveState state)
    {
        IOperationsProgressDialog current = dialog ?? throw new InvalidOperationException("Progress dialog is not open.");
        bool complete = !state.Busy && state.SavedPath is not null;
        uint nextMode = state.Size > 0 || complete ? 1u : 0x11u; // RUN, optionally INDETERMINATE
        if (mode != nextMode) { current.SetMode(nextMode);mode = nextMode; }
        ulong bytes = (ulong)Math.Max(0, state.Bytes), total = (ulong)Math.Max(0, state.Size);
        current.UpdateProgress(total == 0 ? (complete ? 1ul : 0ul) : bytes, Math.Max(1ul, total),
            bytes, total, complete ? 1ul : 0ul, 1);
    }
    public bool Cancelled
    {
        get { dialog!.GetOperationStatus(out uint status);return status is 3 or 4; }
    }
    public void Dispose()
    {
        IOperationsProgressDialog? old = dialog;dialog = null;
        if (old is null) return;
        try { if (started) old.StopProgressDialog(); }
        catch (COMException) { }
        finally { started = false;Marshal.FinalReleaseComObject(old); }
    }
    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoCreateInstance(ref Guid clsid, nint outer, uint context, ref Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out IOperationsProgressDialog result);

    // Method order and GUID from Microsoft's ShObjIdl_core.h; PreserveSig is deliberately absent.
    [ComImport, Guid("0C9FB851-E5C9-43EB-A370-F0677B13874C"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IOperationsProgressDialog
    {
        void StartProgressDialog(nint owner, uint flags);
        void StopProgressDialog();
        void SetOperation(uint action);
        void SetMode(uint mode);
        void UpdateProgress(ulong pointsCurrent, ulong pointsTotal, ulong sizeCurrent, ulong sizeTotal, ulong itemsCurrent, ulong itemsTotal);
        void UpdateLocations(nint source, nint target, nint item);
        void ResetTimer();
        void PauseTimer();
        void ResumeTimer();
        void GetMilliseconds(out ulong elapsed, out ulong remaining);
        void GetOperationStatus(out uint status);
    }
}
