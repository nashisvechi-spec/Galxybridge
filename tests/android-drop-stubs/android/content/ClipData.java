// JVM-only data fixtures; not Android framework behavior and never packaged in the APK.
package android.content;
import android.net.Uri;
import java.util.ArrayList;
public final class ClipData {
 private final ArrayList<Item> items=new ArrayList<>();private final ClipDescription description;
 public ClipData(String label,String[] types,Item first) { description=new ClipDescription(types);items.add(first); }
 public void addItem(Item item) { items.add(item); }
 public int getItemCount() { return items.size(); }
 public Item getItemAt(int index) { return items.get(index); }
 public ClipDescription getDescription() { return description; }
 public static final class Item {
  private final Uri uri;private final Intent intent;private final CharSequence text;
  public Item(Uri uri) { this(null,null,uri); }
  public Item(Intent intent) { this(null,intent,null); }
  public Item(CharSequence text) { this(text,null,null); }
  public Item(CharSequence text,Intent intent,Uri uri) { this.text=text;this.intent=intent;this.uri=uri; }
  public Uri getUri() { return uri; }
  public Intent getIntent() { return intent; }
  public CharSequence getText() { return text; }
  public String getHtmlText() { return null; }
 }
}
