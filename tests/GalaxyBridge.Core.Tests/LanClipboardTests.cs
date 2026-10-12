using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using GalaxyBridge.Core;
using GalaxyBridge.Windows;

static class LanClipboardTests
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        await HandshakeFailureTestsAsync(check);
        Console.WriteLine("LAN clipboard: creating TLS test connection (10 second deadline)");
        await using var peer = await Peer.CreateAsync();
        Console.WriteLine("LAN clipboard: TLS ready; checking delivery and rejection");
        string pasted = "  Привет 🌍\nhttps://example.com/?q=текст";
        check(peer.Session.Paste(pasted), "LAN paste is queued without a legacy binary packet");
        using (JsonDocument delivered = await peer.NextAsync())
            check(delivered.RootElement.GetProperty("type").GetString() == "text" &&
                delivered.RootElement.GetProperty("text").GetString() == pasted, "direct LAN paste preserves exact Unicode and whitespace");
        check(peer.Session.Paste(new string('Я', 8000)), "paste allows 16000 UTF-8 bytes");
        using (JsonDocument delivered = await peer.NextAsync())
            check(delivered.RootElement.GetProperty("text").GetString() == new string('Я', 8000), "paste sends complete text at the byte limit");
        bool rejectedPaste = false;
        try { peer.Session.Paste(new string('Я', 8001)); } catch (ArgumentException) { rejectedPaste = true; }
        check(rejectedPaste && peer.Session.IsAlive, "oversized UTF-8 paste rejects without breaking LAN");
        string? copied = null; int writes = 0;
        peer.Session.ConfigureClipboard(true);
        peer.Session.ClipboardWriter = (text, ct) => { copied = text; writes++; return Task.FromResult(true); };
        foreach (string text in new[] { "Текст с телефона 🌍\n", "https://example.com/?q=икона&n=2" })
        {
            string id = Guid.NewGuid().ToString("N");
            using JsonDocument reply = await peer.RequestAsync(id, text);
            check(reply.RootElement.GetProperty("type").GetString() == "clipboardAck", "actual LAN session acknowledges clipboard packet");
            check(reply.RootElement.GetProperty("id").GetString() == id, "clipboard acknowledgement identifies the request");
            check(reply.RootElement.GetProperty("ok").GetBoolean() && copied == text, "clipboard success follows exact Unicode text delivery");
        }
        int before = writes; peer.Session.ConfigureClipboard(false);
        using (JsonDocument reply = await peer.RequestAsync(Guid.NewGuid().ToString("N"), "must not replace clipboard"))
            check(!reply.RootElement.GetProperty("ok").GetBoolean() && writes == before, "disabled clipboard rejects without writing");
        peer.Session.ConfigureClipboard(true);
        peer.Session.ClipboardWriter = (text, ct) => Task.FromResult(false);
        using (JsonDocument reply = await peer.RequestAsync(Guid.NewGuid().ToString("N"), "busy clipboard"))
            check(!reply.RootElement.GetProperty("ok").GetBoolean() && peer.Session.IsAlive, "busy clipboard returns failure without breaking connection");
        peer.Session.ClipboardWriter = async (text, ct) => { await Task.Delay(10000, ct); return true; };
        using (JsonDocument reply = await peer.RequestAsync(Guid.NewGuid().ToString("N"), "timed out clipboard"))
            check(!reply.RootElement.GetProperty("ok").GetBoolean() && peer.Session.IsAlive, "clipboard deadline rejects delayed UI write without disconnecting");
        peer.Session.ClipboardWriter = (text, ct) => { writes++; copied = text; return Task.FromResult(true); };
        using (JsonDocument reply = await peer.RequestAsync(Guid.NewGuid().ToString("N"), "after timeout"))
            check(reply.RootElement.GetProperty("ok").GetBoolean() && writes == before + 1 && copied == "after timeout", "timed out clipboard is not replayed and next request succeeds");
        peer.Session.ClipboardWriter = null;
        using (JsonDocument reply = await peer.RequestAsync(Guid.NewGuid().ToString("N"), "no UI receiver"))
            check(!reply.RootElement.GetProperty("ok").GetBoolean(), "missing UI receiver cannot report success");
        await peer.Stream.WriteAsync(LanProtocol.Encode(new { type = "clipboard", id = "invalid", text = "reject" }));
        await peer.Session.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        check(!peer.Session.IsAlive, "malformed clipboard request closes protocol session");
        await AutomaticAsync(check);
        await NativeEdgeAsync(check);
        await ControlDisplayAsync(check);
        await LegacyMouseAsync(check);
        await FileDragAsync(check);
        await FileEdgeConfigurationAsync(check);
        await SendProgressAsync(check);
        await InvalidDragRequestsAsync(check);
    }
    private static async Task ControlDisplayAsync(Action<bool, string> check)
    {
        await using var peer = await Peer.CreateAsync();
        peer.Session.ClipboardWriter = (_, _) => Task.FromResult(true);peer.Session.ConfigureClipboard(true);
        int changed = 0;peer.Session.ControlDisplayChanged += () => changed++;
        async Task Deliver(object value)
        {
            await peer.Stream.WriteAsync(LanProtocol.Encode(value));
            using var barrier = await peer.RequestAsync(Guid.NewGuid().ToString("N"), "display barrier");
            check(barrier.RootElement.GetProperty("ok").GetBoolean(), "display metadata does not interrupt ordinary TLS requests");
        }
        await Deliver(new { type = "pong", ready = true });
        check(peer.Session.InputReady && !peer.Session.PreferWifiInput && changed == 0,"legacy phone readiness preserves existing transport policy");
        await Deliver(new { type = "pong", ready = true, controlDisplay = 9, controlRevision = 1, desktopInput = true });
        check(peer.Session.PreferWifiInput && changed == 0,"first DeX metadata chooses Wi-Fi without a false display-switch event");
        int epoch = peer.Session.BeginEdgeReturn(PhoneSide.Right);using var capture = await peer.NextAsync();
        await Deliver(new { type = "edge", line = $"GB_EDGE {epoch} 1 1920 1080 0 500 0" });
        check(peer.Session.LatestEdge is not null,"DeX geometry received inside a live capture");
        await Deliver(new { type = "pong", ready = true, controlDisplay = 9, controlRevision = 1, desktopInput = true });
        check(changed == 0 && peer.Session.LatestEdge is not null,"stable display does not cancel ongoing movement");
        await Deliver(new { type = "pong", ready = true, controlDisplay = 2, controlRevision = 2, desktopInput = true });
        check(changed == 1 && peer.Session.LatestEdge is null && peer.Session.IsAlive,"switching equal-sized monitors invalidates old capture without dropping TLS");
        await Deliver(new { type = "edge", line = $"GB_EDGE {epoch} 2 1920 1080 0 500 0" });
        check(peer.Session.LatestEdge is null,"stale geometry cannot revive display capture after switch");
        epoch = peer.Session.BeginEdgeReturn(PhoneSide.Right);using var recapture = await peer.NextAsync();
        await Deliver(new { type = "edge", line = $"GB_EDGE {epoch} 1 1920 1080 0 500 0" });
        await Deliver(new { type = "pong", ready = true, controlDisplay = 2, controlRevision = 3, desktopInput = true });
        check(changed == 2 && peer.Session.LatestEdge is null,"resolution/rotation/density revision stops existing capture on the same display");
        await Deliver(new { type = "pong", ready = false, controlDisplay = -1, controlRevision = 4, desktopInput = true });
        check(!peer.Session.InputReady && peer.Session.PreferWifiInput && changed == 3,"missing explicitly selected monitor cannot route to phone Bluetooth");
        await Deliver(new { type = "pong", ready = true, controlDisplay = 0, controlRevision = 5, desktopInput = false });
        check(peer.Session.InputReady && !peer.Session.PreferWifiInput && changed == 4,"returning to phone restores original policy without disconnect");
    }
    private static async Task FileEdgeConfigurationAsync(Action<bool, string> check)
    {
        await using var peer = await Peer.CreateAsync(fileEdgeConfig: true);
        string folder = Path.Combine(Path.GetTempPath(), "GalaxyBridge-Edge-Config-" + Guid.NewGuid().ToString("N"));
        string batch = Guid.NewGuid().ToString("N"), transfer = Guid.NewGuid().ToString("N");
        peer.Session.ConfigureReceive(true, folder);
        async Task<bool> Request(object value)
        {
            await peer.Stream.WriteAsync(LanProtocol.Encode(value));
            using var reply = await peer.NextAsync();
            return reply.RootElement.GetProperty("type").GetString() == "uploadAck" && reply.RootElement.GetProperty("ok").GetBoolean();
        }
        string Id() => Guid.NewGuid().ToString("N");
        async Task Config(bool enabled)
        {
            peer.Session.ConfigureFileEdge(enabled);
            using var command = await peer.NextAsync();
            check(command.RootElement.GetProperty("type").GetString() == "fileEdgeConfig" &&
                command.RootElement.GetProperty("enabled").GetBoolean() == enabled, "edge checkbox sends negotiated configuration over real TLS");
        }
        try
        {
            check(!peer.Session.FileEdgeEnabled && !await Request(new { type = "dragPrepare", id = Id(), batch, count = 1 }) && peer.Session.IsAlive,
                "edge transfer is opt-in and disabled preparation does not disconnect");
            await Config(true);
            check(await Request(new { type = "dragPrepare", id = Id(), batch, count = 1 }), "checkbox enables preparation");
            check(await Request(new { type = "uploadBegin", id = Id(), dragBatch = batch, transfer, name = "partial.txt", size = 3 }), "enabled edge starts staged upload");
            await Config(false);
            check(!peer.Session.ReceiveProgress.State.Busy && peer.Session.IsAlive, "turning edge off cancels only staging and preserves TLS");
            check(!await Request(new { type = "uploadChunk", id = Id(), dragBatch = batch, transfer, seq = 0, data = "AQID" }), "disabled edge rejects later staged packets");
            check(await Request(new { type = "uploadBegin", id = Id(), transfer, name = "ordinary.txt", size = 0 }), "ordinary receiving works with edge off");
            await Config(false);
            check(peer.Session.ReceiveProgress.State.Busy, "edge off does not cancel an ordinary receive");
            check(await Request(new { type = "uploadEnd", id = Id(), transfer, size = 0, sha256 = Convert.ToHexString(SHA256.HashData([])).ToLowerInvariant() }), "ordinary receive completes with edge off");
            await Config(true);
            check(await Request(new { type = "dragPrepare", id = Id(), batch, count = 1 }), "new explicit drag allowed after re-enabling");
            check(await Request(new { type = "uploadBegin", id = Id(), dragBatch = batch, transfer, name = "ready.txt", size = 0 }) &&
                await Request(new { type = "uploadEnd", id = Id(), dragBatch = batch, transfer, size = 0, sha256 = Convert.ToHexString(SHA256.HashData([])).ToLowerInvariant() }) &&
                await Request(new { type = "dragReady", id = Id(), batch }), "batch prepared before checkbox disable");
            await Config(false);
            int epoch = peer.Session.BeginEdgeReturn(PhoneSide.Right);
            using var capture = await peer.NextAsync();
            await peer.Stream.WriteAsync(LanProtocol.Encode(new { type = "dragStart", batch, line = $"GB_EDGE {epoch} 1 1000 2000 0 1000 1" }));
            using var rejected = await peer.NextAsync();
            check(rejected.RootElement.GetProperty("type").GetString() == "dragResult" && !rejected.RootElement.GetProperty("ok").GetBoolean(), "queued or legacy drag cannot bypass disabled checkbox");
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
    private sealed class InlineSendProgress(Action<(long Sent, long Total)> report) : IProgress<(long Sent, long Total)>
    { public void Report((long Sent, long Total) value) => report(value); }
    private static async Task SendProgressAsync(Action<bool, string> check)
    {
        await using var peer = await Peer.CreateAsync();
        string folder = Path.Combine(Path.GetTempPath(), "GalaxyBridge-Widget-Send-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);string path = Path.Combine(folder, "на S25.txt");
        byte[] bytes = Enumerable.Range(0, LanProtocol.ChunkSize+3).Select(i => (byte)(i%251)).ToArray();
        await File.WriteAllBytesAsync(path, bytes);
        var reports = new System.Collections.Concurrent.ConcurrentQueue<(long Sent, long Total)>();
        async Task Ack(JsonElement command, string name = "") => await peer.Stream.WriteAsync(LanProtocol.Encode(new { type = "ack", id = command.GetProperty("id").GetString(), ok = true, name }));
        try
        {
            Task<string> sending = peer.Session.SendFileAsync(path, CancellationToken.None, new InlineSendProgress(reports.Enqueue));
            using var begin = await peer.NextAsync();
            check(begin.RootElement.GetProperty("type").GetString() == "fileBegin" && reports.IsEmpty, "widget reports no bytes before phone accepts file begin");
            await Ack(begin.RootElement);
            using MemoryStream received = new();
            for (int i=0;i<2;i++)
            {
                using var chunk = await peer.NextAsync();
                check(chunk.RootElement.GetProperty("type").GetString() == "fileChunk" && reports.Count == i+1, "unacknowledged chunk cannot advance widget progress");
                received.Write(Convert.FromBase64String(chunk.RootElement.GetProperty("data").GetString()!));
                await Ack(chunk.RootElement);
            }
            using var end = await peer.NextAsync();
            check(received.ToArray().SequenceEqual(bytes) && end.RootElement.GetProperty("sha256").GetString() == Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), "widget path sends actual bytes and checksum");
            check(!sending.IsCompleted, "all bytes acknowledged is not yet verified file completion");
            await Ack(end.RootElement, "на S25.txt");
            check(await sending == "на S25.txt" && reports.ToArray().SequenceEqual(new[] { (0L,(long)bytes.Length), ((long)LanProtocol.ChunkSize,(long)bytes.Length), ((long)bytes.Length,(long)bytes.Length) }), "widget progress uses acknowledged bytes and waits for final save ACK");
            reports.Clear();using CancellationTokenSource cancel = new();
            sending = peer.Session.SendFileAsync(path, cancel.Token, new InlineSendProgress(reports.Enqueue));
            using var cancelBegin = await peer.NextAsync();await Ack(cancelBegin.RootElement);
            using var outstanding = await peer.NextAsync();cancel.Cancel();
            using var abort = await peer.NextAsync();
            check(abort.RootElement.GetProperty("type").GetString() == "fileAbort" && reports.Count == 1, "widget Cancel aborts without reporting unacknowledged bytes");
            await Ack(abort.RootElement);
            bool cancelled = false;try { await sending; } catch (OperationCanceledException) { cancelled = true; }
            check(cancelled && peer.Session.IsAlive, "widget cancellation does not drop the Wi-Fi connection");
        }
        finally { Directory.Delete(folder, true); }
    }
    private static async Task InvalidDragRequestsAsync(Action<bool, string> check)
    {
        foreach (object command in new object[] {
            new { type = "dragPrepare", batch = Guid.NewGuid().ToString("N"), count = 1 },
            new { type = "dragCancel", id = (string?)null, batch = Guid.NewGuid().ToString("N") },
            new { type = "dragCancel", batch = "../outside" },
            new { type = "pong", ready = true, controlDisplay = -2, controlRevision = 0, desktopInput = true },
            new { type = "pong", ready = true, controlDisplay = 9, controlRevision = -1, desktopInput = true }
        })
        {
            await using var peer = await Peer.CreateAsync();
            await peer.Stream.WriteAsync(LanProtocol.Encode(command));
            await peer.Session.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            check(!peer.Session.IsAlive, "malformed drag requests or invalid control display metadata are rejected");
        }
    }
    private static async Task FileDragAsync(Action<bool, string> check)
    {
        Console.WriteLine("LAN files: checking staged drag delivery over actual TLS");
        await using var peer = await Peer.CreateAsync();
        string folder = Path.Combine(Path.GetTempPath(), "GalaxyBridge-Drag-Test-" + Guid.NewGuid().ToString("N"));
        peer.Session.ConfigureFileEdge(true);
        string batch = Guid.NewGuid().ToString("N"), transfer = Guid.NewGuid().ToString("N");
        async Task<bool> Request(object message)
        {
            await peer.Stream.WriteAsync(LanProtocol.Encode(message));
            using var reply = await peer.NextAsync();
            check(reply.RootElement.GetProperty("type").GetString() == "uploadAck", "drag request acknowledged over TLS");
            return reply.RootElement.GetProperty("ok").GetBoolean();
        }
        string Id() => Guid.NewGuid().ToString("N");
        string[] paths = [];
        try
        {
            check(!await Request(new { type = "dragPrepare", id = Id(), batch, count = 1 }), "disabled receiving rejects drag preparation");
            peer.Session.ConfigureReceive(true, folder);
            // Real legacy APK cancellation: no id was sent on physical release.
            // It must not close TLS, and a stale cancellation cannot delete new data.
            check(await Request(new { type = "dragPrepare", id = Id(), batch, count = 1 }), "cancel regression prepares partial batch");
            check(await Request(new { type = "uploadBegin", id = Id(), dragBatch = batch, transfer, name = "partial.txt", size = 3 }), "cancel regression starts real staged receive");
            await peer.Stream.WriteAsync(LanProtocol.Encode(new { type = "dragCancel", batch = Id() }));
            check(await Request(new { type = "uploadChunk", id = Id(), dragBatch = batch, transfer, seq = 0, data = "AQ==" }) && peer.Session.IsAlive,
                "legacy stale cancel preserves staged data and TLS");
            await peer.Stream.WriteAsync(LanProtocol.Encode(new { type = "dragCancel", batch }));
            check(await Request(new { type = "dragPrepare", id = Id(), batch, count = 1 }) && peer.Session.IsAlive,
                "legacy active cancel removes partial receive without disconnect; next drag can start");
            check(!peer.Session.ReceiveProgress.State.Busy && !File.Exists(Path.Combine(folder, "partial.txt")), "cancel publishes no partial file");
            string cancelId = Id();
            await peer.Stream.WriteAsync(LanProtocol.Encode(new { type = "dragCancel", id = cancelId, batch }));
            using (var cancelReply = await peer.NextAsync())
                check(cancelReply.RootElement.GetProperty("id").GetString() == cancelId && cancelReply.RootElement.GetProperty("ok").GetBoolean() && peer.Session.IsAlive,
                    "new Android correlated cancellation receives matching ACK without disconnect");
            check(await Request(new { type = "dragPrepare", id = Id(), batch, count = 1 }), "enabled receiver prepares drag batch");
            check(!await Request(new { type = "dragReady", id = Id(), batch }), "partial batch rejected without disconnect");
            check(await Request(new { type = "uploadBegin", id = Id(), dragBatch = batch, transfer, name = "тест.txt", size = 3 }), "tagged file begin accepted");
            var stagedProgress = peer.Session.ReceiveProgress;
            check(stagedProgress.Staging && stagedProgress.State.Busy && LanProtocol.Identifier(stagedProgress.State.OperationId ?? ""), "staging exposes a host-owned operation identity for native progress");
            peer.Session.CancelReceive(stagedProgress.State.OperationId!, false);
            peer.Session.CancelReceive(Id(), true);
            check(peer.Session.ReceiveProgress.State.Busy, "wrong channel or stale operation cannot cancel staging");
            check(await Request(new { type = "uploadChunk", id = Id(), dragBatch = batch, transfer, seq = 0, data = "AQID" }), "tagged chunk accepted");
            check(await Request(new { type = "uploadEnd", id = Id(), dragBatch = batch, transfer, size = 3,
                sha256 = Convert.ToHexString(SHA256.HashData(new byte[] { 1, 2, 3 })).ToLowerInvariant() }), "tagged checksum verified");
            check(await Request(new { type = "dragReady", id = Id(), batch }), "completed drag ready");
            check(!Directory.Exists(folder), "preparing a drag does not copy into configured receive folder");
            // No capture: a paired peer still cannot start a desktop drag.
            await peer.Stream.WriteAsync(LanProtocol.Encode(new { type = "dragStart", batch, line = "GB_EDGE 1 1 1000 2000 0 1000 1" }));
            using (var rejected = await peer.NextAsync()) check(rejected.RootElement.GetProperty("type").GetString() == "dragResult" &&
                !rejected.RootElement.GetProperty("ok").GetBoolean(), "desktop drag requires active phone capture");
            int epoch = peer.Session.BeginEdgeReturn(PhoneSide.Right);
            using var capture = await peer.NextAsync();
            TaskCompletionSource<PhoneEdgeSample> handed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            peer.Session.FileDragRequested += (id, sample) => { if (id == batch) handed.TrySetResult(sample); };
            await peer.Stream.WriteAsync(LanProtocol.Encode(new { type = "dragStart", batch, line = $"GB_EDGE {epoch} 1 1000 2000 0 1000 1" }));
            var sample = await handed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            check(sample.CaptureId == epoch && sample.Buttons == 1, "capture-bound drag event delivered without blocking TLS reader");
            TaskCompletionSource<PhoneEdgeSample> insetHanded = new(TaskCreationOptions.RunContinuationsAsynchronously);
            peer.Session.FileDragRequested += (id, s) => { if (id == batch && s.Zone is not null) insetHanded.TrySetResult(s); };
            await peer.Stream.WriteAsync(LanProtocol.Encode(new { type = "dragStart", batch, line = $"GB_EDGE2 {epoch} 1 1000 2000 18 1000 1 18 24 1 1930" }));
            var inset = await insetHanded.Task.WaitAsync(TimeSpan.FromSeconds(5));
            check(inset.Zone == new DesktopBounds(18, 24, 1, 1930) && inset.CaptureId == epoch, "inset file edge survives real TLS dispatch");
            check(FileDragHandoff.CanReturn(inset, epoch, PhoneSide.Right, 1, false, -1, 0, Environment.TickCount64), "TLS inset edge passes physical handoff policy");
            paths = peer.Session.TakeDragFiles(batch);
            check(paths.Length == 1 && File.ReadAllBytes(paths[0]).SequenceEqual(new byte[] { 1, 2, 3 }), "staged drag has verified actual file bytes");
            await peer.Stream.WriteAsync(LanProtocol.Encode(new { type = "dragStart", batch, line = $"GB_EDGE {epoch} 1 1000 2000 0 1000 1" }));
            using (var replay = await peer.NextAsync()) check(!replay.RootElement.GetProperty("ok").GetBoolean(), "same batch cannot start another drag");
            // Ordinary, untagged uploads remain compatible with earlier APKs.
            check(await Request(new { type = "uploadBegin", id = Id(), transfer, name = "normal.txt", size = 0 }), "legacy upload begin still accepted");
            var normalProgress = peer.Session.ReceiveProgress;
            check(!normalProgress.Staging && normalProgress.State.OperationId != stagedProgress.State.OperationId, "ordinary upload using same wire transfer gets a separate progress operation");
            peer.Session.CancelReceive(stagedProgress.State.OperationId!, false);
            check(peer.Session.ReceiveProgress.State.Busy, "old native dialog cannot cancel the next upload");
            check(await Request(new { type = "uploadEnd", id = Id(), transfer, size = 0,
                sha256 = Convert.ToHexString(SHA256.HashData(Array.Empty<byte>())).ToLowerInvariant() }), "legacy upload end still accepted");
            check(File.Exists(Path.Combine(folder, "normal.txt")), "legacy upload still uses configured folder");
            check(peer.Session.ReceiveProgress.State is { Busy: false, SavedPath: not null } completed &&
                completed.OperationId == normalProgress.State.OperationId, "verified publish retains operation identity for native completion");
            check(await Request(new { type = "uploadBegin", id = Id(), transfer, name = "cancel.txt", size = 3 }), "ordinary cancellable receive started");
            string cancelledOperation = peer.Session.ReceiveProgress.State.OperationId!;
            peer.Session.CancelReceive(cancelledOperation, false);
            check(peer.Session.ReceiveProgress.State is { Busy: false, SavedPath: null } stopped && stopped.OperationId == cancelledOperation,
                "native cancellation stops exactly the active normal receive");
            check(!await Request(new { type = "uploadChunk", id = Id(), transfer, seq = 0, data = "AQID" }) &&
                !File.Exists(Path.Combine(folder, "cancel.txt")), "cancelled receive rejects subsequent chunks and publishes nothing");
            check(await Request(new { type = "dragPrepare", id = Id(), batch, count = 1 }) &&
                await Request(new { type = "uploadBegin", id = Id(), dragBatch = batch, transfer, name = "cancel-drag.txt", size = 3 }), "cancellable staging started");
            peer.Session.CancelReceive(peer.Session.ReceiveProgress.State.OperationId!, true);
            check(!peer.Session.ReceiveProgress.State.Busy && !await Request(new { type = "dragReady", id = Id(), batch }), "native cancellation discards staging and prevents delayed desktop handoff");
            peer.Session.Close(); await peer.Session.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            check(paths.All(File.Exists), "TLS close does not remove files handed to Explorer");
        }
        finally
        {
            if (paths.Length > 0) LanDragStaging.DeleteOwnedFolder(Path.GetDirectoryName(paths[0])!);
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }
    private static async Task LegacyMouseAsync(Action<bool, string> check)
    {
        await using (var legacy = await Peer.CreateAsync())
        {
            legacy.Session.Mouse(0, 0, 0, 1);
            using var wheel = await legacy.NextAsync();
            check(wheel.RootElement.GetProperty("wheel").GetInt32() == 1 && !wheel.RootElement.TryGetProperty("zoom", out _), "old APK retains ordinary wheel scrolling");
            legacy.Session.Keyboard(new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 });
            legacy.Session.Mouse(0, 0, 0, 1);
            using var unsupportedZoom = await legacy.NextAsync();
            check(unsupportedZoom.RootElement.GetProperty("wheel").GetInt32() == 0, "unsupported zoom does not accidentally scroll an old APK");
        }
        await using var peer = await Peer.CreateAsync(wheelZoom: true);
        int epoch = peer.Session.BeginEdgeReturn(PhoneSide.Right);
        using var start = await peer.NextAsync();
        bool unchanged = true;
        for (int i = 0; i < 64; i++)
        {
            int dx = i % 2 == 0 ? i + 1 : -i - 1, dy = 17 - i;
            peer.Session.Mouse(0, dx, dy);
            using var packet = await peer.NextAsync();
            JsonElement root = packet.RootElement;
            unchanged &= root.EnumerateObject().Select(p => p.Name).Order().SequenceEqual(new[] { "buttons", "dx", "dy", "type", "wheel" }) &&
                root.GetProperty("type").GetString() == "mouse" && root.GetProperty("buttons").GetInt32() == 0 &&
                root.GetProperty("dx").GetInt32() == dx && root.GetProperty("dy").GetInt32() == dy && root.GetProperty("wheel").GetInt32() == 0;
        }
        check(unchanged, "ordinary movement over actual TLS uses precisely the five-field 0.7.8 format and retains every delta");
        async Task Wheel(byte buttons = 0, int steps = 1, bool zoom = false)
        {
            peer.Session.Mouse(buttons, 0, 0, steps);
            using var packet = await peer.NextAsync();
            JsonElement root = packet.RootElement;
            bool isZoom = root.TryGetProperty("zoom", out var value) && value.GetBoolean();
            check(root.GetProperty("wheel").GetInt32() == steps && root.GetProperty("buttons").GetInt32() == buttons && isZoom == zoom &&
                !root.TryGetProperty("seq", out _) && root.EnumerateObject().Count() == (zoom ? 6 : 5),
                "only a zoom wheel event adds the optional zoom field");
        }
        await Wheel();
        foreach (byte ctrl in new byte[] { 1, 16 })
        {
            peer.Session.Keyboard(new byte[] { ctrl, 0, 0, 0, 0, 0, 0, 0 });
            await Wheel(steps: -1, zoom: true);
        }
        peer.Session.Keyboard(new byte[8]); await Wheel();
        peer.Session.WheelZoomEnabled = true; await Wheel(zoom: true); await Wheel(buttons: 1);
        async Task Deliver(object message)
        {
            await peer.Stream.WriteAsync(LanProtocol.Encode(message));
            using var barrier = await peer.RequestAsync(Guid.NewGuid().ToString("N"), "barrier");
        }
        object Edge(int capture, long sequence) => new { type = "edge", line = $"GB_EDGE {capture} {sequence} 1080 2400 0 1200 0" };
        await Deliver(Edge(epoch, 3));
        check(peer.Session.LatestEdge?.Sequence == 3, "0.7.8 phone edge feedback is accepted without input acknowledgement metadata");
        await Deliver(Edge(epoch, 2)); await Deliver(Edge(epoch + 1, 100));
        check(peer.Session.LatestEdge?.Sequence == 3, "older feedback and another capture cannot replace current feedback");
        peer.Session.EndEdgeReturn(); using var stop = await peer.NextAsync();
        await Deliver(Edge(epoch, 101));
        check(peer.Session.LatestEdge is null, "feedback after returning to PC cannot revive a stopped capture");
        int next = peer.Session.BeginEdgeReturn(PhoneSide.Right); using var restart = await peer.NextAsync();
        await Deliver(Edge(epoch, 200));
        check(peer.Session.LatestEdge is null, "old capture feedback cannot return a newly entered cursor");
        await Deliver(Edge(next, 1));
        check(peer.Session.LatestEdge?.CaptureId == next, "new capture accepts its own 0.7.8 feedback");
        peer.Session.ReleaseInputs(); using var release = await peer.NextAsync();
        check(peer.Session.LatestEdge is null, "release clears touch feedback");
    }
    private static async Task NativeEdgeAsync(Action<bool, string> check)
    {
        await using (var old = await Peer.CreateAsync())
        {
            check(old.Session.BeginNativeEdgeReturn(PhoneSide.Right) == 0, "old APK never receives unsupported native mouse commands");
            old.Session.Paste("compatibility marker");
            using var marker = await old.NextAsync();
            check(marker.RootElement.GetProperty("type").GetString() == "text", "native edge capability preserves the old LAN protocol");
        }
        await using var peer = await Peer.CreateAsync(nativeEdge: true);
        int epoch = peer.Session.BeginNativeEdgeReturn(PhoneSide.Right);
        using (var start = await peer.NextAsync())
            check(start.RootElement.GetProperty("type").GetString() == "nativeCapture" &&
                start.RootElement.GetProperty("epoch").GetInt32() == epoch && start.RootElement.GetProperty("side").GetInt32() == 0,
                "Bluetooth mouse capture arms the phone edge independently of touch accessibility");
        async Task Deliver(object message)
        {
            await peer.Stream.WriteAsync(LanProtocol.Encode(message));
            // An acknowledged request is a barrier in the same ordered TLS stream.
            using var barrier = await peer.RequestAsync(Guid.NewGuid().ToString("N"), "barrier");
        }
        await Deliver(new { type = "nativeEdge", line = $"GB_EDGE2 {epoch} 1 1080 2400 20.5 1200 0 20 80 1 2220" });
        check(peer.Session.LatestNativeEdge is null, "hover cannot return the cursor before the phone confirms its overlay");
        await Deliver(new { type = "nativeCaptureAck", epoch, ready = true });
        check(peer.Session.LatestNativeEdge?.Sequence == 1, "real hover coordinates become available after the overlay acknowledgement");
        await Deliver(new { type = "nativeEdge", line = $"GB_EDGE2 {epoch} 3 1080 2400 20.5 1200 16 20 80 1 2220" });
        check(peer.Session.LatestNativeEdge?.Buttons == 16, "fifth system mouse button is preserved in edge feedback");
        check(!EdgeReturnPolicy.CanReturn(peer.Session.LatestNativeEdge!, epoch, PhoneSide.Right, -1, 0, 0, false,
            Environment.TickCount64 - 1000, Environment.TickCount64, Environment.TickCount64), "held fifth button prevents edge return");
        await Deliver(new { type = "nativeEdge", line = $"GB_EDGE2 {epoch} 2 1080 2400 20.5 1200 0 20 80 1 2220" });
        await Deliver(new { type = "nativeEdge", line = $"GB_EDGE2 {epoch + 1} 100 1080 2400 20.5 1200 0 20 80 1 2220" });
        await Deliver(new { type = "nativeEdge", line = $"GB_EDGE2 {epoch} 4 1080 2400 20.5 1200 0 20 80 1080 2220" });
        check(peer.Session.LatestNativeEdge?.Sequence == 3, "out-of-order, different capture and invalid geometry do not replace hover feedback");
        await Deliver(new { type = "nativeEdgeLeave", epoch, sequence = 5 });
        check(peer.Session.LatestNativeEdge is null, "leaving the edge immediately invalidates its hover position");
        await Deliver(new { type = "nativeEdge", line = $"GB_EDGE2 {epoch} 4 1080 2400 20.5 1200 0 20 80 1 2220" });
        check(peer.Session.LatestNativeEdge is null, "old hover cannot reappear after a newer edge exit");
        await Deliver(new { type = "nativeEdge", line = $"GB_EDGE2 {epoch} 6 1080 2400 20.5 1200 0 20 80 1 2220" });
        check(peer.Session.LatestNativeEdge?.Sequence == 6, "a new real hover can rearm edge return");
        peer.Session.EndNativeEdgeReturn(epoch);
        using (var stop = await peer.NextAsync())
            check(stop.RootElement.GetProperty("type").GetString() == "nativeCapture" && !stop.RootElement.GetProperty("active").GetBoolean(), "return removes the phone mouse edge");
        await Deliver(new { type = "nativeCaptureAck", epoch, ready = true });
        await Deliver(new { type = "nativeEdge", line = $"GB_EDGE2 {epoch} 10 1080 2400 20.5 1200 0 20 80 1 2220" });
        check(peer.Session.LatestNativeEdge is null, "late phone feedback cannot revive an ended capture");
        int next = peer.Session.BeginNativeEdgeReturn(PhoneSide.Bottom);
        using var restart = await peer.NextAsync();
        peer.Session.EndNativeEdgeReturn(epoch);
        await Deliver(new { type = "nativeCaptureAck", epoch = next, ready = true });
        await Deliver(new { type = "nativeEdge", line = $"GB_EDGE2 {next} 1 1080 2400 500 80.5 0 20 80 1030 1" });
        check(peer.Session.LatestNativeEdge?.CaptureId == next, "late stop cannot end the next native capture");
        await Deliver(new { type = "nativeCaptureAck", epoch = next, ready = false, reason = "permission" });
        check(peer.Session.LatestNativeEdge is null && peer.Session.IsAlive, "revoked overlay permission disables return without disconnecting Bluetooth or LAN");
    }
    private static async Task AutomaticAsync(Action<bool, string> check)
    {
        await using var peer = await Peer.CreateAsync(true);
        uint sequence = 10; int writes = 0; string? copied = null;
        peer.Session.ClipboardSequenceReader = () => sequence;
        peer.Session.AutomaticClipboardWriter = (text, expected, ct) =>
        {
            if (ct.IsCancellationRequested || expected != sequence) return Task.FromResult(false);
            copied = text; writes++; return Task.FromResult(true);
        };
        peer.Session.ConfigureClipboard(true);
        using (var config = await peer.NextAsync())
            check(config.RootElement.GetProperty("type").GetString() == "clipboardConfig" && config.RootElement.GetProperty("enabled").GetBoolean(), "new peer negotiates clipboard enablement");
        check(peer.Session.PushClipboard("С ПК 🌍"), "PC clipboard is queued for new peer");
        string setId;
        using (var set = await peer.NextAsync())
        {
            check(set.RootElement.GetProperty("type").GetString() == "clipboardSet" && set.RootElement.GetProperty("text").GetString() == "С ПК 🌍", "PC copy updates system clipboard, without a paste command");
            setId = set.RootElement.GetProperty("id").GetString()!;
        }
        await peer.Stream.WriteAsync(LanProtocol.Encode(new { type = "clipboardSetAck", id = setId, ok = true }));
        string request = await PullAsync(peer);
        using (var ack = await AutoReplyAsync(peer, request, "С телефона 🌍"))
            check(ack.RootElement.GetProperty("ok").GetBoolean() && writes == 1 && copied == "С телефона 🌍", "phone clipboard on return is written and acknowledged");
        using (var ack = await AutoReplyAsync(peer, request, "duplicate response"))
            check(!ack.RootElement.GetProperty("ok").GetBoolean() && writes == 1, "duplicate automatic response cannot overwrite clipboard");
        request = await PullAsync(peer); sequence++;
        using (var ack = await AutoReplyAsync(peer, request, "stale phone text"))
            check(!ack.RootElement.GetProperty("ok").GetBoolean() && writes == 1, "PC copy made during pull wins even before clipboard notification is processed");
        request = await PullAsync(peer); peer.Session.CancelClipboardPull();
        using (var ack = await AutoReplyAsync(peer, request, "cancelled response"))
            check(!ack.RootElement.GetProperty("ok").GetBoolean() && writes == 1, "cancelled pull cannot write");
        request = await PullAsync(peer); peer.Session.BeginEdgeReturn(PhoneSide.Right);
        using (var capture = await peer.NextAsync()) check(capture.RootElement.GetProperty("active").GetBoolean(), "control re-entry is ordered after pull");
        using (var ack = await AutoReplyAsync(peer, request, "late response after re-entry"))
            check(!ack.RootElement.GetProperty("ok").GetBoolean() && writes == 1, "returning to phone cancels outstanding pull");
        request = await PullAsync(peer); peer.Session.ConfigureClipboard(false);
        using (var config = await peer.NextAsync()) check(!config.RootElement.GetProperty("enabled").GetBoolean(), "disabling is sent to the phone");
        using (var ack = await AutoReplyAsync(peer, request, "disabled response"))
            check(!ack.RootElement.GetProperty("ok").GetBoolean() && writes == 1 && !peer.Session.PushClipboard("disabled"), "disabled shared clipboard refuses both directions");
        peer.Session.ConfigureClipboard(true); using (var config = await peer.NextAsync()) { }
        request = await PullAsync(peer);
        await peer.Stream.WriteAsync(LanProtocol.Encode(new { type = "clipboardResult", request, status = "permission" }));
        // A subsequent packet serves as a barrier for the result in the same TLS stream.
        using (var ack = await AutoReplyAsync(peer, request, "after empty result"))
            check(!ack.RootElement.GetProperty("ok").GetBoolean() && writes == 1, "permission/empty results consume only the current request");
        request = await PullAsync(peer);
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        peer.Session.AutomaticClipboardWriter = async (text, expected, ct) =>
        { started.SetResult(); await Task.Delay(10000, ct); writes++; return true; };
        string pendingId = Guid.NewGuid().ToString("N");
        await peer.Stream.WriteAsync(LanProtocol.Encode(new { type = "clipboard", id = pendingId, request, text = "delayed UI" }));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5)); peer.Session.CancelClipboardPull();
        using (var ack = await peer.NextAsync())
            check(!ack.RootElement.GetProperty("ok").GetBoolean() && writes == 1, "clipboard cancellation reaches a queued UI writer");
        peer.Session.ClipboardReadAllowed = () => false;
        peer.Session.BeginEdgeReturn(PhoneSide.Right);peer.Session.EndEdgeReturn();peer.Session.ReleaseInputs();
        using (var capture = await peer.NextAsync()) { }
        using (var capture = await peer.NextAsync()) { }
        using (var release = await peer.NextAsync())
            check(release.RootElement.GetProperty("type").GetString() == "release", "lock/close stops capture without opening a clipboard window");
        foreach (string invalid in new[] { "", "bad\0text", new string('Я', 8001), new string('\u0001', 16000) })
        {
            bool rejected = false;
            try { peer.Session.PushClipboard(invalid); } catch (Exception e) when (e is InvalidDataException or ArgumentException) { rejected = true; }
            check(rejected && peer.Session.IsAlive, "invalid PC clipboard is rejected before queuing, without disconnect");
        }
        peer.Session.ClipboardReadAllowed = () => true;
        peer.Session.AutomaticClipboardWriter = (text, expected, ct) =>
        {
            if (ct.IsCancellationRequested || expected != sequence) return Task.FromResult(false);
            copied = text; writes++; return Task.FromResult(true);
        };
        peer.Session.BeginNativeControl();peer.Session.EndNativeControl();
        using (var nativePull = await peer.NextAsync())
        {
            check(nativePull.RootElement.GetProperty("type").GetString() == "clipboardGet", "native HID return reads clipboard without enabling the touch cursor");
            using var ack = await AutoReplyAsync(peer, nativePull.RootElement.GetProperty("request").GetString()!, "Native HID clipboard");
            check(ack.RootElement.GetProperty("ok").GetBoolean() && writes == 2, "Bluetooth HID retains automatic LAN clipboard delivery");
        }
        long oldBluetoothCapture = peer.Session.BeginNativeControl();
        peer.Session.BeginEdgeReturn(PhoneSide.Right);
        peer.Session.EndNativeControl(oldBluetoothCapture); // Late completion of BLE release after LAN starts.
        peer.Session.EndEdgeReturn();
        using (var entered = await peer.NextAsync())
            check(entered.RootElement.GetProperty("type").GetString() == "capture" && entered.RootElement.GetProperty("active").GetBoolean(), "touch entry survives the previous Bluetooth clipboard epoch");
        using (var returned = await peer.NextAsync())
            check(returned.RootElement.GetProperty("type").GetString() == "capture" && !returned.RootElement.GetProperty("active").GetBoolean(), "late Bluetooth release does not read clipboard during touch control");
        using (var freshPull = await peer.NextAsync())
            check(freshPull.RootElement.GetProperty("type").GetString() == "clipboardGet", "new touch return still requests its own clipboard after stale Bluetooth return");
        await using var legacy = await Peer.CreateAsync();legacy.Session.ConfigureClipboard(true);
        check(!legacy.Session.AutomaticClipboard && !legacy.Session.PushClipboard("legacy"), "no automatic command is sent to an old peer");
    }
    private static async Task<string> PullAsync(Peer peer)
    {
        peer.Session.BeginEdgeReturn(PhoneSide.Right);peer.Session.EndEdgeReturn();
        using var capture = await peer.NextAsync();using var returned = await peer.NextAsync();using var pull = await peer.NextAsync();
        if (pull.RootElement.GetProperty("type").GetString() != "clipboardGet") throw new Exception("Missing clipboard pull");
        return pull.RootElement.GetProperty("request").GetString()!;
    }
    private static async Task<JsonDocument> AutoReplyAsync(Peer peer, string request, string text)
    {
        await peer.Stream.WriteAsync(LanProtocol.Encode(new { type = "clipboard", id = Guid.NewGuid().ToString("N"), request, text }));
        return await peer.NextAsync();
    }
    // Task.WhenAll alone does not stop the client if the server fails before
    // sending its handshake. Abort both transports and cancel the sibling.
    private static async Task AuthenticateTogetherAsync(Func<CancellationToken, Task> server,
        Func<CancellationToken, Task> client, CancellationToken timeout, Action abort)
    {
        using var failed = CancellationTokenSource.CreateLinkedTokenSource(timeout);
        async Task Authenticate(Func<CancellationToken, Task> action)
        {
            try { await action(failed.Token).WaitAsync(failed.Token); }
            catch { failed.Cancel(); abort(); throw; }
        }
        await Task.WhenAll(Authenticate(server), Authenticate(client));
    }
    private static async Task HandshakeFailureTestsAsync(Action<bool, string> check)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        TaskCompletionSource clientStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        bool aborted = false, rejected = false;
        try
        {
            await AuthenticateTogetherAsync(async ct =>
            {
                await clientStarted.Task.WaitAsync(ct);
                throw new IOException("Injected TLS server credential failure");
            }, ct => { clientStarted.SetResult(); return Task.Delay(Timeout.Infinite, ct); }, deadline.Token, () => aborted = true);
        }
        catch (IOException) { rejected = true; }
        check(rejected && aborted && !deadline.IsCancellationRequested, "TLS server failure aborts the waiting client before the deadline");
        using var stalledDeadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        TaskCompletionSource stalled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        aborted = false; bool expired = false;
        try { await AuthenticateTogetherAsync(_ => stalled.Task, _ => stalled.Task, stalledDeadline.Token, () => aborted = true); }
        catch (OperationCanceledException) { expired = true; }
        check(expired && aborted, "TLS deadline aborts even a handshake that ignores cancellation");
    }
    private sealed class Peer : IAsyncDisposable
    {
        private readonly TcpClient client;
        private readonly X509Certificate2 certificate;
        private readonly SslStream server;
        public SslStream Stream { get; }
        public LanSession Session { get; }
        private Peer(TcpClient client, SslStream stream, LanSession session, X509Certificate2 certificate, SslStream server)
        { this.client = client; Stream = stream; Session = session; this.certificate = certificate; this.server = server; }
        public static async Task<Peer> CreateAsync(bool automatic = false, bool nativeEdge = false, bool wheelZoom = false, bool fileEdgeConfig = false)
        {
            using RSA rsa = RSA.Create(2048);
            CertificateRequest request = new("CN=Galaxy Bridge clipboard test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
            using X509Certificate2 generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
            // Match production's user key container. Schannel cannot use an
            // ephemeral private key returned directly by CreateSelfSigned.
            byte[] pfx = generated.Export(X509ContentType.Pfx);
            X509Certificate2 certificate;
            try { certificate = new(pfx, (string?)null, X509KeyStorageFlags.UserKeySet); }
            finally { CryptographicOperations.ZeroMemory(pfx); }
            string pin = certificate.GetCertHashString(HashAlgorithmName.SHA256);
            using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(10));
            using TcpListener listener = new(IPAddress.Loopback, 0);
            TcpClient client = new();
            TcpClient? accepted = null; SslStream? server = null, stream = null;
            string stage = "loopback connection";
            try
            {
                listener.Start(); await client.ConnectAsync((IPEndPoint)listener.LocalEndpoint, deadline.Token);
                accepted = await listener.AcceptTcpClientAsync(deadline.Token); listener.Stop();
                server = new(accepted.GetStream(), false);
                stream = new(client.GetStream(), false, (_, cert, _, _) => cert?.GetCertHashString(HashAlgorithmName.SHA256) == pin);
                stage = "TLS authentication";
                await AuthenticateTogetherAsync(
                    ct => server.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = certificate, EnabledSslProtocols = SslProtocols.Tls12 }, ct),
                    ct => stream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = "localhost", EnabledSslProtocols = SslProtocols.Tls12 }, ct),
                    deadline.Token, () => { accepted.Dispose(); client.Dispose(); });
                LanSession session = new(accepted, server, "clipboard test", automatic, nativeEdge, wheelZoom, fileEdgeConfig); session.Start();
                return new(client, stream, session, certificate, server);
            }
            catch (Exception ex)
            {
                server?.Dispose(); stream?.Dispose(); accepted?.Dispose(); client.Dispose(); certificate.Dispose();
                throw new InvalidOperationException($"LAN clipboard test setup failed during {stage} (10 second deadline).", ex);
            }
        }
        public async Task<JsonDocument> RequestAsync(string id, string text)
        {
            using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(8));
            await Stream.WriteAsync(LanProtocol.Encode(new { type = "clipboard", id, text }), deadline.Token);
            return await NextAsync();
        }
        public async Task<JsonDocument> NextAsync()
        {
            using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(8));
            while (true)
            {
                JsonDocument reply = await LanProtocol.ReadAsync(Stream, deadline.Token);
                if (reply.RootElement.GetProperty("type").GetString() != "ping") return reply;
                reply.Dispose(); await Stream.WriteAsync(LanProtocol.Encode(new { type = "pong", ready = false }), deadline.Token);
            }
        }
        public async ValueTask DisposeAsync()
        {
            try { Session.Close(); await Session.Completion.WaitAsync(TimeSpan.FromSeconds(5)); }
            finally { server.Dispose(); Stream.Dispose(); client.Dispose(); certificate.Dispose(); }
        }
    }
}
