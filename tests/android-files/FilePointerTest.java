package com.galaxybridge.files;

public final class FilePointerTest {
    private static int checks;
    private static void check(boolean value,String label) { if(!value) throw new AssertionError(label);checks++; }
    public static void main(String[] args) {
        FilePointer p=new FilePointer();
        p.down("a",10,10);check(p.up("a",100)==1,"single select");
        p.down("a",10,10);check(p.up("a",300)==2,"double open");
        p.down("a",10,10);check(p.up("a",450)==1,"triple click begins new pair");
        p.down("b",10,10);check(p.up("b",500)==1,"different item is not double click");
        p.down("b",10,10);check(!p.move(12,12,8),"jitter is not drag");
        check(p.move(30,10,8),"movement begins drag");check(!p.move(40,10,8),"prepare emitted once");
        check(p.dragging() && "b".equals(p.item()),"drag retains known file");
        check(p.up("b",550)==0,"drag release never opens");
        p.down("b",10,10);check(p.up("b",600)==1,"click after drag not double");
        p.down("b",10,10);check(p.up("a",650)==0,"release over other row not click");
        p.down(null,10,10);check(!p.move(30,10,8),"toolbar not file drag");
        p.down("b",10,10);p.cancel();check(p.up("b",700)==0,"cancel blocks stale release");
        p.down("b",10,10);check(p.up("b",1000)==1,"first click after cancel");
        p.down("b",10,10);check(p.up("b",1500)==1,"late second click not double");
        System.out.println("PASS: "+checks+" file-pointer checks");
    }
}
