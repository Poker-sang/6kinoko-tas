namespace KinokoTAS.Core;

/// <summary>Bounded frame images only; never represents restorable simulation state.</summary>
public sealed class PreviewCache(long capacityBytes=64*1024*1024) {
    readonly Dictionary<long,LinkedListNode<PreviewFrame>> frames=[];
    readonly LinkedList<PreviewFrame> recent=[];
    long bytes;
    public long Bytes=>bytes;
    public PreviewFrame? Get(long completed) {
        if(!frames.TryGetValue(completed,out var node))return null;
        recent.Remove(node);recent.AddLast(node);
        return node.Value;
    }
    public void Add(PreviewFrame frame) {
        if(frame.Pixels.LongLength>capacityBytes)return;
        if(frames.Remove(frame.Completed,out var old)){bytes-=old.Value.Pixels.LongLength;recent.Remove(old);}
        var owned=frame with {Pixels=(byte[])frame.Pixels.Clone()};
        frames.Add(frame.Completed,recent.AddLast(owned));bytes+=owned.Pixels.LongLength;
        while(bytes>capacityBytes && recent.First is { } first){bytes-=first.Value.Pixels.LongLength;frames.Remove(first.Value.Completed);recent.RemoveFirst();}
    }
    public void Clear(){frames.Clear();recent.Clear();bytes=0;}
}
