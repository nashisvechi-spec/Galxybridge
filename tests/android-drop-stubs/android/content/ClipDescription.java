// JVM-only data fixtures; not Android framework behavior and never packaged in the APK.
package android.content;
public final class ClipDescription {
 private final String[] types;
 public ClipDescription(String[] types) { this.types=types; }
 public int getMimeTypeCount() { return types.length; }
 public String getMimeType(int index) { return types[index]; }
}
