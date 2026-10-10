package com.galaxybridge.lan;

/** Hit testing uses only geometry and actions, never labels or field contents. */
final class ClickTarget {
    interface Node {
        boolean contains(float x,float y);
        boolean available();
        boolean clickable();
        int childCount();
        Node child(int index);
        boolean click();
        void release();
    }
    private static final int MISS=0, CLICKED=1, REJECTED=2;
    private int remaining=512;
    static boolean click(Node root,float x,float y) {
        // The caller owns the root; traversal owns every acquired child.
        return root!=null && new ClickTarget().visit(root,x,y,0)==CLICKED;
    }
    private int visit(Node node,float x,float y,int depth) {
        if(--remaining<0 || depth>32) return REJECTED;
        if(!node.available() || !node.contains(x,y)) return MISS;
        int count=node.childCount();
        if(count>128) return REJECTED;
        // Later siblings normally draw over earlier ones.
        for(int i=count-1;i>=0;i--) {
            Node child=node.child(i);
            if(child==null) continue;
            int result;
            try { result=visit(child,x,y,depth+1); }
            finally { child.release(); }
            if(result!=MISS) return result;
        }
        // Never try a parent after a child rejects the action: one click per command.
        return node.clickable() ? (node.click()?CLICKED:REJECTED) : MISS;
    }
}
