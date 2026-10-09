package com.galaxybridge.lan;
import java.net.URI;
import java.net.URLDecoder;
import java.util.HashMap;
import java.util.Map;
import java.util.HashSet;
import java.util.Arrays;

final class Pairing {
    final String host, id, pin, ticket, name;
    final int port;
    Pairing(String qr) {
        if (qr == null || qr.length() > 1200) throw new IllegalArgumentException("Invalid QR");
        URI uri;
        Map<String,String> fields=new HashMap<>();
        try {
            uri=new URI(qr);
            if (!"galaxybridge".equals(uri.getScheme()) || !"pair".equals(uri.getHost()) || uri.getFragment()!=null || uri.getUserInfo()!=null || uri.getPort()!=-1 ||
                !(uri.getPath()==null || uri.getPath().isEmpty()) || uri.getRawQuery()==null) throw new IllegalArgumentException("Invalid QR");
            for(String part:uri.getRawQuery().split("&",-1)) {
                String[] pair=part.split("=",2); if(pair.length!=2) throw new IllegalArgumentException("Invalid QR field");
                String key=URLDecoder.decode(pair[0],"UTF-8"), value=URLDecoder.decode(pair[1],"UTF-8");
                if(fields.put(key,value)!=null) throw new IllegalArgumentException("Duplicate QR field");
            }
        } catch(Exception e) { throw new IllegalArgumentException("Invalid QR",e); }
        if(!new HashSet<>(Arrays.asList("v","host","port","id","pin","ticket","name")).equals(fields.keySet())) throw new IllegalArgumentException("Invalid QR fields");
        host=fields.get("host"); id=fields.get("id"); pin=fields.get("pin"); ticket=fields.get("ticket"); name=fields.get("name");
        port=Integer.parseInt(fields.get("port"));
        if(!"1".equals(fields.get("v")) || !Wire.ipv4(host) || port!=Wire.PORT || !Wire.hex(id,32) || !Wire.hex(pin,64) || !Wire.hex(ticket,64) || name.length()>80)
            throw new IllegalArgumentException("Invalid QR");
        for(int i=0;i<name.length();i++) if(Character.isISOControl(name.charAt(i))) throw new IllegalArgumentException("Invalid QR name");
    }
}
