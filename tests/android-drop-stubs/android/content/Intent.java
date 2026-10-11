// JVM-only data fixtures; not Android framework behavior and never packaged in the APK.
package android.content;
import android.net.Uri;
import android.os.Bundle;
public final class Intent {
 public static final String EXTRA_STREAM="android.intent.extra.STREAM",EXTRA_INTENT="android.intent.extra.INTENT";
 private Uri data;private ClipData clip;private final Bundle extras=new Bundle();
 public Uri getData() { return data; }
 public Intent setData(Uri value) { data=value;return this; }
 public ClipData getClipData() { return clip; }
 public Intent setClipData(ClipData value) { clip=value;return this; }
 public Bundle getExtras() { return extras; }
 public Intent putExtra(String key,Object value) { extras.put(key,value);return this; }
}
