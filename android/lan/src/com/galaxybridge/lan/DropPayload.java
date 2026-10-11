package com.galaxybridge.lan;

import android.content.ClipData;
import android.content.ClipDescription;
import android.content.Intent;
import android.net.Uri;
import android.os.Bundle;
import java.io.IOException;
import java.util.ArrayList;
import java.util.List;

/** Extracts references; received Intents are data and are never launched. */
final class DropPayload {
    private final DropLinks links=new DropLinks();
    private final StringBuilder diagnostic=new StringBuilder();
    private int visited;
    private void visit(int depth) throws IOException {
        if(depth>4 || ++visited>512) throw new IOException("Данные сброса слишком сложные. Используйте «Поделиться».");
    }
    ArrayList<Uri> extract(ClipData clip) throws IOException {
        if(clip==null || clip.getItemCount()<1 || clip.getItemCount()>DropProbe.MAX_FILES) throw new IOException("Файловое приложение должно передать от 1 до 100 элементов.");
        note("Элементов: "+clip.getItemCount());
        ClipDescription description=clip.getDescription();
        if(description!=null) for(int i=0;i<description.getMimeTypeCount() && i<8;i++) note("MIME: "+clean(description.getMimeType(i)));
        for(int i=0;i<clip.getItemCount();i++) {
            note("Элемент "+(i+1)+":");
            if(!item(clip.getItemAt(i),0)) throw new IOException("В элементе "+(i+1)+" не найдена поддерживаемая ссылка на файл. Ниже показан формат данных.");
        }
        ArrayList<Uri> result=new ArrayList<>();for(String link:links.values()) result.add(Uri.parse(link));return result;
    }
    private boolean item(ClipData.Item item,int depth) throws IOException {
        visit(depth);if(item==null) return false;
        Uri uri=item.getUri();note("uri="+(uri==null?"нет":clean(uri.getScheme())));
        boolean matched=uri!=null && links.add(uri.toString());
        Intent intent=item.getIntent();note("intent="+(intent==null?"нет":"есть"));
        if(intent!=null && !matched) matched=intent(intent,depth+1);
        CharSequence text=item.getText();
        if(text!=null) {
            boolean reference=(matched?new DropLinks():links).text(text);matched=reference||matched;
            String type=reference?"content-ссылка":text.length()>8192?"слишком длинный":text.toString().startsWith("/")||text.toString().startsWith("file:")?"путь к файлу":"обычный текст";
            note("text="+type+" ("+text.length()+" символов)");
        } else note("text=нет");
        note("html="+(item.getHtmlText()==null?"нет":"есть"));return matched;
    }
    @SuppressWarnings("deprecation") // Diagnostic needs the actual mixed Bundle types, not a forced Parcelable cast.
    private boolean intent(Intent intent,int depth) throws IOException {
        visit(depth);boolean matched=false;Uri data=intent.getData();
        note("intent.data="+(data==null?"нет":clean(data.getScheme())));
        ClipData nested=intent.getClipData();
        if(nested!=null) matched=clip(nested,depth+1)||matched;
        Bundle extras=intent.getExtras();
        if(extras!=null) {
            int count=0;for(String key:extras.keySet()) {
                if(++count>16) { note("extras: остальные ключи скрыты");break; }
                Object value=extras.get(key);note("extra "+clean(key)+"="+(value==null?"null":clean(value.getClass().getSimpleName())));
            }
            Object stream=extras.get(Intent.EXTRA_STREAM);
            if(stream!=null) matched=stream(stream,depth+1)||matched;
            Object child=extras.get(Intent.EXTRA_INTENT);
            if(child instanceof Intent && !matched) matched=intent((Intent)child,depth+1);
        }
        if(!matched && data!=null) matched=links.add(data.toString());
        return matched;
    }
    private boolean clip(ClipData clip,int depth) throws IOException {
        visit(depth);if(clip.getItemCount()<1 || clip.getItemCount()>DropProbe.MAX_FILES) throw new IOException("Вложенная подборка должна содержать от 1 до 100 элементов.");
        note("intent.clipData="+clip.getItemCount());
        for(int i=0;i<clip.getItemCount();i++) if(!item(clip.getItemAt(i),depth+1)) throw new IOException("Вложенная подборка содержит элемент без доступной ссылки.");
        return true;
    }
    private boolean stream(Object value,int depth) throws IOException {
        visit(depth);
        if(value instanceof Uri) { note("stream.uri="+clean(((Uri)value).getScheme()));return links.add(value.toString()); }
        if(value instanceof CharSequence) {
            boolean matched=links.text((CharSequence)value);note("stream.text="+(matched?"content-ссылка":"не content-ссылка"));return matched;
        }
        if(value instanceof List<?>) {
            List<?> list=(List<?>)value;if(list.isEmpty() || list.size()>DropProbe.MAX_FILES) throw new IOException("EXTRA_STREAM должен содержать от 1 до 100 файлов.");
            for(Object entry:list) if(!stream(entry,depth+1)) throw new IOException("EXTRA_STREAM содержит элемент без поддерживаемой ссылки.");return true;
        }
        if(value instanceof Object[]) {
            Object[] list=(Object[])value;if(list.length<1 || list.length>DropProbe.MAX_FILES) throw new IOException("EXTRA_STREAM должен содержать от 1 до 100 файлов.");
            for(Object entry:list) if(!stream(entry,depth+1)) throw new IOException("EXTRA_STREAM содержит элемент без поддерживаемой ссылки.");return true;
        }
        return false;
    }
    String diagnostic() { return diagnostic.toString(); }
    private void note(String text) { if(diagnostic.length()<4000) diagnostic.append(text).append('\n'); }
    private static String clean(String value) {
        if(value==null) return "нет";StringBuilder result=new StringBuilder();
        for(int i=0;i<value.length() && i<100;i++) { char c=value.charAt(i);result.append(Character.isISOControl(c)?' ':c); }return result.toString();
    }
}
