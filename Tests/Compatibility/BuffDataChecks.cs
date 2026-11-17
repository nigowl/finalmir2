internal static class BuffDataChecks
{
    public static void Run(Action<bool,string> check)
    {
        var original=new[]{
            (value:(object)false,bytes:"0001000000FFFFFFFF010000000000000004010000000E53797374656D2E426F6F6C65616E01000000076D5F76616C75650001000B"),
            (value:(object)123456L,bytes:"0001000000FFFFFFFF010000000000000004010000000C53797374656D2E496E74363401000000076D5F76616C7565000940E20100000000000B")};
        foreach(var sample in original)
        {
            check(Convert.ToHexString(Functions.SerializeToBytes(sample.value))==sample.bytes,"Buff内部数据没有保持原序列化字节格式");
            check(Functions.DeserializeFromBytes(Convert.FromHexString(sample.bytes)).Equals(sample.value),"原Buff内部数据无法读取");
        }
        foreach(object value in new object[]{false,true,long.MinValue,0L,long.MaxValue})check(Functions.DeserializeFromBytes(Functions.SerializeToBytes(value)).Equals(value),"Buff边界不能往返保存");
        void Reject(Action action){try{action();check(false,"Buff读取接受未知对象或破损数据");}catch(Exception e)when(e is InvalidDataException or EndOfStreamException){check(true,"");}}
        Reject(()=>Functions.SerializeToBytes("未知对象"));
        var bytes=Convert.FromHexString(original[0].bytes);bytes[^2]=2;Reject(()=>Functions.DeserializeFromBytes(bytes));
        Reject(()=>Functions.DeserializeFromBytes(bytes[..^1]));Reject(()=>Functions.DeserializeFromBytes([..bytes,0]));
        bytes[0]=1;Reject(()=>Functions.DeserializeFromBytes(bytes));
    }
}
