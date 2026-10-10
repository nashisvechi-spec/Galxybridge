using GalaxyBridge.Core;

static class NativeMouseTests
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        // Parse HID report sizes independently, as a host would, rather than
        // comparing the descriptor to a copy of its bytes.
        Dictionary<(int Id, bool Output), int> Sizes(byte[] descriptor)
        {
            Dictionary<(int, bool), int> bits = [];int size = 0, count = 0, id = 0;
            for (int i = 0; i < descriptor.Length;)
            {
                byte prefix = descriptor[i++];int n = prefix & 3;n = n == 3 ? 4 : n;
                int value = 0;for (int b = 0; b < n; b++) value |= descriptor[i++] << (8 * b);
                int type = (prefix >> 2) & 3, tag = prefix >> 4;
                if (type == 1) { if (tag == 7) size = value;else if (tag == 8) id = value;else if (tag == 9) count = value; }
                if (type == 0 && tag is 8 or 9) { var key = (id, tag == 9);bits[key] = bits.GetValueOrDefault(key) + size * count; }
            }
            return bits;
        }
        check(Sizes(Hid.MouseDescriptor)[(0, false)] == 40, "native mouse descriptor declares exactly five report bytes");
        var composite = Sizes(Hid.BluetoothReportMap);
        check(composite[(1, false)] == 64 && composite[(2, false)] == 40 && composite[(1, true)] == 8,
            "Bluetooth composite map has separate keyboard/mouse IDs and LED output");
        var mouse = new MouseInputState();
        foreach (var (down, up, data, mask) in new[] { (0x201, 0x202, 0U, 1), (0x204, 0x205, 0U, 2), (0x207, 0x208, 0U, 4), (0x20B, 0x20C, 1U << 16, 8), (0x20B, 0x20C, 2U << 16, 16) })
        {
            check(mouse.Update(down, data, out var pressed) && pressed.Buttons == mask, "physical button reaches HID on down " + mask);
            check(mouse.Update(up, data, out var released) && released.Buttons == 0, "physical button reaches HID on up " + mask);
        }
        mouse.Update(0x201, 0, out _);mouse.Update(0x20B, 2U << 16, out _);
        check(mouse.Buttons == 17, "simultaneous primary/forward buttons stay held");
        uint Wheel(short value) => (uint)(ushort)value << 16;
        check(!mouse.Update(0x20A, Wheel(60), out _), "partial vertical wheel delta is retained");
        check(!mouse.Update(0x20E, Wheel(-60), out _), "horizontal wheel has an independent remainder");
        check(mouse.Update(0x20A, Wheel(60), out var v) && v.Wheel == 1 && v.HorizontalWheel == 0 && v.Buttons == 17, "vertical scroll preserves held buttons");
        check(mouse.Update(0x20E, Wheel(-60), out var h) && h.HorizontalWheel == -1 && h.Wheel == 0, "horizontal scroll keeps direction without vertical leakage");
        mouse.Update(0x20A, Wheel(60), out _);mouse.Update(0x20E, Wheel(60), out _);mouse.Clear();
        check(mouse.Buttons == 0 && !mouse.Update(0x20A, Wheel(60), out _) && !mouse.Update(0x20E, Wheel(60), out _), "switching capture clears buttons and both wheel remainders");
        check(!mouse.Update(0x20B, 3U << 16, out _) && mouse.Buttons == 0, "invalid extra button cannot stick a bit");
        check(!mouse.Update(0x200, 0, out _), "hover does not synthesize any button press");

        List<(byte Report, byte[] Data)> ordered = [];
        await using (var sequence = new HidReportPump((_, report, data, _) => { ordered.Add((report, data));return Task.CompletedTask; }))
        {
            sequence.Begin("S25");
            byte[] shift = [2, 0, 0, 0, 0, 0, 0, 0];sequence.Post(1, shift);shift[0] = 0;
            sequence.Post(2, Hid.MouseReports(1, 20, 0).Single());
            sequence.Post(1, new byte[8]);sequence.Post(2, Hid.MouseReports(0, 0, 0).Single());
            await sequence.FlushAsync().WaitAsync(TimeSpan.FromSeconds(5));
            check(ordered.Select(e => e.Report).SequenceEqual(new byte[] { 1, 2, 1, 2 }), "modifier and mouse events share one ordered HID stream");
            check(ordered[0].Data[0] == 2 && ordered[1].Data[0] == 1 && ordered[2].Data[0] == 0 && ordered[3].Data[0] == 0,
                "Shift+drag remains held until real releases; queued reports cannot be changed by their caller");
            sequence.Stop();
        }

        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously), unblock = new(TaskCreationOptions.RunContinuationsAsynchronously);
        List<(string Host, byte Report, byte[] Data)> sent = [];
        await using (var pump = new HidReportPump(async (host, report, payload, ct) =>
        {
            if (payload[0] == 1 && report == 2) { entered.TrySetResult();await unblock.Task.WaitAsync(ct); }
            sent.Add((host, report, payload));
        }))
        {
            pump.Begin("S25");pump.Post(2, Hid.MouseReports(1, 0, 0).Single());
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            pump.Post(2, Hid.MouseReports(1, 50, 40).Single());pump.Post(1, [2, 0, 4, 0, 0, 0, 0, 0]);
            pump.Stop();pump.Begin("other phone");pump.Post(2, Hid.MouseReports(2, 0, 0).Single());pump.Stop();
            unblock.SetResult();await pump.FlushAsync().WaitAsync(TimeSpan.FromSeconds(5));
            check(sent.Count == 5 && sent[0].Host == "S25", "already in-flight down is followed only by releases after return");
            check(sent.Skip(1).All(e => e.Data.All(b => b == 0)), "queued drag/text cannot play after capture stops");
            check(sent[1].Host == "S25" && sent[2].Host == "S25" && sent[3].Host == "other phone" && sent[4].Host == "other phone", "old target releases precede new target releases without broadcasting");
            check(!pump.Post(2, Hid.MouseReports(1, 0, 0).Single()), "input cannot be sent without an explicit capture lease");
        }
        int attempts = 0, faults = 0;
        await using (var failed = new HidReportPump((_, _, _, _) => { attempts++;return attempts == 1 ? Task.FromException(new IOException("radio lost")) : Task.CompletedTask; }))
        {
            failed.Fault += _ => faults++;failed.Begin("S25");failed.Post(2, Hid.MouseReports(31, 1, 1).Single());
            await failed.FlushAsync().WaitAsync(TimeSpan.FromSeconds(5));
            // Fault may append releases behind the first flush; wait for them too.
            await failed.FlushAsync().WaitAsync(TimeSpan.FromSeconds(5));
            check(failed.Failed && !failed.IsActive && faults == 1 && attempts == 3, "transport failure stops capture and attempts keyboard/mouse release exactly once");
        }
        entered = new(TaskCreationOptions.RunContinuationsAsynchronously);unblock = new(TaskCreationOptions.RunContinuationsAsynchronously);sent.Clear();
        await using (var full = new HidReportPump(async (host, report, payload, ct) =>
        { entered.TrySetResult();await unblock.Task.WaitAsync(ct);sent.Add((host, report, payload)); }))
        {
            faults = 0;full.Fault += _ => faults++;full.Begin("S25");full.Post(2, Hid.MouseReports(1, 0, 0).Single());
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            for (int i = 0; i < 513; i++) full.Post(2, Hid.MouseReports(1, 1, 1).Single());
            unblock.SetResult();await full.FlushAsync().WaitAsync(TimeSpan.FromSeconds(5));
            check(full.Failed && faults == 1 && sent.Count == 3 && sent.Skip(1).All(e => e.Data.All(b => b == 0)), "backpressure never drops an up or replays a backlog after overflow");
        }
    }
}
