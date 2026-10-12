package com.galaxybridge.lan;

/** Mirror the desktop's latest nonzero motion; button/heartbeat packets preserve it. */
final class FilesDragMotion {
    private int dx,dy;
    void update(int x,int y) { if(x!=0 || y!=0) { dx=x;dy=y; } }
    boolean outward(int side) { return side==0?dx<0:side==1?dx>0:side==2?dy>0:side==3 && dy<0; }
    void reset() { dx=dy=0; }
}
