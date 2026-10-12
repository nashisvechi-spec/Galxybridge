package com.galaxybridge.files;

import android.graphics.*;
import android.graphics.drawable.Drawable;

/** Original vector artwork; scales to list, grid and home category sizes. */
final class FileArtwork extends Drawable {
    private final FileKind kind;
    private final boolean category;
    private final Paint paint=new Paint(Paint.ANTI_ALIAS_FLAG);
    FileArtwork(FileKind kind,boolean category) { this.kind=kind;this.category=category; }
    @Override public void draw(Canvas canvas) {
        Rect b=getBounds();canvas.save();canvas.translate(b.left,b.top);canvas.scale(b.width()/64f,b.height()/64f);
        if(category) { paint.setStyle(Paint.Style.FILL);paint.setColor(0xfff3f4f5);canvas.drawCircle(32,32,30,paint); }
        paint.setColor(kind.color);paint.setStyle(Paint.Style.STROKE);paint.setStrokeWidth(2.2f);paint.setStrokeCap(Paint.Cap.ROUND);paint.setStrokeJoin(Paint.Join.ROUND);
        if(kind==FileKind.FOLDER) {
            paint.setStyle(Paint.Style.FILL);Path folder=new Path();folder.moveTo(7,18);folder.lineTo(27,18);folder.lineTo(33,24);folder.lineTo(57,24);folder.lineTo(57,51);folder.lineTo(7,51);folder.close();canvas.drawPath(folder,paint);
            paint.setColor(0xffffce64);canvas.drawRoundRect(7,26,57,52,5,5,paint);
        } else if(kind==FileKind.IMAGE) {
            canvas.drawRoundRect(17,18,47,46,5,5,paint);canvas.drawCircle(26,26,3,paint);
            Path mountains=new Path();mountains.moveTo(20,42);mountains.lineTo(28,33);mountains.lineTo(34,39);mountains.lineTo(40,30);mountains.lineTo(46,39);canvas.drawPath(mountains,paint);
        } else if(kind==FileKind.VIDEO) {
            canvas.drawRoundRect(18,16,46,48,5,5,paint);Path play=new Path();play.moveTo(28,25);play.lineTo(39,32);play.lineTo(28,39);play.close();canvas.drawPath(play,paint);
        } else if(kind==FileKind.AUDIO) {
            canvas.drawLine(29,22,29,42,paint);canvas.drawLine(29,22,43,18,paint);canvas.drawLine(43,18,43,37,paint);
            paint.setStyle(Paint.Style.FILL);canvas.drawOval(20,38,30,46,paint);canvas.drawOval(34,34,44,42,paint);
        } else if(kind==FileKind.DOWNLOAD) {
            canvas.drawLine(32,18,32,39,paint);canvas.drawLine(24,32,32,40,paint);canvas.drawLine(32,40,40,32,paint);canvas.drawLine(20,47,44,47,paint);
        } else if(kind==FileKind.APK && category) {
            paint.setStyle(Paint.Style.FILL);paint.setTypeface(Typeface.create("sans-serif-medium",Typeface.NORMAL));paint.setTextAlign(Paint.Align.CENTER);paint.setTextSize(14);canvas.drawText("APK",32,37,paint);
        } else {
            Path sheet=new Path();sheet.moveTo(17,12);sheet.lineTo(37,12);sheet.lineTo(48,23);sheet.lineTo(48,51);sheet.lineTo(17,51);sheet.close();
            if(!category) { paint.setStyle(Paint.Style.FILL);paint.setColor((kind.color&0x00ffffff)|0x18000000);canvas.drawPath(sheet,paint);paint.setColor(kind.color);paint.setStyle(Paint.Style.STROKE); }
            canvas.drawPath(sheet,paint);canvas.drawLine(37,13,37,24,paint);canvas.drawLine(37,24,47,24,paint);
            if(!kind.mark.isEmpty()) { paint.setStyle(Paint.Style.FILL);paint.setTextAlign(Paint.Align.CENTER);paint.setTypeface(Typeface.create("sans-serif-medium",Typeface.NORMAL));paint.setTextSize(10);canvas.drawText(kind.mark,32,40,paint); }
            else { canvas.drawLine(24,33,40,33,paint);canvas.drawLine(24,40,37,40,paint); }
        }
        canvas.restore();
    }
    @Override public void setAlpha(int alpha) { paint.setAlpha(alpha);invalidateSelf(); }
    @Override public void setColorFilter(ColorFilter filter) { paint.setColorFilter(filter);invalidateSelf(); }
    @Override public int getOpacity() { return PixelFormat.TRANSLUCENT; }
}
