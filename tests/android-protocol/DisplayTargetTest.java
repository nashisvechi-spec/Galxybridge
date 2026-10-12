package com.galaxybridge.lan;

public final class DisplayTargetTest {
    private static int checks;
    private static void check(boolean value,String reason) { if(!value) throw new AssertionError(reason);checks++; }
    private static DisplayTarget.Screen screen(int id,int w,int h,boolean on,boolean presentation) {
        return new DisplayTarget.Screen(id,w,h,0,on,id!=0,presentation);
    }
    public static void main(String[] args) {
        DisplayTarget.Screen phone=screen(0,1080,2340,true,false),dex=screen(9,1920,1080,true,true);
        check(DisplayTarget.choose(-1,new DisplayTarget.Screen[]{phone})==phone,"phone-only behavior preserved");
        check(DisplayTarget.choose(-1,new DisplayTarget.Screen[]{phone,dex})==dex,"DeX wins automatic selection without hardcoded ID 1");
        check(DisplayTarget.choose(0,new DisplayTarget.Screen[]{phone,dex})==phone,"explicit phone choice overrides connected DeX");
        check(DisplayTarget.choose(9,new DisplayTarget.Screen[]{phone,dex})==dex,"manual external display selection");
        check(DisplayTarget.choose(9,new DisplayTarget.Screen[]{phone})==null,"missing manual external screen cannot silently send input to phone");
        check(DisplayTarget.choose(-2,new DisplayTarget.Screen[]{phone,dex})==null,"invalid preference cannot invent a screen");
        check(DisplayTarget.choose(-1,new DisplayTarget.Screen[]{})==null,"no screen means no input target");
        check(DisplayTarget.choose(-1,new DisplayTarget.Screen[]{null,phone})==phone,"missing entry ignored");
        check(DisplayTarget.choose(-1,new DisplayTarget.Screen[]{phone,screen(9,1920,1080,false,true)})==phone,"off/inaccessible/private display ignored");
        check(DisplayTarget.choose(-1,new DisplayTarget.Screen[]{screen(0,1080,2340,false,false),dex})==dex,"phone screen off does not block active DeX");
        DisplayTarget.Screen virtual=screen(2,1000,1000,true,false),present=screen(5,3840,2160,true,true);
        check(DisplayTarget.choose(-1,new DisplayTarget.Screen[]{virtual,dex,phone,present})==present,"presentation display preferred; stable ordering across candidates");
        check(DisplayTarget.choose(2,new DisplayTarget.Screen[]{virtual,dex})==virtual,"explicit accessible display takes priority over presentation rank");
        check(DisplayTarget.choose(-1,new DisplayTarget.Screen[]{virtual,phone})==virtual,"public external display may omit presentation flag");
        for(int[] invalid:new int[][]{{9,0,1080},{9,1920,15},{9,20001,1080},{-3,1920,1080}})
            check(DisplayTarget.choose(-1,new DisplayTarget.Screen[]{phone,screen(invalid[0],invalid[1],invalid[2],true,true)})==phone,"invalid display metrics/ID ignored");
        check(DisplayTarget.same(phone,screen(0,1080,2340,true,false)),"unchanged metrics keep current capture");
        check(!DisplayTarget.same(dex,screen(2,1920,1080,true,true)),"same resolution on another display cancels capture");
        check(!DisplayTarget.same(dex,screen(9,1280,720,true,true)),"resolution change cancels capture");
        check(!DisplayTarget.same(dex,new DisplayTarget.Screen(9,1920,1080,1,true,true,true)),"rotation change cancels capture");
        check(!DisplayTarget.same(dex,new DisplayTarget.Screen(9,1920,1080,0,true,true,true,240)),"density change recreates cursor resources");
        check(DisplayTarget.same(null,null) && !DisplayTarget.same(dex,null) && !DisplayTarget.same(null,dex),"removal and first connection change capture identity");
        check(DisplayTarget.matches(9,9) && !DisplayTarget.matches(9,0) && !DisplayTarget.matches(-1,-1),"IPC never routes monitor coordinates into phone activity");
        EdgeStrip zone=EdgeStrip.create(1920,1080,0,24,0,48,0);
        EdgeFileDrag.Pointer held=new EdgeFileDrag.Pointer(7,0,1920,1080,0,500,1,true,9);
        EdgeFileDrag drag=new EdgeFileDrag();check(drag.begin(held,zone,1000),"DeX source hold at actual monitor edge");
        check(drag.valid(drag.clamp(held),1100) && drag.clamp(held).displayId==9,"projection preserves display identity");
        check(!drag.valid(new EdgeFileDrag.Pointer(7,0,1920,1080,0,500,1,true,0),1100),"same capture and metrics on phone cannot take DeX file hold");
        FileDropContact contact=new FileDropContact();contact.location(held,zone,0,500-zone.y,1000);
        check(contact.ready(held,zone,1200) && !contact.ready(new EdgeFileDrag.Pointer(7,0,1920,1080,0,500,1,true,0),zone,1200),"drop dwell bound to selected display");
        System.out.println("PASS: "+checks+" DeX display target assertions");
    }
}
