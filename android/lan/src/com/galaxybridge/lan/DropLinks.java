package com.galaxybridge.lan;

import java.io.IOException;
import java.net.URI;
import java.net.URISyntaxException;
import java.util.ArrayList;
import java.util.LinkedHashSet;

/** Content references only: never turn a path or filename into storage access. */
final class DropLinks {
    private final LinkedHashSet<String> links=new LinkedHashSet<>();
    boolean add(String value) throws IOException {
        if(value==null || value.length()>8192) return false;
        try {
            URI uri=new URI(value);
            if(!"content".equals(uri.getScheme()) || uri.getRawAuthority()==null || uri.getRawAuthority().isEmpty() || uri.getRawFragment()!=null) return false;
        } catch(URISyntaxException e) { return false; }
        if(!links.contains(value) && links.size()>=DropProbe.MAX_FILES) throw new IOException("Принимается не более 100 файлов.");
        links.add(value);return true; // Duplicates still constitute a valid source item.
    }
    boolean text(CharSequence value) throws IOException {
        if(value==null || value.length()>8192) return false;
        return add(value.toString().trim());
    }
    ArrayList<String> values() { return new ArrayList<>(links); }
}
