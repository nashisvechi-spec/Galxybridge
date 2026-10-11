package com.galaxybridge.lan;

// Keep a short confirmation of an actual outward move, rather than guessing
// its direction from the last mouse packet received by Windows.
final class PointerEdgeIntent {
    private long outwardAt=-1;
    void clear() { outwardAt=-1; }
    void move(int side,int width,int height,float x,float y,int dx,int dy,int buttons,int wheel,long now) {
        boolean atEdge=side==0?x==0:side==1?x==width-1:side==2?y==height-1:y==0;
        int direction=side==0?-dx:side==1?dx:side==2?dy:-dy;
        if(!atEdge || buttons!=0 || wheel!=0 || direction<0) clear();
        else if(direction>0) outwardAt=now;
    }
    boolean active(long now) { return outwardAt>=0 && now>=outwardAt && now-outwardAt<=1000; }
}
