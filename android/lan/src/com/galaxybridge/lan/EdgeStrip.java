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
    boolean contains(float px,float py) {
        return !Float.isNaN(px) && !Float.isNaN(py) && px>=x && py>=y && px<x+width && py<y+height;
    }
}
