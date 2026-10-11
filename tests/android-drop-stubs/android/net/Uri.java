// JVM-only data fixtures; not Android framework behavior and never packaged in the APK.
package android.net;
public final class Uri {
 private final String value;
 private Uri(String value) { this.value=value; }
 public static Uri parse(String value) { return new Uri(value); }
 public String getScheme() { int n=value.indexOf(':');return n<0?null:value.substring(0,n); }
 public String toString() { return value; }
}
