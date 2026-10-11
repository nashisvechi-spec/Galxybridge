package com.galaxybridge.lan;

public final class WheelGestureTest {
    private static int checks;
    private static void check(boolean value,String reason) { if(!value) throw new AssertionError(reason);checks++; }
    public static void main(String[] args) {
        for(int[] size:new int[][] {{1080,2400},{2400,1080},{320,480},{100,100}})
        for(int steps:new int[] {-32,-1,1,32})
        for(float cx:new float[] {0,size[0]/2f,size[0]-1})
        for(float cy:new float[] {0,size[1]/2f,size[1]-1}) {
            WheelGesture wheel=new WheelGesture(size[0],size[1],cx,cy,steps,true);
            float[] p=wheel.pinch();
            check(p[2]>0 && p[3]>0 && (steps>0?p[3]>p[2]:p[3]<p[2]),"wheel direction maps to zoom in/out");
            float radius=Math.max(p[2],p[3]);
            check(p[0]-radius>=0 && p[0]+radius<size[0] && p[1]>0 && p[1]<size[1],"both fingers remain on screen at every edge");
            float[] swipe=wheel.swipe();
            check(swipe[1]>=0 && swipe[2]<size[1] && (steps>0?swipe[2]>swipe[1]:swipe[2]<swipe[1]),"scroll at an edge remains nonzero in the requested direction");
        }
        WheelGesture first=new WheelGesture(1080,2400,500,800,1,true);
        check(first.merge(new WheelGesture(1080,2400,550,850,3,true)) && first.steps==4 && first.x==550,"rapid wheel ticks coalesce at the latest pointer");
        check(!first.merge(new WheelGesture(1080,2400,550,850,1,false)) && first.steps==4,"scroll and zoom never merge");
        check(!first.merge(new WheelGesture(2400,1080,550,850,1,true)),"rotation does not merge old geometry");
        check(first.merge(new WheelGesture(1080,2400,550,850,-4,true)) && first.steps==0,"opposite pending ticks cancel");
        for(int i=0;i<100;i++) first.merge(new WheelGesture(1080,2400,550,850,1,true));
        check(first.steps==32,"queued gesture accumulation is bounded");
        System.out.println("PASS: "+checks+" wheel gesture assertions");
    }
}
