package com.galaxybridge.lan;

import android.app.*;
import android.content.*;
import android.net.*;
import android.provider.Settings;

final class Diagnostics {
    static String report(Context c) {
        StringBuilder text=new StringBuilder("Galaxy Bridge · проверка подключения\n\nСвязь: ").append(ConnectionService.connected()?"установлена":"не установлена");
        try {
            ConnectivityManager manager=c.getSystemService(ConnectivityManager.class);boolean wifi=false,vpn=false;
            for(Network network:manager.getAllNetworks()) { NetworkCapabilities caps=manager.getNetworkCapabilities(network);if(caps!=null) { wifi|=caps.hasTransport(NetworkCapabilities.TRANSPORT_WIFI);vpn|=caps.hasTransport(NetworkCapabilities.TRANSPORT_VPN); } }
            text.append("\nWi-Fi: ").append(wifi?"найдена сеть":"нет подключения");
            if(vpn) text.append("\nVPN включён: проверьте доступ к локальной сети в настройках VPN.");
        } catch(RuntimeException e) { text.append("\nСведения о сети недоступны."); }
        text.append("\n\nСпециальные возможности: ").append(ControlService.instance!=null?"включены":"выключены");
        text.append("\nПоверх приложений: ").append(Settings.canDrawOverlays(c)?"разрешено":"не разрешено");
        text.append("\n").append(ReceivedFiles.status(c));
        text.append("\n\n").append(ControlDisplays.description(c));
        if(!ConnectionService.connected()) text.append("\n\nЗапустите Galaxy Bridge на ПК. Подключитесь к сохранённому ПК или отсканируйте новый QR. Оба устройства должны находиться в одной локальной сети; гостевая сеть может запрещать связь между ними.");
        return text.toString();
    }
    static void show(Activity activity) {
        String report=report(activity);
        new AlertDialog.Builder(activity).setTitle("Диагностика").setMessage(report).setPositiveButton("Закрыть",null)
            .setNeutralButton("Скопировать",(d,w) -> activity.getSystemService(ClipboardManager.class).setPrimaryClip(ClipData.newPlainText("Galaxy Bridge",report))).show();
    }
}
