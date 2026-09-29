using System.Buffers.Binary;
using System.Text;
namespace KinokoTAS.Core;

/// <summary>Immutable, validated KINORPL1 source. All fields use explicit wire offsets.</summary>
public sealed class Replay {
    public const int HeaderSize=80, RecordSize=208, MaxFrames=1_000_000, ActionCount=19;
    public static readonly string[] Actions=["moveLeft","moveRight","moveUp","moveDown","jump","attack","run","carry","door","pipeUp","pipeDown","confirm","menuAcceptAlt","pause","useItem","menuLeft","menuRight","menuUp","menuDown"];
    public static readonly string[] Labels=["左","右","上","下","跳跃","攻击","加速","搬运","进门","管道↑","管道↓","确认","副确认","暂停","道具","菜单←","菜单→","菜单↑","菜单↓"];
    private readonly byte[] bytes;
    public int Count {get;}
    public string Identity {get;}
    public ReadOnlyMemory<byte> Bytes => bytes;
    private Replay(byte[] data,int count,string identity) {bytes=data;Count=count;Identity=identity;}
    public static Replay Parse(byte[] input) {
        if(input.Length<97 || input.Length>HeaderSize+(long)RecordSize*MaxFrames+17) throw new InvalidDataException("录制长度无效或超出限制。");
        var b=input.AsSpan();
        if(!b[..8].SequenceEqual("KINORPL1"u8) || U32(b[8..])!=1 || U32(b[12..])!=ActionCount) throw new InvalidDataException("不支持的录制格式或动作版本。");
        string identity=Encoding.ASCII.GetString(b.Slice(16,64));
        if(identity.Any(c=>!Uri.IsHexDigit(c))) throw new InvalidDataException("会话标识无效。");
        int at=HeaderSize,count=0; ulong chain=14695981039346656037;
        while(at<input.Length && b[at]==1) {
            if(count>=MaxFrames || input.Length-at<RecordSize) throw new InvalidDataException("录制未完整结束。");
            var record=b.Slice(at+1,199);
            if(U64(record)!=(ulong)count || U64(b[(at+200)..])!=Hash(record)) throw new InvalidDataException($"第 {count} 帧序号或校验错误。");
            // 19 logical release flags and 4 legacy release flags.
            foreach(byte flag in record.Slice(8+76,19)) if(flag>1) throw new InvalidDataException("动作释放标记无效。");
            foreach(byte flag in record.Slice(8+95+32,4)) if(flag>1) throw new InvalidDataException("兼容输入释放标记无效。");
            chain=Hash(record,chain);count++;at+=RecordSize;
        }
        if(input.Length-at!=17 || b[at]!=0 || U64(b[(at+1)..])!=(ulong)count || U64(b[(at+9)..])!=chain) throw new InvalidDataException("录制尾部不完整、校验错误或有多余数据。");
        return new Replay((byte[])input.Clone(),count,identity);
    }
    public static Replay Load(string path) {
        var info=new FileInfo(path);
        if(info.Length>HeaderSize+(long)RecordSize*MaxFrames+17)throw new InvalidDataException("录制超过大小限制。");
        return Parse(File.ReadAllBytes(path));
    }
    private ReadOnlySpan<byte> Payload(int frame) {
        if((uint)frame>=(uint)Count)throw new ArgumentOutOfRangeException(nameof(frame));
        return bytes.AsSpan(HeaderSize+frame*RecordSize+9,191);
    }
    public int Held(int frame,int action) {
        if((uint)action>=ActionCount)throw new ArgumentOutOfRangeException(nameof(action));
        return BinaryPrimitives.ReadInt32LittleEndian(Payload(frame)[(action*4)..]);
    }
    public bool Released(int frame,int action) {
        if((uint)action>=ActionCount)throw new ArgumentOutOfRangeException(nameof(action));
        return Payload(frame)[76+action]!=0;
    }
    public uint Clock(int frame)=>U32(Payload(frame)[171..]);
    public uint RandomBefore(int frame)=>U32(Payload(frame)[175..]);
    public uint RandomAfter(int frame)=>U32(Payload(frame)[179..]);
    public ulong Checkpoint(int frame)=>U64(Payload(frame)[183..]);
    private static uint U32(ReadOnlySpan<byte> b)=>BinaryPrimitives.ReadUInt32LittleEndian(b);
    private static ulong U64(ReadOnlySpan<byte> b)=>BinaryPrimitives.ReadUInt64LittleEndian(b);
    public static ulong Hash(ReadOnlySpan<byte> b,ulong h=14695981039346656037) { foreach(byte x in b) h=unchecked((h^x)*1099511628211);return h; }
}
