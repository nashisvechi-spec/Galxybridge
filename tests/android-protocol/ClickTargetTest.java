package com.galaxybridge.lan;

public final class ClickTargetTest {
    private static int checks;
    private static void check(boolean value,String reason) {
        if(!value) throw new AssertionError(reason);checks++;
    }
    private static final class Node implements ClickTarget.Node {
        Node[] children=new Node[0];
        boolean available=true,clickable,accepted=true,fail;
        int clicks,releases;
        float left=0,top=0,right=100,bottom=100;
        Node(boolean clickable,Node... children) { this.clickable=clickable;this.children=children; }
        public boolean contains(float x,float y) { return x>=left && x<right && y>=top && y<bottom; }
        public boolean available() { return available; }
        public boolean clickable() { return clickable; }
        public int childCount() { return children.length; }
        public ClickTarget.Node child(int index) { return children[index]; }
        public boolean click() { clicks++;if(fail) throw new IllegalStateException();return accepted; }
        public void release() { releases++; }
    }
    public static void main(String[] args) {
        check(!ClickTarget.click(null,50,50),"missing active window uses touch fallback");
        Node label=new Node(false),row=new Node(true,label),root=new Node(true,row);
        check(ClickTarget.click(root,50,50),"non-clickable file label selects its actionable row");
        check(row.clicks==1 && root.clicks==0 && label.clicks==0,"exactly the deepest actionable ancestor is clicked");
        check(label.releases==1 && row.releases==1 && root.releases==0,"children released, root owned by caller");
        Node leaf=new Node(true),parent=new Node(true,leaf);
        check(ClickTarget.click(parent,50,50) && leaf.clicks==1 && parent.clicks==0,"nested actionable element takes precedence");
        Node rejected=new Node(true);rejected.accepted=false;parent=new Node(true,rejected);
        check(!ClickTarget.click(parent,50,50),"rejected node action uses touch fallback");
        check(rejected.clicks==1 && parent.clicks==0,"rejection cannot click parent too");
        Node behind=new Node(true),front=new Node(true);root=new Node(false,behind,front);
        check(ClickTarget.click(root,50,50) && front.clicks==1 && behind.clicks==0,"later overlapping sibling has precedence");
        Node elsewhere=new Node(true);elsewhere.left=60;root=new Node(false,row,elsewhere);
        check(ClickTarget.click(root,50,50) && elsewhere.clicks==0,"screen-coordinate hit testing ignores nearby rows");
        check(!ClickTarget.click(root,100,50),"right edge is outside");
        check(!ClickTarget.click(root,50,100),"bottom edge is outside");
        check(!ClickTarget.click(root,-1,50),"left outside");
        Node hidden=new Node(true,new Node(true));hidden.available=false;
        check(!ClickTarget.click(hidden,50,50) && hidden.children[0].clicks==0,"unavailable subtree is ignored");
        check(!ClickTarget.click(new Node(false),50,50),"gesture-only surface uses touch fallback");
        Node sparse=new Node(true,(Node)null);
        check(ClickTarget.click(sparse,50,50),"missing child does not break row activation");
        Node deep=new Node(true);for(int i=0;i<34;i++) deep=new Node(true,deep);
        check(!ClickTarget.click(deep,50,50) && deep.clicks==0,"depth limit falls back rather than clicking wrong ancestor");
        Node wide=new Node(true,new Node[129]);
        check(!ClickTarget.click(wide,50,50) && wide.clicks==0,"child limit falls back");
        Node[] branches=new Node[128];
        for(int i=0;i<branches.length;i++) branches[i]=new Node(false,new Node(false),new Node(false),new Node(false),new Node(false));
        Node large=new Node(true,branches);
        check(!ClickTarget.click(large,50,50) && large.clicks==0,"visit budget bounds work per remote click");
        Node throwing=new Node(true);throwing.fail=true;root=new Node(false,throwing);
        try { ClickTarget.click(root,50,50);throw new AssertionError("expected action failure"); }
        catch(IllegalStateException expected) { check(throwing.releases==1,"failed action still releases child"); }
        check(ClickTarget.click(new Node(true),50,50),"new click has its own traversal budget");
        System.out.println("PASS: "+checks+" click target assertions");
    }
}
