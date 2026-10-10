package com.galaxybridge.lan;

import java.nio.charset.StandardCharsets;
import org.json.JSONObject;

final class ClipText {
    static final int MAX_BYTES=16000;
    static String validate(CharSequence value) {
        if(value==null || value.length()==0) throw new IllegalArgumentException("Нет текста для отправки.");
        if(value.length()>MAX_BYTES) throw new IllegalArgumentException("Текст ограничен 16 КБ.");
        String text=value.toString();
        if(text.indexOf('\0')>=0) throw new IllegalArgumentException("Текст содержит недопустимый нулевой символ.");
        if(text.getBytes(StandardCharsets.UTF_8).length>MAX_BYTES) throw new IllegalArgumentException("Текст ограничен 16 КБ.");
        return text;
    }
    static JSONObject request(String id,CharSequence value) {
        if(!Wire.hex(id,32)) throw new IllegalArgumentException("Invalid clipboard request id");
        JSONObject message=Wire.message("clipboard","id",id,"text",validate(value));
        if(message.toString().getBytes(StandardCharsets.UTF_8).length>Wire.MAX_FRAME)
            throw new IllegalArgumentException("Текст не помещается в сообщение. Отправьте его частями.");
        return message;
    }
}
