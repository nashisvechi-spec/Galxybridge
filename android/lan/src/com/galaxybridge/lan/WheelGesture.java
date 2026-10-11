package com.galaxybridge.lan;

// Android-independent geometry and accumulation for queued wheel gestures.
final class WheelGesture {
    final int width,height;
    final boolean zoom;
    float x,y;
    int steps;
    WheelGesture(int width,int height,float x,float y,int steps,boolean zoom) {
        this.width=width;this.height=height;this.x=x;this.y=y;this.steps=steps;this.zoom=zoom;
    }
    boolean merge(WheelGesture next) {
        if(zoom!=next.zoom || width!=next.width || height!=next.height) return false;
        steps=Math.max(-32,Math.min(32,steps+next.steps));x=next.x;y=next.y;return true;
    }
    float[] pinch() {
        float margin=Math.min(48,width*.1f),limit=(width-2*margin)/2;
        float scale=(float)Math.pow(1.18,Math.max(-4,Math.min(4,steps)));
        float start=Math.min(width*.12f,limit/Math.max(1,scale)),end=start*scale;
        float radius=Math.max(start,end),cx=Math.max(margin+radius,Math.min(width-margin-radius,x));
        float cy=Math.max(height*.15f,Math.min(height*.85f,y));
        return new float[] { cx,cy,start,end };
    }
    float[] swipe() {
        float top=height*.2f,bottom=height*.8f,start=Math.max(top,Math.min(bottom,y));
        // Leave room in the requested direction even when the cursor is at an edge.
        float travel=Math.min(height*.35f,Math.abs(steps)*100f);
        start=steps>0?Math.min(start,bottom-travel):Math.max(start,top+travel);
        float cx=Math.max(width*.05f,Math.min(width*.95f,x));
        return new float[] { cx,start,start+Math.signum(steps)*travel };
    }
}
