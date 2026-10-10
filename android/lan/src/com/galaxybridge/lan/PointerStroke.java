package com.galaxybridge.lan;

import java.util.ArrayList;

// One physical press, split into short, connected touch strokes. Mouse packets
// received during dispatch belong to the next segment, never to a replay on up.
final class PointerStroke {
    static final int MAX_POINTS=64;
    private final PointerPress press;
    private final ArrayList<Point> points=new ArrayList<>();
    private final long pressedAt;
    private long releasedAt;
    private boolean down=true, started, finished;
    final float x,y;
    PointerStroke(float slop,float x,float y,long now) {
        this.x=x;this.y=y;pressedAt=now;
        press=new PointerPress(slop);press.begin(x,y);points.add(new Point(x,y));
    }
    void move(float x,float y) {
        if(!down || finished) return;
        press.move(x,y);
        if(!press.isDrag()) return;
        Point last=points.get(points.size()-1);
        if(last.x==x && last.y==y) return;
        // Retain the connection point and newest position if input outruns Android.
        if(points.size()==MAX_POINTS) points.remove(1);
        points.add(new Point(x,y));
    }
    void release(float x,float y,long now) {
        if(!down) return;
        move(x,y);down=false;releasedAt=now;
    }
    boolean click(int timeout) { return !started && !down && !press.isDrag() && releasedAt-pressedAt<timeout; }
    Segment take(int timeout) {
        if(finished || (started && down && points.size()==1)) return null;
        Point[] path=points.toArray(new Point[0]);
        long duration=started || down?16:press.duration(releasedAt-pressedAt,timeout);
        points.clear();points.add(path[path.length-1]);
        started=true;finished=!down;
        return new Segment(path,duration,down);
    }
    static final class Point {
        final float x,y;
        Point(float x,float y) { this.x=x;this.y=y; }
    }
    static final class Segment {
        final Point[] points;
        final long duration;
        final boolean continues;
        Segment(Point[] points,long duration,boolean continues) { this.points=points;this.duration=duration;this.continues=continues; }
    }
}
