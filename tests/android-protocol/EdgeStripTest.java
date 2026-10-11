package com.galaxybridge.lan;

public final class EdgeStripTest {
    private static int checks;
    private static void check(boolean value,String reason) {
        if(!value) throw new AssertionError(reason);checks++;
    }
    public static void main(String[] args) {
        for(int side=0;side<4;side++) {
            EdgeStrip strip=EdgeStrip.create(1080,2400,20,80,30,100,side);
            check(strip.width==1 || strip.height==1,"only one physical pixel intercepts hover");
            check(strip.x>=20 && strip.y>=80 && strip.x+strip.width<=1050 && strip.y+strip.height<=2300,"strip stays inside system insets");
            check(strip.contains(strip.x+0.25f,strip.y+0.25f),"fractional mouse position is inside strip");
            check(!strip.contains(strip.x-0.01f,strip.y) && !strip.contains(strip.x+strip.width,strip.y),"exclusive horizontal bounds");
            check(!strip.contains(strip.x,strip.y-0.01f) && !strip.contains(strip.x,strip.y+strip.height),"exclusive vertical bounds");
            check(!strip.contains(Float.NaN,strip.y) && !strip.contains(strip.x,Float.POSITIVE_INFINITY),"nonfinite positions rejected");
            EdgeStrip rotated=EdgeStrip.create(2400,1080,90,20,100,30,side);
            check(rotated.x>=90 && rotated.y>=20 && rotated.x+rotated.width<=2300 && rotated.y+rotated.height<=1050,"landscape cutout and navigation bounds");
        }
        check(EdgeStrip.create(1080,2400,0,0,0,0,0).x==0,"phone right of PC returns through left edge");
        check(EdgeStrip.create(1080,2400,0,0,0,0,1).x==1079,"phone left of PC returns through right edge");
        check(EdgeStrip.create(1080,2400,0,0,0,0,2).y==2399,"phone above PC returns through bottom edge");
        check(EdgeStrip.create(1080,2400,0,0,0,0,3).y==0,"phone below PC returns through top edge");
        for(int[] invalid:new int[][]{{0,2400,0,0,0,0,0},{1080,2400,-1,0,0,0,0},
            {1080,2400,20,0,1060,0,0},{1080,2400,0,80,0,2320,0},
            {1080,2400,0,0,0,0,4},{20001,2400,0,0,0,0,0},{1080,2400,Integer.MAX_VALUE,0,0,0,0}}) {
            boolean rejected=false;
            try { EdgeStrip.create(invalid[0],invalid[1],invalid[2],invalid[3],invalid[4],invalid[5],invalid[6]); }
            catch(IllegalArgumentException e) { rejected=true; }
            check(rejected,"invalid geometry cannot create a screen-wide overlay");
        }
        System.out.println("PASS: "+checks+" physical mouse edge assertions");
    }
}
