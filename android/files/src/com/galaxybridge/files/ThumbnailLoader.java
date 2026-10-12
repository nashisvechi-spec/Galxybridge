package com.galaxybridge.files;

import android.graphics.Bitmap;
import android.media.ThumbnailUtils;
import android.os.*;
import android.util.*;
import android.widget.ImageView;
import java.io.*;
import java.lang.ref.WeakReference;
import java.util.*;
import java.util.concurrent.*;

/** Decode only visible media; bounded queue/cache and request tags prevent stale images. */
final class ThumbnailLoader {
    private final Handler main=new Handler(Looper.getMainLooper());
    private final LruCache<String,Bitmap> cache=new LruCache<String,Bitmap>(12*1024*1024) {
        @Override protected int sizeOf(String key,Bitmap bitmap) { return bitmap.getAllocationByteCount(); }
    };
    private final ThreadPoolExecutor workers=new ThreadPoolExecutor(2,2,30,TimeUnit.SECONDS,new ArrayBlockingQueue<>(48));
    private final Set<CancellationSignal> active=Collections.synchronizedSet(new HashSet<>());
    private volatile boolean closed;
    void bind(ImageView view,File file,int pixels) {
        FileKind kind=FileKind.of(file);view.setImageDrawable(new FileArtwork(kind,false));
        int size=Math.max(48,Math.min(256,pixels));String key=file.getPath()+"|"+file.lastModified()+"|"+file.length()+"|"+size;view.setTag(key);
        if(closed || (kind!=FileKind.IMAGE && kind!=FileKind.VIDEO)) return;
        Bitmap cached=cache.get(key);if(cached!=null) { view.setImageBitmap(cached);return; }
        WeakReference<ImageView> target=new WeakReference<>(view);CancellationSignal signal=new CancellationSignal();active.add(signal);
        try { workers.execute(() -> {
            Bitmap decoded=null;
            try { if(!closed) decoded=kind==FileKind.IMAGE?ThumbnailUtils.createImageThumbnail(file,new Size(size,size),signal):ThumbnailUtils.createVideoThumbnail(file,new Size(size,size),signal); }
            catch(IOException | RuntimeException ignored) { }
            finally { active.remove(signal); }
            final Bitmap result=decoded;
            if(result!=null && !closed) { cache.put(key,result);main.post(() -> { ImageView image=target.get();if(!closed && image!=null && key.equals(image.getTag())) image.setImageBitmap(result); }); }
        }); } catch(RejectedExecutionException full) { active.remove(signal); }
    }
    void close() { closed=true;synchronized(active) { for(CancellationSignal signal:active) signal.cancel();active.clear(); }workers.shutdownNow();cache.evictAll(); }
}
