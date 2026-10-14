package com.galaxybridge.lan;

import android.content.Context;
import org.json.*;

final class TransferHistory {
    static synchronized JSONArray read(Context c) {
        try { String raw=c.getSharedPreferences("transfers",Context.MODE_PRIVATE).getString("entries","[]");if(raw.length()>200000) return new JSONArray();return new JSONArray(raw); }
        catch(Exception e) { return new JSONArray(); }
    }
    static synchronized void add(Context c,String direction,String name,String status,String uri,String error) {
        try {
            JSONArray old=read(c),entries=new JSONArray();
            entries.put(new JSONObject().put("time",System.currentTimeMillis()).put("direction",direction).put("name",name).put("status",status).put("uri",uri).put("error",error));
            for(int i=0;i<old.length() && i<99;i++) entries.put(old.get(i));
            c.getSharedPreferences("transfers",Context.MODE_PRIVATE).edit().putString("entries",entries.toString()).apply();
        } catch(Exception ignored) { }
    }
    static synchronized void clear(Context c) { c.getSharedPreferences("transfers",Context.MODE_PRIVATE).edit().remove("entries").apply(); }
}
