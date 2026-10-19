package com.galaxybridge.lan;

import android.app.Activity;
import android.content.Context;
import android.content.res.ColorStateList;
import android.content.res.Configuration;
import android.graphics.*;
import android.graphics.drawable.*;
import android.view.*;
import android.widget.*;

/** Shared native components for the quiet, minimal Galaxy Bridge interface. */
final class BridgeUi {
    static final int BLUE=0xff356bd9;
    static final class Palette {
        final boolean dark;
        final int background,surface,text,muted,border,link,success,selected;
        Palette(boolean dark) {
            this.dark=dark;background=dark?0xff15181e:0xfff5f6f8;surface=dark?0xff1d2129:0xffffffff;
            text=dark?0xffeef0f4:0xff202630;muted=dark?0xffa2aab8:0xff636d7d;
            border=dark?0xff303641:0xffe4e7ec;link=dark?0xff8fb2ff:BLUE;
            success=dark?0xff7bcea6:0xff247b52;selected=dark?0xff28344b:0xffebf1fd;
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
    static void updateText(TextView view,String value) {
        if(view!=null && !value.contentEquals(view.getText())) view.setText(value);
    }
    static LinearLayout card(LinearLayout parent,String heading) {
        Context c=parent.getContext();Palette p=palette(c);LinearLayout card=column(c);int pad=dp(c,18);card.setPadding(pad,pad,pad,pad);
        card.setBackground(shape(c,p.surface,0,12));LinearLayout.LayoutParams lp=new LinearLayout.LayoutParams(-1,-2);lp.bottomMargin=dp(c,12);parent.addView(card,lp);
        if(heading!=null) { TextView title=text(c,heading,16,true);title.setAccessibilityHeading(true);card.addView(title); }return card;
    }
    static Button button(LinearLayout parent,String label,boolean primary,Runnable action) {
        Context c=parent.getContext();Palette p=palette(c);Button b=new Button(c);b.setText(label);b.setAllCaps(false);b.setTextSize(16);
        b.setTypeface(Typeface.create("sans-serif-medium",Typeface.NORMAL));b.setMinHeight(dp(c,48));b.setMinimumHeight(dp(c,48));
        b.setPadding(dp(c,16),dp(c,10),dp(c,16),dp(c,10));b.setBackgroundTintList(null);b.setStateListAnimator(null);
        StateListDrawable background=new StateListDrawable();background.addState(new int[]{-android.R.attr.state_enabled},shape(c,p.background,0,10));
        background.addState(new int[]{},shape(c,primary?BLUE:p.surface,primary?0:p.border,10));
        b.setBackground(new RippleDrawable(ColorStateList.valueOf(p.selected),background,null));
        b.setTextColor(new ColorStateList(new int[][]{new int[]{-android.R.attr.state_enabled},new int[]{}},new int[]{p.muted,primary?Color.WHITE:p.text}));
        b.setOnClickListener(v -> action.run());LinearLayout.LayoutParams lp=new LinearLayout.LayoutParams(-1,-2);lp.topMargin=dp(c,8);parent.addView(b,lp);return b;
    }
    static void navigation(Button button,boolean selected) {
        Context c=button.getContext();Palette p=palette(c);button.setSelected(selected);button.setStateDescription(selected?"Выбрано":"");
        button.setTextColor(selected?p.link:p.muted);
        button.setBackground(new RippleDrawable(ColorStateList.valueOf(p.selected),shape(c,selected?p.selected:p.surface,0,10),null));
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
            TextView brand=text(c,"Galaxy Bridge",16,true);brand.setPadding(dp(c,10),0,0,0);row.addView(brand);body.addView(row);
        }
        TextView heading=text(c,title,28,true);heading.setAccessibilityHeading(true);heading.setPadding(0,dp(c,back==null?8:16),0,dp(c,20));body.addView(heading);
        if(subtitle!=null) body.addView(text(c,subtitle,14,false));
    }
    static void show(Activity activity,LinearLayout root) { activity.setContentView(root);root.requestApplyInsets(); }
}
