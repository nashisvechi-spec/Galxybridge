package com.galaxybridge.lan;

public final class ClipHistoryTest {
    private static int checks;
    private static void check(boolean value) { if(!value) throw new AssertionError();checks++; }
    public static void main(String[] args) {
        ClipHistory history=new ClipHistory();
        check(history.shouldSend("phone text",100));
        history.delivered(100);
        check(!history.shouldSend("phone text",100));
        check(history.shouldSend("phone text",101)); // Explicitly copied again.
        history.received("PC text 🌍",200);
        check(!history.shouldSend("PC text 🌍",199));
        check(!history.shouldSend("PC text 🌍",200));
        check(history.shouldSend("new phone text",200));
        check(history.shouldSend("PC text 🌍",201)); // Same text, newer local copy.
        check(history.shouldSend("failed delivery",300));
        check(history.shouldSend("failed delivery",300)); // A failed ack does not consume it.
        history.delivered(300);
        check(!history.shouldSend("failed delivery",300));
        check(new ClipHistory().shouldSend("new session",300));
        System.out.println("PASS: "+checks+" clipboard history assertions");
    }
}
