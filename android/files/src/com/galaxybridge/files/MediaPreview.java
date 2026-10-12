package com.galaxybridge.files;

import android.content.Context;
import android.graphics.*;
import android.widget.ImageView;

/** A play badge keeps decoded video frames distinguishable from photos. */
final class MediaPreview extends ImageView {
    private final FileKind kind;
    private final Paint paint=new Paint(Paint.ANTI_ALIAS_FLAG);
    MediaPreview(Context context,FileKind kind) { super(context);this.kind=kind; }
    @Override protected void onDraw(Canvas canvas) {
        super.onDraw(canvas);if(kind!=FileKind.VIDEO) return;
        float unit=getResources().getDisplayMetrics().density,radius=8*unit,x=getWidth()-11*unit,y=getHeight()-11*unit;
        paint.setColor(0xbb000000);canvas.drawCircle(x,y,radius,paint);paint.setColor(Color.WHITE);
        Path triangle=new Path();triangle.moveTo(x-2*unit,y-4*unit);triangle.lineTo(x+4*unit,y);triangle.lineTo(x-2*unit,y+4*unit);triangle.close();canvas.drawPath(triangle,paint);
    }
}
