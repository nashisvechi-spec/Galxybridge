package com.galaxybridge.files;

/** Gesture intent, independent of Android's injected touch tool type. */
final class FilePointer {
    private Object pressed,lastClick;
    private float originX,originY;
    private long clickedAt;
    private boolean dragging;
    void down(Object item,float x,float y) { pressed=item;originX=x;originY=y;dragging=false; }
    boolean move(float x,float y,float slop) {
        if(pressed==null || dragging || Math.hypot(x-originX,y-originY)<=slop) return false;
        dragging=true;lastClick=null;return true;
    }
    Object item() { return pressed; }
    boolean dragging() { return dragging; }
    // 0 = no click, 1 = select, 2 = open.
    int up(Object under,long now) {
        int result=0;
        if(!dragging && pressed!=null && pressed.equals(under)) {
            result=pressed.equals(lastClick) && now>=clickedAt && now-clickedAt<=400?2:1;
            lastClick=result==2?null:pressed;clickedAt=now;
        } else lastClick=null;
        pressed=null;dragging=false;return result;
    }
    void cancel() { pressed=null;lastClick=null;dragging=false; }
}
