package com.galaxybridge.lan;

public final class PointerEdgeIntentTest {
    private static int checks;
    private static void check(boolean value,String reason) { if(!value) throw new AssertionError(reason);checks++; }
    public static void main(String[] args) {
        PointerEdgeIntent intent=new PointerEdgeIntent();
        check(!intent.active(500),"capture starts without an exit intent");
        for(int side=0;side<4;side++) {
            intent.clear();
            float x=side==0?0:side==1?1079:500,y=side==2?2399:side==3?0:1200;
            int dx=side==0?-5:side==1?5:0,dy=side==2?5:side==3?-5:0;
            intent.move(side,1080,2400,x,y,dx,dy,0,0,500);
            check(intent.active(1100),"confirmed exit survives Wi-Fi delay and host stopping, side "+side);
            intent.move(side,1080,2400,x,y,dy,dx,0,0,1100);
            check(intent.active(1100),"tangential movement preserves the recent exit");
            check(!intent.active(1501),"exit confirmation expires");
            check(!intent.active(499),"future confirmation is invalid");
            intent.move(side,1080,2400,x,y,-dx,-dy,0,0,1100);
            check(!intent.active(1100),"reversal clears confirmation even at the edge");
            intent.move(side,1080,2400,x,y,dx,dy,1,0,1200);
            check(!intent.active(1200),"drag cannot confirm an exit");
            intent.move(side,1080,2400,x,y,dx,dy,0,1,1300);
            check(!intent.active(1300),"scrolling cannot confirm an exit");
        }
        intent.move(0,1080,2400,1,100,-1,0,0,0,2000);
        check(!intent.active(2000),"near-edge movement is insufficient");
        intent.move(0,1080,2400,0,100,-1,0,0,0,2000);
        intent.clear();check(!intent.active(2000),"reset invalidates old capture or rotated display");
        System.out.println("PASS: "+checks+" pointer edge intent assertions");
    }
}
