package com.galaxybridge.lan;

/** Display IDs are not fixed: DeX may be display 2, 9, etc. */
final class DisplayTarget {
    static final int AUTO=-1;
    static final class Screen {
        final int id,width,height,rotation,density;
        final boolean available,external,presentation;
        Screen(int id,int width,int height,int rotation,boolean available,boolean external,boolean presentation) {
            this(id,width,height,rotation,available,external,presentation,160);
        }
        Screen(int id,int width,int height,int rotation,boolean available,boolean external,boolean presentation,int density) {
            this.id=id;this.width=width;this.height=height;this.rotation=rotation;
            this.available=available;this.external=external;this.presentation=presentation;
            this.density=density;
        }
        boolean usable() { return available && id>=0 && width>=16 && height>=16 && width<=20000 && height<=20000; }
    }
    static Screen choose(int requested,Screen[] screens) {
        Screen primary=null,best=null;
        for(Screen screen:screens) {
            if(screen==null || !screen.usable()) continue;
            if(requested>=0 && screen.id==requested) return screen;
            if(screen.id==0) primary=screen;
            if(screen.external && (best==null || screen.presentation && !best.presentation ||
                screen.presentation==best.presentation && screen.id<best.id)) best=screen;
        }
        return requested==AUTO?(best!=null?best:primary):null;
    }
    static boolean same(Screen a,Screen b) {
        return a==null?b==null:b!=null && a.id==b.id && a.width==b.width && a.height==b.height && a.rotation==b.rotation && a.density==b.density;
    }
    static boolean matches(int selected,int owner) { return selected>=0 && selected==owner; }
}
