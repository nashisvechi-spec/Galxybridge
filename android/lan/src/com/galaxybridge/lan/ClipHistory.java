package com.galaxybridge.lan;

// Session-only metadata. Clipboard text is never persisted or logged.
final class ClipHistory {
    private long delivered=-1, receivedAt=-1;
    private String received;
    boolean shouldSend(String text,long timestamp) {
        return timestamp!=delivered && !(timestamp<=receivedAt && text.equals(received));
    }
    void received(String text,long timestamp) { received=text;receivedAt=timestamp; }
    void delivered(long timestamp) { delivered=timestamp; }
}
