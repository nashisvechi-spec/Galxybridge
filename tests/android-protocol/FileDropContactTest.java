package com.galaxybridge.lan;

public final class FileDropContactTest {
    private static int checks;
    private static void check(boolean value,String reason) { if(!value) throw new AssertionError(reason);checks++; }
    private static EdgeFileDrag.Pointer pointer(int side,EdgeStrip zone,int epoch,int buttons,boolean stroke) {
        return new EdgeFileDrag.Pointer(epoch,side,1080,2340,zone.x+zone.width/2f,zone.y+zone.height/2f,buttons,stroke);
    }
    public static void main(String[] args) {
        for(int side=0;side<4;side++) {
            EdgeStrip zone=EdgeStrip.dropZone(1080,2340,18,24,18,48,side,3);
            check(zone.width>4 && zone.height>4,"visible target has real area");
            check(zone.x>=18 && zone.y>=24 && zone.x+zone.width<=1062 && zone.y+zone.height<=2292,"target respects insets");
            check(side==0?zone.x>18 && zone.x+zone.width<=270:side==1?zone.x>=810 && zone.x+zone.width<1062:
                side==2?zone.y>=1755 && zone.y+zone.height<2292:zone.y>24 && zone.y+zone.height<=585,"inset target stays in PC-facing quarter");
            EdgeFileDrag.Pointer p=pointer(side,zone,7,1,true);
            FileDropContact contact=new FileDropContact();
            check(!contact.ready(p,zone,1200),"ENTERED alone cannot trigger up");
            check(contact.location(p,zone,zone.width/2f,zone.height/2f,1000),"actual LOCATION arms dwell");
            check(!contact.ready(p,zone,1199) && contact.ready(p,zone,1200),"200ms dwell required");
            check(!contact.ready(p,zone,999) && !contact.ready(p,zone,1601),"future/stale contact cannot trigger up");
            check(!contact.ready(pointer(side,zone,8,1,true),zone,1200),"new capture requires fresh LOCATION");
            check(!contact.ready(pointer(side,zone,7,0,true),zone,1200),"physical release cancels dwell");
            check(!contact.ready(pointer(side,zone,7,3,true),zone,1200),"extra button cancels dwell");
            check(!contact.ready(pointer(side,zone,7,1,false),zone,1200),"finger-only or ended injection cannot drop");
            check(!contact.ready(new EdgeFileDrag.Pointer(7,side,2340,1080,p.x,p.y,1,true),zone,1200),"rotation cancels dwell");
            check(!contact.ready(new EdgeFileDrag.Pointer(7,side,1080,2340,500,1000,1,true),zone,1200),"mouse leaves target before dwell");
            check(!contact.location(p,zone,zone.width,0,1200),"outside actual shadow cannot be projected into target");
            check(!contact.ready(p,zone,1201),"outside LOCATION clears prior dwell");
            check(!contact.location(p,zone,Float.NaN,0,1200),"nonfinite drag coordinate rejected");
            check(!contact.location(pointer(side,zone,7,1,false),zone,1,1,1200),"no injected finger");
            check(!contact.location(null,zone,1,1,1200) && !contact.location(p,null,1,1,1200),"missing pointer/layout");
            contact.location(p,zone,1,1,2000);contact.clear();
            check(!contact.ready(p,zone,2300),"EXITED/ENDED clears contact");
            contact.location(p,zone,1,1,3000);contact.location(p,zone,1,1,3700);
            check(!contact.ready(p,zone,3701) && contact.ready(p,zone,3900),"stale contact starts a new dwell");
            contact.location(pointer(side,zone,8,1,true),zone,1,1,4000);
            check(!contact.ready(pointer(side,zone,8,1,true),zone,4001),"epoch change starts a new dwell");
            EdgeStrip shifted=EdgeStrip.dropZone(1080,2340,20,26,20,50,side,3);
            check(!contact.ready(pointer(side,shifted,8,1,true),shifted,4200),"layout change requires new contact");
            EdgeFileDrag hold=new EdgeFileDrag();check(hold.begin(p,zone,5000),"broad zone binds physical hold");
            check(hold.valid(hold.clamp(p),5200),"movement within zone remains valid");
            float ox=side==0?0:side==1?1079:p.x,oy=side==2?2339:side==3?0:p.y;
            EdgeFileDrag.Pointer outward=new EdgeFileDrag.Pointer(7,side,1080,2340,ox,oy,1,false);
            check(hold.valid(hold.clamp(outward),5200),"outward overshoot clamped only after touch up");
            check(hold.clamp(new EdgeFileDrag.Pointer(7,side,1080,2340,500,1000,1,false))==null,"inward movement cancels preparation");
            check(hold.consume(hold.clamp(outward),5201)!=null && !hold.waiting(),"broad bounds hand off once");
            for(int[] screen:new int[][]{{1080,2340},{2340,1080},{480,800},{1440,3200}}) {
                EdgeStrip z=EdgeStrip.dropZone(screen[0],screen[1],8,16,8,20,side,4);
                check(z.width>=8 && z.height>=8,"visible geometry across portrait/landscape/small displays");
                check(z.x>=8 && z.y>=16 && z.x+z.width<=screen[0]-8 && z.y+z.height<=screen[1]-20,"scaled zone stays usable");
            }
        }
        for(float density:new float[]{Float.NaN,Float.POSITIVE_INFINITY,0,-1,11}) {
            boolean rejected=false;try { EdgeStrip.dropZone(1080,2340,0,0,0,0,0,density); } catch(IllegalArgumentException e) { rejected=true; }
            check(rejected,"invalid density rejected");
        }
        boolean rejected=false;try { EdgeStrip.dropZone(1080,2340,265,0,0,0,0,3); } catch(IllegalArgumentException e) { rejected=true; }
        check(rejected,"no target when insets consume the edge quarter");
        System.out.println("PASS: "+checks+" visible file drop contact assertions");
    }
}
