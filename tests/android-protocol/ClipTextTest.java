package com.galaxybridge.lan;
import org.json.JSONObject;
import java.nio.charset.StandardCharsets;

public final class ClipTextTest {
    private static int checks;
    private static void check(boolean value) { if(!value) throw new AssertionError();checks++; }
    private static void reject(Runnable action) { try { action.run();throw new AssertionError("expected rejection"); } catch(IllegalArgumentException expected) { checks++; } }
    public static void main(String[] args) throws Exception {
        String id="0123456789abcdef0123456789abcdef",value="Привет 🌍\nhttps://example.com/path?q=икона&n=2";
        JSONObject request=ClipText.request(id,value);
        check("clipboard".equals(request.getString("type")));
        check(id.equals(request.getString("id")));
        check(value.equals(new JSONObject(request.toString()).getString("text")));
        check(ClipText.validate("  text\n").equals("  text\n"));
        check(ClipText.validate(new String(new char[16000]).replace('\0','a')).length()==16000);
        check(ClipText.validate(new String(new char[8000]).replace('\0','Я')).getBytes(StandardCharsets.UTF_8).length==16000);
        reject(() -> ClipText.validate(null));reject(() -> ClipText.validate(""));
        reject(() -> ClipText.validate("a\0b"));
        reject(() -> ClipText.validate(new String(new char[16001]).replace('\0','a')));
        reject(() -> ClipText.validate(new String(new char[8001]).replace('\0','Я')));
        reject(() -> ClipText.request("invalid",value));
        reject(() -> ClipText.request(id,new String(new char[16000]).replace('\0','\1')));
        System.out.println("PASS: "+checks+" clipboard text assertions");
    }
}
