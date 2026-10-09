package com.galaxybridge.lan;

import org.json.JSONObject;
import org.json.JSONException;
import java.io.DataInputStream;
import java.io.DataOutputStream;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;

final class Wire {
    static final int PORT = 38271, DISCOVERY = 38272, MAX_FRAME = 65536, CHUNK = 32768;
    static final long MAX_FILE = 2L * 1024 * 1024 * 1024;
    static JSONObject read(DataInputStream in) throws Exception {
        int length = in.readInt();
        if (length < 2 || length > MAX_FRAME) throw new IOException("Invalid frame");
        byte[] data = new byte[length]; in.readFully(data);
        return new JSONObject(new String(data, StandardCharsets.UTF_8));
    }
    static void write(DataOutputStream out, JSONObject message) throws IOException {
        byte[] data = message.toString().getBytes(StandardCharsets.UTF_8);
        if (data.length < 2 || data.length > MAX_FRAME) throw new IOException("Invalid frame");
        out.writeInt(data.length); out.write(data); out.flush();
    }
    static JSONObject message(String type, Object... pairs) {
        JSONObject out = new JSONObject();
        try { out.put("type", type); for (int i = 0; i < pairs.length; i += 2) out.put((String)pairs[i], pairs[i + 1]); }
        catch (JSONException impossible) { throw new IllegalArgumentException(impossible); }
        return out;
    }
    static String hex(byte[] data) { StringBuilder text = new StringBuilder(); for (byte b : data) text.append(String.format(java.util.Locale.ROOT, "%02x", b & 255)); return text.toString(); }
    static boolean hex(String s, int length) { return s != null && s.length() == length && s.matches("[0-9a-f]+" ); }
    static boolean ipv4(String value) {
        if (value == null || !value.matches("[0-9]{1,3}(\\.[0-9]{1,3}){3}")) return false;
        String[] parts = value.split("\\.");
        for (String part : parts) if (Integer.parseInt(part) > 255) return false;
        int first = Integer.parseInt(parts[0]); return first > 0 && first < 224 && first != 127;
    }
    static boolean name(String name) {
        if(name.length()==0 || name.equals(".") || name.equals("..") || name.getBytes(StandardCharsets.UTF_8).length>255) return false;
        for(int i=0;i<name.length();i++) if(name.charAt(i)=='/' || name.charAt(i)=='\\' || Character.isISOControl(name.charAt(i))) return false;
        return true;
    }
}
