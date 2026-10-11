using System.Runtime.InteropServices;
using GalaxyBridge.Core;
using GalaxyBridge.Windows;

static class ReceiveProgressWindowTests
{
    public static int Run()
    {
        int checks = 0;
        void Check(bool value, string reason) { if (!value) throw new Exception("Receive progress: " + reason);checks++; }
        object first = new(), replacement = new();
        List<FakeDialog> dialogs = [];
        using ReceiveProgressWindow window = new(() => { FakeDialog d = new();dialogs.Add(d);return d; });
        List<string> cancelled = [];
        LanReceiveState Idle() => new(false, 0, 0, "idle");
        LanReceiveState Busy(string id, long bytes = 0, long size = 100) => new(true, bytes, size, "receive", OperationId: id);
        void Update(object? source, LanReceiveState state) => window.Update(source, state, 17, cancelled.Add);
        Update(first, Idle());Check(dialogs.Count == 0, "idle does not open a dialog");
        Update(first, Busy("a", 25));
        Check(dialogs.Count == 1 && dialogs[0].Owner == 17 && dialogs[0].Last!.Bytes == 25, "real receive bytes reach modeless native backend");
        Update(first, Busy("a", 70));Check(dialogs.Count == 1 && dialogs[0].Last!.Bytes == 70, "one window updates throughout a file");
        Update(first, new(false, 100, 100, "saved", "test.txt", "a"));
        Check(dialogs[0].Disposed && dialogs[0].Last!.SavedPath == "test.txt", "verified publish completes then closes");
        Update(first, Busy("b", 7, -1));Check(dialogs[1].Last!.Size == -1, "unknown size stays unknown");
        dialogs[1].CancelRequested = true;Update(first, Busy("b", 8, -1));
        Check(cancelled.SequenceEqual(new[] { "b" }) && dialogs[1].Disposed, "shell cancel closes and targets only its operation");
        Update(first, Busy("b", 8, -1));Check(dialogs.Count == 2 && cancelled.Count == 1, "cancelled snapshot cannot reopen or repeat cancellation");
        Update(first, Busy("c"));Check(dialogs.Count == 3, "next operation can open after cancellation");
        Update(replacement, Busy("c", 30));
        Check(dialogs[2].Disposed && dialogs.Count == 4 && cancelled.Count == 1, "replacement session closes old window without cancelling new transfer");
        Update(null, Busy("c", 30));Check(dialogs[3].Disposed, "disconnect closes progress without reporting success");
        Update(first, Busy("d"));Update(first, new(false, 42, 100, "hash failed", OperationId: "d"));
        Check(dialogs[4].Disposed && dialogs[4].Last!.Busy, "failed hash never completes progress as a saved file");
        Update(first, Busy("e", 10));Update(first, Busy("f", 0));
        Check(dialogs[5].Disposed && dialogs.Count == 7 && dialogs[6].Last!.Bytes == 0, "back-to-back transfers reset progress even without idle poll");
        dialogs[6].FailUpdate = true;Update(first, Busy("f", 12));
        Check(dialogs[6].Disposed && cancelled.Count == 1, "native update failure preserves the network transfer");
        Update(first, Busy("f", 20));Check(dialogs.Count == 7, "failed shell UI is not recreated every timer tick");
        Update(first, Busy("g"));Check(dialogs.Count == 8, "next transfer retries shell creation");
        window.Dispose();Update(first, Busy("h"));Check(dialogs[7].Disposed && dialogs.Count == 8, "application shutdown disposes and prevents revival");
        int attempts = 0;
        using ReceiveProgressWindow unavailable = new(() => { attempts++;throw new COMException("Unavailable shell"); });
        unavailable.Update(first, Busy("i"), 0, cancelled.Add);unavailable.Update(first, Busy("i", 20), 0, cancelled.Add);
        Check(attempts == 1 && cancelled.Count == 1, "shell creation failure falls back without affecting file receipt");
        FakeDialog denied = new() { FailStart = true };
        int deniedAttempts = 0;
        using ReceiveProgressWindow deniedWindow = new(() => { deniedAttempts++;return denied; });
        deniedWindow.Update(first, Busy("j"), 0, cancelled.Add);
        Check(denied.Disposed && cancelled.Count == 1, "denied native start releases its backend without cancelling receipt");
        deniedWindow.Update(first, Busy("j", 20), 0, cancelled.Add);
        Check(deniedAttempts == 1, "denied start is not retried on every poll");
        return checks;
    }
    private sealed class FakeDialog : IShellReceiveProgress
    {
        public nint Owner;
        public bool Disposed, CancelRequested, FailUpdate, FailStart;
        public LanReceiveState? Last;
        public bool Cancelled => CancelRequested;
        public void Start(nint owner) { if (FailStart) throw new UnauthorizedAccessException("Shell UI denied");Owner = owner; }
        public void Update(LanReceiveState state)
        { if (FailUpdate) throw new COMException("Update failed");Last = state; }
        public void Dispose() => Disposed = true;
    }
}
