package com.galaxybridge.lan;

/** A missing accessibility tree is not evidence that the user changed apps. */
final class FilesForeground {
    static final long GRACE=1000;
    private long confirmed=-1;
    private long queried=-1;
    boolean refresh(long now) {
        if(queried<0 || now<queried || now-queried>=100) { queried=now;return true; }
        return false;
    }
    void invalidate() { queried=-1; }
    boolean check(String packageName,boolean available,long now) {
        if(!available) { reset();return false; }
        if(packageName!=null) {
            if(!"com.galaxybridge.files".equals(packageName)) { reset();return false; }
            confirmed=now;return true;
        }
        return confirmed>=0 && now>=confirmed && now-confirmed<=GRACE;
    }
    void reset() { confirmed=-1;queried=-1; }
}
