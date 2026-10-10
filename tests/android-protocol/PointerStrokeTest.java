package com.galaxybridge.lan;

public final class PointerStrokeTest {
    private static int checks;
    private static void check(boolean value,String reason) {
        if(!value) throw new AssertionError(reason);checks++;
    }
    private static void point(PointerStroke.Point p,float x,float y,String reason) {
        check(p.x==x && p.y==y,reason);
    }
    public static void main(String[] args) {
        PointerStroke hold=new PointerStroke(8,100,100,0);
        PointerStroke.Segment down=hold.take(500);
        check(down!=null && down.continues,"mouse down reaches Android before mouse up");
        check(down.duration==16,"starting a long press does not wait for its duration");
        point(down.points[0],100,100,"touch begins at the pointer tip");
        check(hold.take(500)==null,"pausing leaves the existing finger down without repeated taps");
        hold.move(106,101);
        check(hold.take(500)==null,"mouse jitter does not disturb a long press");
        hold.move(130,100);
        PointerStroke.Segment move=hold.take(500);
        check(move.continues && move.points.length==2,"drag moves while button is still held");
        point(move.points[0],100,100,"movement continues from the exact prior endpoint");
        point(move.points[1],130,100,"movement reaches the new pointer position");
        check(hold.take(500)==null,"selection can be held stationary without releasing");
        hold.move(160,120);hold.move(200,150);
        PointerStroke.Segment follow=hold.take(500);
        point(follow.points[0],130,100,"packets received during dispatch stay connected");
        point(follow.points[follow.points.length-1],200,150,"pending motion is not lost");
        hold.release(210,155,10000);
        PointerStroke.Segment up=hold.take(500);
        check(!up.continues && up.duration==16,"long holds release promptly without replaying ten seconds");
        point(up.points[0],200,150,"release continues the last injected stroke");
        point(up.points[up.points.length-1],210,155,"release packet motion is included");
        check(hold.take(500)==null,"mouse up is emitted only once");

        PointerStroke tap=new PointerStroke(8,50,60,100);
        tap.move(52,62);tap.release(53,61,120);
        check(tap.click(500),"a fast unstarted click retains accessibility node activation");
        PointerStroke.Segment fast=tap.take(500);
        check(!fast.continues && fast.points.length==1,"fast click fallback is a single completed touch");
        point(fast.points[0],50,60,"jitter cannot move a quick click to another control");
        check(!tap.click(500),"an injected tap cannot be activated a second time");

        PointerStroke stationary=new PointerStroke(8,30,40,0);
        stationary.take(500);stationary.release(30,40,600);
        check(!stationary.click(500),"already injected long press cannot become a node click");
        PointerStroke.Segment finish=stationary.take(500);
        check(finish.points.length==1 && !finish.continues,"a stationary held finger gets a matching up");
        point(finish.points[0],30,40,"stationary up stays at the held position");

        PointerStroke burst=new PointerStroke(1,0,0,0);
        burst.take(500);
        for(int i=2;i<1000;i++) burst.move(i,i);
        PointerStroke.Segment bounded=burst.take(500);
        check(bounded.points.length==PointerStroke.MAX_POINTS,"slow dispatch cannot grow an unbounded mouse backlog");
        point(bounded.points[0],0,0,"backpressure preserves the connection endpoint");
        point(bounded.points[bounded.points.length-1],999,999,"backpressure keeps the latest position");
        burst.move(0,0);burst.release(0,0,800);
        PointerStroke.Segment back=burst.take(500);
        check(!back.continues,"returning to origin still finishes a drag");
        point(back.points[0],999,999,"return path continues from the dispatched endpoint");
        point(back.points[back.points.length-1],0,0,"return path is preserved");
        burst.move(700,700);burst.release(700,700,900);
        check(burst.take(500)==null,"late packets cannot revive a finished press");

        PointerStroke one=new PointerStroke(8,10,10,0),two=new PointerStroke(8,20,20,40);
        one.take(500);one.release(10,10,30);two.release(20,20,60);
        check(!one.take(500).continues && two.click(500),"rapid subsequent clicks do not share held-finger state");
        System.out.println("PASS: "+checks+" live pointer stroke assertions");
    }
}
