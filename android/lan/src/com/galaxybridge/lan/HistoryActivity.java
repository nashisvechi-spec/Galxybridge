package com.galaxybridge.lan;

import android.app.*;
import android.content.*;
import android.net.Uri;
import android.os.Bundle;
import android.widget.*;
import java.util.*;
import org.json.*;

public final class HistoryActivity extends Activity {
    @Override public void onCreate(Bundle saved) { BridgeUi.prepare(this);super.onCreate(saved);show(); }
    private void show() {
        LinearLayout root=BridgeUi.root(this),page=BridgeUi.scrollBody(root);
        BridgeUi.header(page,"История передач","Последние 100 передач · ПК: "+getSharedPreferences("lan",MODE_PRIVATE).getString("name","не сопряжён"),this::finish);
        BridgeUi.button(page,"Обновить",false,this::show);
        JSONArray entries=TransferHistory.read(this);
        if(entries.length()==0) page.addView(BridgeUi.text(this,"Передачи пока не записаны.",16,false));
        for(int i=0;i<entries.length();i++) try {
            JSONObject entry=entries.getJSONObject(i);String name=entry.optString("name","Файл"),uri=entry.optString("uri","");
            LinearLayout card=BridgeUi.card(page,name);
            card.addView(BridgeUi.text(this,android.text.format.DateFormat.format("dd.MM.yyyy HH:mm",entry.optLong("time",0))+" · "+entry.optString("direction")+"\n"+entry.optString("status")+(entry.optString("error").isEmpty()?"":"\n"+entry.optString("error")),14,false));
            if(uri.startsWith("content://")) {
                if("Телефон → ПК".equals(entry.optString("direction"))) BridgeUi.button(card,"Повторить отправку",false,() -> {
                    ArrayList<Uri> files=new ArrayList<>();files.add(Uri.parse(uri));ShareActivity.submit(this,files);
                    Toast.makeText(this,"Если доступ к исходному файлу истёк, выберите его заново.",Toast.LENGTH_LONG).show();
                });
                else BridgeUi.button(card,"Открыть файл",false,() -> {
                    try { startActivity(new Intent(Intent.ACTION_VIEW).setDataAndType(Uri.parse(uri),getContentResolver().getType(Uri.parse(uri))).addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)); }
                    catch(RuntimeException e) { startActivity(new Intent(this,ReceivedFolderActivity.class)); }
                });
            }
        } catch(JSONException ignored) { }
        BridgeUi.button(page,"Выбрать файлы заново",false,() -> startActivity(new Intent(this,SendActivity.class)));
        BridgeUi.button(page,"Очистить историю",false,() -> new AlertDialog.Builder(this).setMessage("Очистить историю? Файлы сохранятся.").setNegativeButton("Отмена",null).setPositiveButton("Очистить",(d,w) -> { TransferHistory.clear(this);show(); }).show());
        BridgeUi.show(this,root);
    }
}
