using System.Security.Cryptography;
using System.Text.Json;
using GalaxyBridge.Core;

internal static class NewFeatureTests
{
    public static void Run(Action<bool,string> check)
    {
        string root=Path.Combine(Path.GetTempPath(),"GalaxyBridge-new-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        string cache=Path.Combine(root,"cache"),received=Path.Combine(root,"received"),id=Guid.NewGuid().ToString("N");
        byte[] bytes=new byte[70000];new Random(37).NextBytes(bytes);string hash=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        JsonElement Message(object value)=>JsonSerializer.SerializeToElement(value);
        var begin=new {type="uploadBegin",transfer=id,name="photo.bin",size=bytes.Length,sha256=hash,resume=true};
        try {
            using(var first=new ResumeFileReceiver(cache)) {
                first.Accept(Message(begin),received,true);first.Accept(Message(new {type="uploadChunk",transfer=id,seq=0,data=Convert.ToBase64String(bytes,0,32768)}),received,true);
                check(first.Offset==32768,"resume stores actual confirmed bytes");
            }
            using(var second=new ResumeFileReceiver(cache)) {
                second.Accept(Message(begin),received,true);check(second.Offset==32768,"resume survives receiver recreation");
                for(int offset=32768,seq=0;offset<bytes.Length;offset+=32768,seq++) second.Accept(Message(new {type="uploadChunk",transfer=id,seq,data=Convert.ToBase64String(bytes,offset,Math.Min(32768,bytes.Length-offset))}),received,true);
                second.Accept(Message(new {type="uploadEnd",transfer=id,sha256=hash}),received,true);
                check(File.ReadAllBytes(second.State.SavedPath!).SequenceEqual(bytes),"resumed transfer publishes exact original bytes");
                check(!Directory.GetFiles(cache,"*.part").Any(),"completed resume removes partial spool");
            }
            using(var third=new ResumeFileReceiver(cache)) {
                string name=third.Accept(Message(begin),received,true);
                check(third.AlreadySaved && name=="photo.bin","lost final ACK returns existing receipt");
                check(Directory.GetFiles(received).Length==1,"lost final ACK does not publish a duplicate");
            }
            string cancelId=Guid.NewGuid().ToString("N");
            using(var cancelled=new ResumeFileReceiver(cache)) {
                cancelled.Accept(Message(new {type="uploadBegin",transfer=cancelId,name="cancel.bin",size=bytes.Length,sha256=hash}),received,true);
                cancelled.Accept(Message(new {type="uploadChunk",transfer=cancelId,seq=0,data=Convert.ToBase64String(bytes,0,20)}),received,true);
                cancelled.Expire(Environment.TickCount64+31000);check(!cancelled.State.Busy && File.Exists(Path.Combine(cache,cancelId+".part")),"network inactivity preserves resume bytes");
                cancelled.Cancel();check(!File.Exists(Path.Combine(cache,cancelId+".part")),"explicit cancel deletes its own partial");
            }
            string badId=Guid.NewGuid().ToString("N");
            using(var invalid=new ResumeFileReceiver(cache)) {
                invalid.Accept(Message(new {type="uploadBegin",transfer=badId,name="bad.bin",size=4,sha256=new string('0',64)}),received,true);
                invalid.Accept(Message(new {type="uploadChunk",transfer=badId,seq=0,data=Convert.ToBase64String(new byte[4])}),received,true);
                bool rejected=false;try { invalid.Accept(Message(new {type="uploadEnd",transfer=badId,sha256=new string('0',64)}),received,true); }catch(InvalidDataException) { rejected=true; }
                check(rejected && !File.Exists(Path.Combine(received,"bad.bin")),"bad checksum never publishes a resumed file");
            }
            string history=Path.Combine(root,"history.json");var journal=new TransferJournal(history);
            string lockedId=Guid.NewGuid().ToString("N");
            var lockedBegin=new {type="uploadBegin",transfer=lockedId,name="locked.bin",size=bytes.Length,sha256=hash};
            using(var owner=new ResumeFileReceiver(cache)) using(var contender=new ResumeFileReceiver(cache)) {
                owner.Accept(Message(lockedBegin),received,true);bool busy=false;
                try { contender.Accept(Message(lockedBegin),received,true); }catch(ResumeFileBusyException) { busy=true; }
                check(busy,"overlapping reconnect reports transient busy while previous receiver owns partial file");
                owner.Suspend();contender.Accept(Message(lockedBegin),received,true);contender.RequestCancel();bool stopped=false;
                try { contender.Accept(Message(new {type="uploadEnd",transfer=lockedId,sha256=hash}),received,true); }catch(OperationCanceledException) { stopped=true; }
                check(stopped && !File.Exists(Path.Combine(received,"locked.bin")),"requested cancellation aborts verification without publishing");
            }
            for(int i=0;i<105;i++) journal.Add(new(i.ToString(),DateTimeOffset.UtcNow,"ПК → телефон","file"+i,"Ошибка",Source:"/tmp/source",Error:"offline"));
            var restored=new TransferJournal(history);check(restored.Entries.Length==100 && restored.Entries[0].Name=="file104","journal persists bounded ordered history including errors");
            restored.Clear();check(new TransferJournal(history).Entries.Length==0,"clear history persists without touching transferred files");
            check(File.Exists(Path.Combine(received,"photo.bin")),"clearing history preserves actual files");
            string request=Guid.NewGuid().ToString("N"),frame=Guid.NewGuid().ToString("N");
            byte[] jpeg=[255,216,255,192,0,8,8,0,3,0,2,0,255,217];
            var frames=new ScreenFrames(request);
            frames.Accept(Message(new {type="screenBegin",request,frame,width=2,height=3,size=jpeg.Length,revision=1}));
            frames.Accept(Message(new {type="screenChunk",request,frame,seq=0,data=Convert.ToBase64String(jpeg)}));
            check(frames.Accept(Message(new {type="screenEnd",request,frame})) is {Width:2,Height:3,Revision:1},"screen frame assembles in order with dimensions");
            check(!ScreenFrames.DimensionsMatch(jpeg,200,300),"screen rejects decompression dimension mismatch before decoding");
            check(frames.Accept(Message(new {type="screenBegin",request=Guid.NewGuid().ToString("N"),frame,width=2,height=3,size=jpeg.Length,revision=1})) is null,"stale screen request cannot replace active frame");
            bool oversized=false;try { frames.Accept(Message(new {type="screenBegin",request,frame,width=960,height=960,size=512001,revision=1})); }catch(InvalidDataException) { oversized=true; }
            check(oversized,"screen bounds compressed frame allocation");
        } finally { Directory.Delete(root,true); }
    }
}
