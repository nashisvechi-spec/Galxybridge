package com.galaxybridge.lan;

public final class EdgeFileDragTest {
    private static int checks;
    private static void check(boolean value,String name) { if(!value) throw new AssertionError(name);checks++; }
    private static EdgeFileDrag.Pointer pointer(int epoch,int side,int w,int h,float x,float y,int buttons,boolean stroke) {
        return new EdgeFileDrag.Pointer(epoch,side,w,h,x,y,buttons,stroke);
    }
    public static void main(String[] args) {
        for(int side=0;side<4;side++) {
            EdgeStrip zone=EdgeStrip.create(1080,2340,18,24,18,48,side);
            float x=zone.width==1?zone.x:500,y=zone.height==1?zone.y:1000;
            EdgeFileDrag.Pointer start=pointer(7,side,1080,2340,x,y,1,true);
            float outsideX=side==0?0:side==1?1079:x,outsideY=side==2?2339:side==3?0:y;
            EdgeFileDrag.Pointer projected=EdgeFileDrag.project(pointer(7,side,1080,2340,outsideX,outsideY,1,true),zone);
            check(projected!=null && projected.x==x && projected.y==y,"outward motion clamps to actual inset");
            check(EdgeFileDrag.project(pointer(7,side,1080,2340,500,1000,1,true),zone)==null,"middle cannot project to edge");
            check(EdgeFileDrag.project(pointer(7,side,1080,2340,Float.NaN,y,1,true),zone)==null,"NaN cannot be projected");
            EdgeFileDrag drag=new EdgeFileDrag();
            check(!drag.begin(null,zone,1000),"missing pointer");
            check(!drag.begin(pointer(7,side,1080,2340,x,y,0,true),zone,1000),"finger/released hold");
            check(!drag.begin(pointer(7,side,1080,2340,x,y,3,true),zone,1000),"extra button");
            check(!drag.begin(pointer(7,side,1080,2340,x,y,1,false),zone,1000),"no injected source stroke");
            check(!drag.begin(pointer(0,side,1080,2340,x,y,1,true),zone,1000),"no capture");
            check(drag.begin(start,zone,1000),"source drag at inset edge");
            check(!drag.begin(start,zone,1001),"cannot replace armed hold");
            EdgeFileDrag.Pointer held=pointer(7,side,1080,2340,x,y,1,false);
            check(drag.valid(held,5000),"Android finger ended while physical hold remains");
            check(drag.valid(drag.clamp(pointer(7,side,1080,2340,outsideX,outsideY,1,false)),5000),"continued outward motion preserves preparation hold");
            check(!drag.valid(held,999),"future time");
            check(!drag.valid(held,1001+EdgeFileDrag.MAX_HOLD),"bounded preparation");
            check(!drag.valid(pointer(8,side,1080,2340,x,y,1,false),1002),"new capture");
            check(!drag.valid(pointer(7,(side+1)%4,1080,2340,x,y,1,false),1002),"new edge side");
            check(!drag.valid(pointer(7,side,2340,1080,x,y,1,false),1002),"rotation");
            check(!drag.valid(pointer(7,side,1080,2340,x,y,0,false),1002),"physical up");
            check(!drag.valid(pointer(7,side,1080,2340,500,1000,1,false),1002),"left edge strip");
            check(!drag.valid(pointer(7,side,1080,2340,Float.NaN,y,1,false),1002),"invalid coordinate");
            check(drag.consume(pointer(7,side,1080,2340,x,y,0,false),1002)==null,"released hold cannot hand off");
            String line=drag.consume(held,1003);
            check(line!=null && line.startsWith("GB_EDGE2 7 1 1080 2340 ") && line.endsWith(" 1 "+zone.x+" "+zone.y+" "+zone.width+" "+zone.height),"measured edge encoded");
            check(!drag.waiting() && drag.consume(held,1004)==null,"handoff consumed once");
            check(drag.begin(start,zone,2000),"new explicit drag");drag.cancel();
            check(!drag.waiting() && !drag.valid(held,2001),"cancel prevents delayed preparation callback");
        }
        System.out.println("PASS: "+checks+" edge file drag assertions");
    }
}
