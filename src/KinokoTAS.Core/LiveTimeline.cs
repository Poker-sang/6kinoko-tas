using System.Buffers.Binary;
namespace KinokoTAS.Core;
// Read only complete, individually checked records; never fabricate replay footers.
public sealed class LiveTimeline {
    string path="";readonly List<uint> masks=[];
    public IReadOnlyList<uint> Masks=>masks;
    public void Read(string file,long completed) {
        if(path!=file){path=file;masks.Clear();}
        if(!File.Exists(file))return;
        using var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
        if(stream.Length<Replay.HeaderSize)return;
        Span<byte> header=stackalloc byte[Replay.HeaderSize];stream.ReadExactly(header);
        if(!header[..8].SequenceEqual("KINORPL1"u8)||BinaryPrimitives.ReadUInt32LittleEndian(header[8..])!=1||BinaryPrimitives.ReadUInt32LittleEndian(header[12..])!=19)throw new InvalidDataException("实时录制格式错误。");
        stream.Position=Replay.HeaderSize+(long)masks.Count*Replay.RecordSize;
        Span<byte> record=stackalloc byte[Replay.RecordSize];
        while(masks.Count<Math.Min(completed,Replay.MaxFrames) && stream.Length-stream.Position>=Replay.RecordSize){
            stream.ReadExactly(record);
            if(record[0]!=1 || BinaryPrimitives.ReadUInt64LittleEndian(record[1..])!=(ulong)masks.Count || Replay.Hash(record.Slice(1,199))!=BinaryPrimitives.ReadUInt64LittleEndian(record[200..]))throw new InvalidDataException("实时录制帧校验失败。");
            uint mask=0;for(int i=0;i<19;i++)if(BinaryPrimitives.ReadInt32LittleEndian(record[(9+i*4)..])>0)mask|=1u<<i;masks.Add(mask);
        }
    }
}
