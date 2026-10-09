package com.galaxybridge.lan;
import java.io.*;
import java.nio.charset.StandardCharsets;
import org.json.JSONObject;

// JVM tests for the actual platform-independent companion parser; not an APK/device test.
public final class WireTest {
    private static int checks;
    private static void check(boolean value) { if(!value) throw new AssertionError("Check "+checks);checks++; }
    private interface Throwing { void run() throws Exception; }
    private static void reject(Throwing action) throws Exception { boolean failed=false;try { action.run(); } catch(Exception e){ failed=true; }check(failed); }
    public static void main(String[] args) throws Exception {
        String qr="galaxybridge://pair?v=1&host=192.168.0.23&port=38271&id="+repeat("a",32)+"&pin="+repeat("b",64)+"&ticket="+repeat("c",64)+"&name=HP%20%D0%9E%D0%BB%D0%B5%D0%B3";
        Pairing pair=new Pairing(qr);check(pair.name.equals("HP Олег"));check(pair.port==Wire.PORT);check(pair.host.equals("192.168.0.23"));
        reject(() -> new Pairing(qr+"&v=1"));reject(() -> new Pairing(qr+"&%76=1"));reject(() -> new Pairing(qr+"#x"));
        reject(() -> new Pairing(qr.replace("192.168.0.23","example.com")));reject(() -> new Pairing(qr.replace("192.168.0.23","127.0.0.1")));
        reject(() -> new Pairing(qr.replace("38271","5555")));reject(() -> new Pairing(qr.replace("&name=","&unknown=")));
        reject(() -> new Pairing(qr.replace("v=1","v=2")));reject(() -> new Pairing(qr.replace("galaxybridge://pair","http://pair")));
        reject(() -> new Pairing(qr.replace("galaxybridge://pair","galaxybridge://evil@pair")));reject(() -> new Pairing(qr+"%0A%0A"));
        check(Wire.name("Икона 🌍 (2).jpg"));for(String bad:new String[]{"", ".", "..", "../x", "x\\y", "x\ny\nz", "x\0y", repeat("Я",128)}) check(!Wire.name(bad));
        for(String bad:new String[]{"", "localhost", "127.0.0.1", "224.0.0.1", "256.1.1.1", "1.2.3", "1.2.3.4:5", "0.0.0.0"}) check(!Wire.ipv4(bad));
        check(Wire.hex(repeat("a",64),64));check(!Wire.hex(repeat("A",64),64));
        String text="Привет 🌍\n\"\\";ByteArrayOutputStream bytes=new ByteArrayOutputStream();Wire.write(new DataOutputStream(bytes),Wire.message("text","text",text));
        byte[] frame=bytes.toByteArray();check(new DataInputStream(new ByteArrayInputStream(frame)).readInt()==frame.length-4);
        InputStream fragmented=new FilterInputStream(new ByteArrayInputStream(frame)){ @Override public int read(byte[] b,int o,int n)throws IOException {return super.read(b,o,Math.min(n,1));} };
        check(Wire.read(new DataInputStream(fragmented)).getString("text").equals(text));
        for(int n:new int[]{-1,0,1,65537,Integer.MAX_VALUE}) { ByteArrayOutputStream h=new ByteArrayOutputStream();new DataOutputStream(h).writeInt(n);reject(() -> Wire.read(new DataInputStream(new ByteArrayInputStream(h.toByteArray())))); }
        reject(() -> Wire.read(new DataInputStream(new ByteArrayInputStream(java.util.Arrays.copyOf(frame,frame.length-1)))));
        reject(() -> Wire.write(new DataOutputStream(new ByteArrayOutputStream()),Wire.message("text","text",repeat("x",65537))));
        System.out.println("PASS: "+checks+" actual Java wire/pairing assertions (JVM only)");
    }
    private static String repeat(String value,int count){StringBuilder b=new StringBuilder();for(int i=0;i<count;i++)b.append(value);return b.toString();}
}
