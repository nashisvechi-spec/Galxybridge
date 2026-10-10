package com.galaxybridge.lan;

// Keep mouse jitter below Android's touch slop out of the injected gesture.
final class PointerPress {
    private final float slop;
    private float x,y;
    private boolean moved;
    PointerPress(float slop) { this.slop=Math.max(1,slop); }
    void begin(float x,float y) { this.x=x; this.y=y; moved=false; }
    void move(float x,float y) {
        if(Math.hypot(x-this.x,y-this.y)>slop) moved=true;
    }
    boolean isDrag() { return moved; }
    long duration(long elapsed,int longPressTimeout) {
        if(moved) return Math.max(100,Math.min(1000,elapsed));
        return elapsed>=longPressTimeout?longPressTimeout+100L:60;
    }
}
