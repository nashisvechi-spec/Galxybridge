using GalaxyBridge.Core;

static class PointerMotionTests
{
    public static void Run(Action<bool, string> check)
    {
        PointerMotion motion = new();
        List<long> sent = []; int totalX = 0, totalY = 0;
        // A 1000 Hz mouse and the independent 16ms fallback timer share the
        // pacing window: they must neither flood the link nor lose movement.
        for (int now = 0; now < 1000; now++)
        {
            motion.Add(1, -2);
            if (motion.TryTake(now, false, out int dx, out int dy))
            { sent.Add(now); totalX += dx; totalY += dy; }
            if (now % 16 == 0 && motion.TryTake(now, false, out dx, out dy))
            { sent.Add(now); totalX += dx; totalY += dy; }
        }
        check(sent.Count == 125 && sent[0] == 0, "1000 Hz mouse reaches 125 paced updates without waiting for the UI timer");
        check(sent.Zip(sent.Skip(1)).All(pair => pair.Second - pair.First >= 8), "mouse hook and fallback timer cannot send duplicate bursts");
        check(motion.TryTake(1000, true, out int tailX, out int tailY) && totalX + tailX == 1000 && totalY + tailY == -2000,
            "coalescing preserves every pixel in both axes, including the final tail");
        motion.Reset();
        for (int now = 0; now < 1000; now += 8)
        {
            motion.Add(2, 3);
            check(motion.TryTake(now, false, out int dx, out int dy) && dx == 2 && dy == 3,
                "125 Hz mouse sends each sample promptly " + now);
        }
        motion.Reset(.25);
        for (int i = 0; i < 3; i++)
        { motion.Add(1, -1); check(!motion.TryTake(i, true, out _, out _), "fractional sensitivity does not create zero packets"); }
        motion.Add(1, -1);
        check(motion.TryTake(3, true, out int fractionX, out int fractionY) && fractionX == 1 && fractionY == -1,
            "fractional sensitivity preserves both positive and negative remainders");
        motion.Add(8, 4);
        check(!motion.TryTake(4, false, out _, out _), "normal movement remains paced");
        check(motion.TryTake(4, true, out int forcedX, out int forcedY) && forcedX == 2 && forcedY == 1,
            "button or modifier change flushes preceding motion immediately");
        motion.Add(8, 8); motion.Reset(4);
        check(!motion.TryTake(100, true, out _, out _), "new capture cannot replay old pending motion");
        motion.Add(3000, -3000);
        check(motion.TryTake(100, false, out int boundedX, out int boundedY) && boundedX == 8192 && boundedY == -8192,
            "large scaled movement respects the phone packet limit");
        check(motion.TryTake(108, false, out int restX, out int restY) && boundedX + restX == 12000 && boundedY + restY == -12000,
            "packet bounding retains the rest instead of losing movement");
        check(!motion.TryTake(200, true, out _, out _), "stationary pointer sends no motion packet");
        motion.Add(5000, -5000); int forceTotalX = 0, forceTotalY = 0, parts = 0;
        while (motion.TryTake(201, true, out int partX, out int partY))
        { parts++; forceTotalX += partX; forceTotalY += partY; }
        check(parts == 3 && forceTotalX == 20000 && forceTotalY == -20000,
            "forced drain delivers all bounded packets before a button transition");
    }
}
