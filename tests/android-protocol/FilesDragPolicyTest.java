package com.galaxybridge.lan;

import org.json.JSONObject;

public final class FilesDragPolicyTest {
    private static int checks;
    private static void check(boolean value,String reason) { if(!value) throw new AssertionError(reason);checks++; }
    public static void main(String[] args) throws Exception {
        String batch="aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        JSONObject cancel=Wire.dragCancel(batch),again=Wire.dragCancel(batch);
        check("dragCancel".equals(cancel.getString("type")) && batch.equals(cancel.getString("batch")),"cancel retains opaque batch scope");
        check(Wire.hex(cancel.getString("id"),32),"release cancellation carries valid request ID");
        check(!cancel.getString("id").equals(again.getString("id")),"each cancel has a fresh correlation ID");
        boolean bad=false;try { Wire.dragCancel("../outside"); } catch(IllegalArgumentException e) { bad=true; }
        check(bad,"invalid batch cannot produce cancellation");
        FilesForeground focus=new FilesForeground();
        check(focus.refresh(1000) && !focus.refresh(1001) && !focus.refresh(1099),"mouse packets reuse cached focus for 100ms");
        check(focus.refresh(1100),"foreground tree periodically refreshed");
        focus.invalidate();check(focus.refresh(1101),"window-change event forces fresh tree");
        focus.reset();check(focus.refresh(1102),"capture reset forces fresh tree");
        check(!focus.check(null,true,1000),"unknown tree cannot enable routing without confirmed app");
        check(focus.check("com.galaxybridge.files",true,1000),"confirmed file manager routes pointer");
        check(focus.check(null,true,1050) && focus.check(null,true,2000),"brief missing tree does not abort held upload");
        check(!focus.check(null,true,2001),"unknown window has a bounded grace");
        check(!focus.check(null,true,999),"future confirmation rejected");
        focus.check("com.galaxybridge.files",true,3000);
        check(!focus.check("com.other.app",true,3001) && !focus.check(null,true,3002),"confirmed app change cancels immediately");
        focus.check("com.galaxybridge.files",true,4000);
        check(!focus.check(null,false,4001) && !focus.check(null,true,4002),"lock/inactive capture resets grace");
        focus.check("com.galaxybridge.files",true,5000);focus.reset();
        check(!focus.check(null,true,5001),"capture reset cannot inherit old app permission");
        for(int side=0;side<4;side++) {
            FilesDragMotion motion=new FilesDragMotion();
            check(!motion.outward(side),"initial hold cannot invent outward movement");
            int dx=side==0?-4:side==1?4:0,dy=side==2?4:side==3?-4:0;
            motion.update(dx,dy);check(motion.outward(side),"outward movement matches desktop policy");
            for(int i=0;i<20;i++) motion.update(0,0);
            check(motion.outward(side),"waiting for preparation and button packets preserve motion");
            motion.update(-dx,-dy);check(!motion.outward(side),"inward jitter waits instead of handing off rejected drag");
            motion.update(dx,dy);check(motion.outward(side),"fresh outward movement permits retry within same hold");
            motion.update(side<2?0:5,side<2?5:0);
            check(!motion.outward(side),"parallel motion cannot trigger premature desktop handoff");
            motion.reset();check(!motion.outward(side),"reset removes old capture direction");
        }
        System.out.println("PASS: "+checks+" file manager transfer policy assertions");
    }
}
