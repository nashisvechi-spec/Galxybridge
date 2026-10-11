package com.galaxybridge.lan;

/** Physical-pixel hover strip on the phone edge facing the desktop. */
final class EdgeStrip {
    final int x,y,width,height;
    private EdgeStrip(int x,int y,int width,int height) {
        this.x=x;this.y=y;this.width=width;this.height=height;
    }
    static EdgeStrip create(int width,int height,int left,int top,int right,int bottom,int side) {
        if(width<16 || height<16 || width>20000 || height>20000 ||
            left<0 || top<0 || right<0 || bottom<0 || left>=width || right>=width-left ||
            top>=height || bottom>=height-top) throw new IllegalArgumentException("Invalid screen insets");
        switch(side) {
            case 0:return new EdgeStrip(left,top,1,height-top-bottom);
            case 1:return new EdgeStrip(width-right-1,top,1,height-top-bottom);
            case 2:return new EdgeStrip(left,height-bottom-1,width-left-right,1);
            case 3:return new EdgeStrip(left,top,width-left-right,1);
            default:throw new IllegalArgumentException("Invalid phone side");
        }
    }
    /** Visible target, inset from the edge; ordinary pointer strips stay one pixel. */
    static EdgeStrip dropZone(int width,int height,int left,int top,int right,int bottom,int side,float density) {
        EdgeStrip edge=create(width,height,left,top,right,bottom,side);
        if(Float.isNaN(density) || density<=0 || density>10) throw new IllegalArgumentException("Invalid density");
        int inset=side==0?left:side==1?right:side==2?bottom:top;
        int available=(side<2?width:height)/4-inset;
        int gap=Math.min(Math.max(1,Math.round(16*density)),available/4);
        int size=Math.min(Math.round(64*density),available-gap);
        if(gap<1 || size<8) throw new IllegalArgumentException("No room for drop zone");
        switch(side) {
            case 0:return new EdgeStrip(edge.x+gap,edge.y,size,edge.height);
            case 1:return new EdgeStrip(edge.x-gap-size+1,edge.y,size,edge.height);
            case 2:return new EdgeStrip(edge.x,edge.y-gap-size+1,edge.width,size);
            default:return new EdgeStrip(edge.x,edge.y+gap,edge.width,size);
        }
    }
    boolean contains(float px,float py) {
        return !Float.isNaN(px) && !Float.isNaN(py) && px>=x && py>=y && px<x+width && py<y+height;
    }
}
