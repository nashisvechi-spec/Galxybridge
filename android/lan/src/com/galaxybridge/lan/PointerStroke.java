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
    private long startedAt;
    private boolean down=true, started, finished, stationaryHold;
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
    // Called only after the last dispatched continuation has completed inside
    // the file target. Discard pending motion; up stays at that exact endpoint.
    boolean releaseHere(long now) {
        if(!down || !started || finished) return false;
        Point endpoint=points.get(0);points.clear();points.add(endpoint);
        down=false;releasedAt=now;return true;
    }
    boolean click(int timeout) { return !started && !down && !press.isDrag() && releasedAt-pressedAt<timeout; }
    long waitForIntent(long now,int timeout) {
        // Until release, movement or the long-press deadline, this could still
        // be an ordinary click. Do not inject a finger and then click a node too.
        if(!started && down && !press.isDrag()) return Math.max(0,pressedAt+timeout-now);
        // A classified hold must stay down long enough for Android to recognize
        // it, even if the physical button is released soon after injection.
        if(started && !down && stationaryHold && !press.isDrag()) return Math.max(0,startedAt+timeout+100L-now);
        return 0;
    }
    Segment take(long now,int timeout) {
        if(waitForIntent(now,timeout)>0) return null;
        if(finished || (started && down && points.size()==1)) return null;
        if(!started) { startedAt=now;stationaryHold=down && !press.isDrag(); }
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
