package com.galaxybridge.lan;

/** Actual drag LOCATION, not ENTERED or a projected mouse point, arms touch-up. */
final class FileDropContact {
    static final long DWELL=200,FRESH=600;
    private EdgeFileDrag.Pointer origin;
    private EdgeStrip zone;
    private long entered,last;
    boolean location(EdgeFileDrag.Pointer pointer,EdgeStrip bounds,float localX,float localY,long now) {
        if(pointer==null || !pointer.stroke || pointer.buttons!=1 || pointer.epoch<1 || bounds==null ||
            !bounds.contains(pointer.x,pointer.y) || !bounds.contains(bounds.x+localX,bounds.y+localY)) {
            clear();return false;
        }
        if(!same(pointer,bounds) || now<last || now-last>FRESH) { origin=pointer;zone=bounds;entered=now; }
        last=now;return true;
    }
    boolean ready(EdgeFileDrag.Pointer pointer,EdgeStrip bounds,long now) {
        return same(pointer,bounds) && pointer.stroke && pointer.buttons==1 && zone.contains(pointer.x,pointer.y) &&
            now>=last && now-last<=FRESH && now>=entered && now-entered>=DWELL;
    }
    private boolean same(EdgeFileDrag.Pointer pointer,EdgeStrip bounds) {
        return origin!=null && pointer!=null && bounds!=null && pointer.epoch==origin.epoch && pointer.side==origin.side &&
            pointer.displayId==origin.displayId &&
            pointer.width==origin.width && pointer.height==origin.height && bounds.x==zone.x && bounds.y==zone.y &&
            bounds.width==zone.width && bounds.height==zone.height;
    }
    void clear() { origin=null;zone=null; }
}
