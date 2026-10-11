package com.galaxybridge.lan;

import java.util.Locale;

/** One physical hold binds the Android drop, preparation and desktop handoff. */
final class EdgeFileDrag {
    static final long MAX_HOLD=300000;
    static final class Pointer {
        final int epoch,side,width,height,buttons;
        final float x,y;
        final boolean stroke;
        Pointer(int epoch,int side,int width,int height,float x,float y,int buttons,boolean stroke) {
            this.epoch=epoch;this.side=side;this.width=width;this.height=height;
            this.x=x;this.y=y;this.buttons=buttons;this.stroke=stroke;
        }
    }
    private Pointer origin;
    private EdgeStrip zone;
    private long started;
    boolean waiting() { return origin!=null; }
    static Pointer project(Pointer pointer,EdgeStrip zone) {
        if(pointer==null || zone==null || Float.isNaN(pointer.x) || Float.isNaN(pointer.y) ||
            pointer.x<0 || pointer.y<0 || pointer.x>=pointer.width || pointer.y>=pointer.height) return null;
        float x=pointer.x,y=pointer.y;
        switch(pointer.side) {
            case 0:if(x>=zone.x+zone.width || y<zone.y || y>=zone.y+zone.height) return null;x=Math.max(x,zone.x);break;
            case 1:if(x<zone.x || y<zone.y || y>=zone.y+zone.height) return null;x=Math.min(x,zone.x+zone.width-1);break;
            case 2:if(y<zone.y || x<zone.x || x>=zone.x+zone.width) return null;y=Math.min(y,zone.y+zone.height-1);break;
            case 3:if(y>=zone.y+zone.height || x<zone.x || x>=zone.x+zone.width) return null;y=Math.max(y,zone.y);break;
            default:return null;
        }
        if(!zone.contains(x,y)) return null;
        return new Pointer(pointer.epoch,pointer.side,pointer.width,pointer.height,x,y,pointer.buttons,pointer.stroke);
    }
    Pointer clamp(Pointer pointer) { return project(pointer,zone); }
    boolean begin(Pointer pointer,EdgeStrip bounds,long now) {
        if(waiting() || pointer==null || !pointer.stroke || pointer.epoch<1 || pointer.buttons!=1 ||
            bounds==null || !bounds.contains(pointer.x,pointer.y)) return false;
        origin=pointer;zone=bounds;started=now;return true;
    }
    boolean valid(Pointer pointer,long now) {
        return origin!=null && pointer!=null && now>=started && now-started<=MAX_HOLD &&
            pointer.epoch==origin.epoch && pointer.side==origin.side &&
            pointer.width==origin.width && pointer.height==origin.height && pointer.buttons==1 &&
            zone.contains(pointer.x,pointer.y);
    }
    String consume(Pointer pointer,long now) {
        if(!valid(pointer,now)) return null;
        String line=String.format(Locale.ROOT,"GB_EDGE2 %d 1 %d %d %.3f %.3f 1 %d %d %d %d",
            pointer.epoch,pointer.width,pointer.height,pointer.x,pointer.y,zone.x,zone.y,zone.width,zone.height);
        cancel();return line;
    }
    void cancel() { origin=null;zone=null; }
}
