using System.Text.Json;

namespace GalaxyBridge.Core;

public sealed record ScreenFrame(byte[] Jpeg,int Width,int Height,int Revision);

// One frame in flight; both dimensions and compressed size are bounded before decoding.
public sealed class ScreenFrames
{
    private readonly string request;
    private MemoryStream? pending;
    private string? frame;
    private int width,height,revision,size,sequence;
    public ScreenFrames(string request) { if(!LanProtocol.Identifier(request)) throw new ArgumentException("Invalid request.");this.request=request; }
    public ScreenFrame? Accept(JsonElement message)
    {
        if(message.GetProperty("request").GetString()!=request) return null;
        string type=message.GetProperty("type").GetString()??"",id=message.GetProperty("frame").GetString()??"";
        if(!LanProtocol.Identifier(id)) throw new InvalidDataException("Invalid screen frame.");
        if(type=="screenBegin") {
            int w=message.GetProperty("width").GetInt32(),h=message.GetProperty("height").GetInt32(),n=message.GetProperty("size").GetInt32(),r=message.GetProperty("revision").GetInt32();
            if(w<1 || h<1 || w>1280 || h>1280 || n<4 || n>512000 || r<0) throw new InvalidDataException("Invalid screen dimensions.");
            pending?.Dispose();pending=new MemoryStream(n);frame=id;width=w;height=h;size=n;revision=r;sequence=0;return null;
        }
        if(frame!=id || pending is null) return null;
        if(type=="screenChunk") {
            string encoded=message.GetProperty("data").GetString()??"";
            if(encoded.Length>43692 || message.GetProperty("seq").GetInt32()!=sequence++) throw new InvalidDataException("Invalid screen chunk.");
            byte[] data=Convert.FromBase64String(encoded);
            if(data.Length<1 || data.Length>LanProtocol.ChunkSize || pending.Length+data.Length>size) throw new InvalidDataException("Invalid screen frame size.");
            pending.Write(data);return null;
        }
        if(type!="screenEnd" || pending.Length!=size) throw new InvalidDataException("Incomplete screen frame.");
        byte[] jpeg=pending.ToArray();pending.Dispose();pending=null;frame=null;
        if(!DimensionsMatch(jpeg,width,height)) throw new InvalidDataException("JPEG dimensions do not match the frame.");
        return new(jpeg,width,height,revision);
    }
    public static bool DimensionsMatch(ReadOnlySpan<byte> jpeg,int width,int height)
    {
        if(jpeg.Length<4 || jpeg[0]!=255 || jpeg[1]!=216 || jpeg[^2]!=255 || jpeg[^1]!=217) return false;
        int pos=2;
        while(pos+4<=jpeg.Length) {
            if(jpeg[pos++]!=255) return false;while(pos<jpeg.Length && jpeg[pos]==255) pos++;if(pos>=jpeg.Length) return false;
            int marker=jpeg[pos++];if(marker is 0xda or 0xd9) return false;
            if(marker is 0x01 or >=0xd0 and <=0xd8) continue;
            if(pos+2>jpeg.Length) return false;int length=(jpeg[pos]<<8)|jpeg[pos+1];
            if(length<2 || pos+length>jpeg.Length) return false;
            if(marker is 0xc0 or 0xc1 or 0xc2) return length>=8 && ((jpeg[pos+3]<<8)|jpeg[pos+4])==height && ((jpeg[pos+5]<<8)|jpeg[pos+6])==width;
            pos+=length;
        }
        return false;
    }
}
