// JVM-only data fixtures; not Android framework behavior and never packaged in the APK.
package android.os;
import java.util.LinkedHashMap;
import java.util.Set;
public final class Bundle {
 private final LinkedHashMap<String,Object> values=new LinkedHashMap<>();
 @Deprecated public Object get(String key) { return values.get(key); }
 public void put(String key,Object value) { values.put(key,value); }
 public Set<String> keySet() { return values.keySet(); }
}
