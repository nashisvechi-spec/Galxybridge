package com.galaxybridge.lan;

import android.app.Activity;
import android.content.Context;
import android.content.res.ColorStateList;
import android.content.res.Configuration;
import android.graphics.*;
import android.graphics.drawable.*;
import android.view.*;
import android.widget.*;

/** Shared native components from the Galaxy Bridge design concept. */
final class BridgeUi {
    static final int BLUE=0xff0963d3;
    static final class Palette {
        final boolean dark;
        final int background,surface,text,muted,border,link,success,selected;
        Palette(boolean dark) {
            this.dark=dark;background=dark?0xff0d1422:0xfff4f7fc;surface=dark?0xff131e30:0xffffffff;
            text=dark?0xffebf2ff:0xff19263e;muted=dark?0xffa6b8d2:0xff53698a;
            border=dark?0xff2b3d56:0xffd8e3f3;link=dark?0xff49dbdf:0xff00707c;
            success=dark?0xff75d5ad:0xff00734e;selected=dark?0xff213852:0xffe2efff;
        }
    }
    static Palette palette(Context c) {
        String mode=c.getSharedPreferences("appearance",Context.MODE_PRIVATE).getString("theme","system");
        boolean night=(c.getResources().getConfiguration().uiMode&Configuration.UI_MODE_NIGHT_MASK)==Configuration.UI_MODE_NIGHT_YES;
        return new Palette("dark".equals(mode) || "system".equals(mode) && night);
    }
    static void prepare(Activity activity) {
        Palette p=palette(activity);activity.setTheme(p.dark?android.R.style.Theme_Material_NoActionBar:android.R.style.Theme_Material_Light_NoActionBar);
        activity.getWindow().setDecorFitsSystemWindows(false);
        activity.getWindow().setStatusBarColor(p.background);activity.getWindow().setNavigationBarColor(p.background);
        activity.getWindow().getDecorView().setSystemUiVisibility(p.dark?0:View.SYSTEM_UI_FLAG_LIGHT_STATUS_BAR|View.SYSTEM_UI_FLAG_LIGHT_NAVIGATION_BAR);
    }
    static int dp(Context c,float value) { return Math.round(value*c.getResources().getDisplayMetrics().density); }
    static GradientDrawable shape(Context c,int fill,int border,int radius) {
        GradientDrawable drawable=new GradientDrawable();drawable.setColor(fill);drawable.setCornerRadius(dp(c,radius));
        if(border!=0) drawable.setStroke(dp(c,1),border);return drawable;
    }
    static LinearLayout column(Context c) { LinearLayout v=new LinearLayout(c);v.setOrientation(LinearLayout.VERTICAL);return v; }
    static TextView text(Context c,String value,int size,boolean bold) {
        TextView v=new TextView(c);v.setText(value);v.setTextSize(size);v.setTextColor(bold?palette(c).text:palette(c).muted);
        v.setTypeface(Typeface.create(bold?"sans-serif-medium":"sans-serif",Typeface.NORMAL));v.setLineSpacing(dp(c,2),1);
        v.setPadding(0,0,0,dp(c,8));return v;
    }
    static LinearLayout card(LinearLayout parent,String heading) {
        Context c=parent.getContext();Palette p=palette(c);LinearLayout card=column(c);int pad=dp(c,18);card.setPadding(pad,pad,pad,pad);
        card.setBackground(shape(c,p.surface,p.border,18));LinearLayout.LayoutParams lp=new LinearLayout.LayoutParams(-1,-2);lp.bottomMargin=dp(c,16);parent.addView(card,lp);
        if(heading!=null) card.addView(text(c,heading,18,true));return card;
    }
    static Button button(LinearLayout parent,String label,boolean primary,Runnable action) {
        Context c=parent.getContext();Palette p=palette(c);Button b=new Button(c);b.setText(label);b.setAllCaps(false);b.setTextSize(16);
        b.setTypeface(Typeface.create("sans-serif-medium",Typeface.NORMAL));b.setMinHeight(dp(c,52));b.setMinimumHeight(dp(c,52));b.setMaxLines(3);
        b.setPadding(dp(c,16),dp(c,12),dp(c,16),dp(c,12));b.setBackgroundTintList(null);
        StateListDrawable background=new StateListDrawable();background.addState(new int[]{-android.R.attr.state_enabled},shape(c,p.selected,p.border,12));
        background.addState(new int[]{},shape(c,primary?BLUE:p.surface,primary?0:p.border,12));
        b.setBackground(new RippleDrawable(ColorStateList.valueOf(p.selected),background,null));
        b.setTextColor(new ColorStateList(new int[][]{new int[]{-android.R.attr.state_enabled},new int[]{}},new int[]{p.muted,primary?Color.WHITE:p.text}));
        b.setOnClickListener(v -> action.run());LinearLayout.LayoutParams lp=new LinearLayout.LayoutParams(-1,-2);lp.topMargin=dp(c,8);parent.addView(b,lp);return b;
    }
    static LinearLayout root(Activity activity) {
        LinearLayout root=column(activity);root.setBackgroundColor(palette(activity).background);
        root.setOnApplyWindowInsetsListener((v,insets) -> {
            Insets bars=insets.getInsets(WindowInsets.Type.systemBars()|WindowInsets.Type.displayCutout());
            Insets ime=insets.getInsets(WindowInsets.Type.ime());v.setPadding(bars.left,bars.top,bars.right,Math.max(bars.bottom,ime.bottom));return insets;
        });return root;
    }
    static LinearLayout scrollBody(LinearLayout root) {
        Context c=root.getContext();ScrollView scroll=new ScrollView(c);scroll.setFillViewport(true);
        LinearLayout body=column(c);int p=dp(c,20);body.setPadding(p,p,p,p);scroll.addView(body);
        root.addView(scroll,new LinearLayout.LayoutParams(-1,0,1));return body;
    }
    static void header(LinearLayout body,String title,String subtitle,Runnable back) {
        Context c=body.getContext();LinearLayout row=new LinearLayout(c);row.setGravity(Gravity.CENTER_VERTICAL);
        if(back!=null) {
            Button b=new Button(c);b.setText("‹");b.setTextSize(28);b.setTextColor(palette(c).text);b.setBackgroundColor(Color.TRANSPARENT);
            b.setContentDescription("Назад");b.setOnClickListener(v -> back.run());row.addView(b,new LinearLayout.LayoutParams(dp(c,48),dp(c,48)));
        } else {
            ImageView icon=new ImageView(c);int id=c.getResources().getIdentifier("ic_launcher","mipmap",c.getPackageName());
            if(id!=0) icon.setImageResource(id);row.addView(icon,new LinearLayout.LayoutParams(dp(c,28),dp(c,28)));
        }
        TextView brand=text(c,"Galaxy Bridge",16,true);brand.setPadding(dp(c,10),0,0,0);row.addView(brand);body.addView(row);
        TextView heading=text(c,title,28,true);heading.setPadding(0,dp(c,20),0,dp(c,8));body.addView(heading);
        if(subtitle!=null) body.addView(text(c,subtitle,14,false));
    }
    static void show(Activity activity,LinearLayout root) { activity.setContentView(root);root.requestApplyInsets(); }
    static final class Devices extends View {
        private final Paint paint=new Paint(Paint.ANTI_ALIAS_FLAG);
        boolean connected;
        Devices(Context c) { super(c);setContentDescription("Компьютер и телефон, связь по Wi-Fi"); }
        @Override protected void onDraw(Canvas canvas) {
            super.onDraw(canvas);Palette p=palette(getContext());float w=getWidth(),h=getHeight(),unit=Math.min(w/340f,h/146f);
            paint.setColor(p.background);canvas.drawRoundRect(0,0,w,h,dp(getContext(),12),dp(getContext(),12),paint);
            paint.setColor(connected?p.link:p.muted);paint.setStrokeWidth(2*unit);canvas.drawLine(w*.34f,h*.47f,w*.8f,h*.47f,paint);
            device(canvas,new RectF(w*.1f,h*.19f,w*.56f,h*.7f),"ПК",false,p,unit);
            device(canvas,new RectF(w*.74f,h*.12f,w*.91f,h*.77f),"GB",true,p,unit);
            paint.setColor(connected?p.link:p.muted);paint.setTextSize(12*unit);paint.setTypeface(Typeface.DEFAULT);paint.setTextAlign(Paint.Align.CENTER);
            canvas.drawText(connected?"Связь установлена":"Ожидаем подключение",w/2,h*.91f,paint);
        }
        private void device(Canvas canvas,RectF box,String text,boolean phone,Palette p,float unit) {
            paint.setColor(p.surface);paint.setStyle(Paint.Style.FILL);canvas.drawRoundRect(box,8*unit,8*unit,paint);
            paint.setStyle(Paint.Style.STROKE);paint.setStrokeWidth(2*unit);paint.setColor(p.muted);canvas.drawRoundRect(box,8*unit,8*unit,paint);
            paint.setStyle(Paint.Style.FILL);RectF screen=new RectF(box);screen.inset(6*unit,8*unit);
            paint.setShader(new LinearGradient(screen.left,screen.top,screen.right,screen.bottom,0xff0d4079,0xff007a97,Shader.TileMode.CLAMP));canvas.drawRoundRect(screen,3*unit,3*unit,paint);paint.setShader(null);
            paint.setColor(0xff82edf0);paint.setTextSize((phone?14:20)*unit);paint.setTypeface(Typeface.create("sans-serif-medium",0));paint.setTextAlign(Paint.Align.CENTER);
            canvas.drawText(text,screen.centerX(),screen.centerY()+6*unit,paint);
            if(!phone) { paint.setStrokeWidth(3*unit);paint.setColor(p.muted);canvas.drawLine(box.left-8*unit,box.bottom+7*unit,box.right+8*unit,box.bottom+7*unit,paint); }
        }
    }
}
