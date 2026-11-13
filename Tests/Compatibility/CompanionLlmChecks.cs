using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Mir2.Companions;
using Server.ConsoleHost;

internal static class CompanionLlmChecks
{
    public static async Task Run(Action<bool,string> check)
    {
        var probe=new TcpListener(IPAddress.Loopback,0);probe.Start();var port=((IPEndPoint)probe.LocalEndpoint).Port;probe.Stop();
        using var listener=new HttpListener();var endpoint=$"http://127.0.0.1:{port}/";listener.Prefixes.Add(endpoint);listener.Start();
        var client=new CompanionLlm();var profile=new CompanionProfile{Name="LlmTest",Notes="private-note"};
        var config=new CompanionConfiguration(new(){LlmEndpoint=endpoint,LlmModel="fixture"},"test-key","test",default,0);
        foreach(var (content,valid) in new[]{("{\"text\":\"你好，一起打怪。\"}",true),("{\"text\":\"@level 100\"}",false),("{\"text\":\"!!伪造队聊\"}",false),("{\"text\":\"你好\",\"command\":\"give\"}",false)})
        {
            var server=Task.Run(async()=>
            {
                var request=await listener.GetContextAsync();
                using var reader=new StreamReader(request.Request.InputStream);var body=await reader.ReadToEndAsync();
                check(!body.Contains("private-note")&&!body.Contains("test-key"),"模型正文泄露管理备注或密钥");
                check(request.Request.Headers["Authorization"]=="Bearer test-key","模型鉴权未独立放入请求头");
                var bytes=JsonSerializer.SerializeToUtf8Bytes(new{choices=new[]{new{message=new{content}}}});
                request.Response.ContentLength64=bytes.Length;await request.Response.OutputStream.WriteAsync(bytes);request.Response.Close();
            });
            try{var text=await client.ReplyTo(config,profile,"你好",default);check(valid?text=="你好，一起打怪。":text.Length==0,"模型输出没有按严格文本边界处理");}
            catch(JsonException){check(!valid,"合法模型输出解析失败");}
            await server;
        }
        var oversized=Task.Run(async()=>
        {
            var request=await listener.GetContextAsync();var bytes=new byte[32769];request.Response.ContentLength64=bytes.Length;
            await request.Response.OutputStream.WriteAsync(bytes);request.Response.Close();
        });
        try{await client.ReplyTo(config,profile,"你好",default);throw new Exception("模型响应上限失效");}
        catch(InvalidDataException){check(true,"超长响应已拒绝");}await oversized;
        var stalled=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var slow=Task.Run(async()=>
        {
            var request=await listener.GetContextAsync();request.Response.ContentLength64=100;
            await request.Response.OutputStream.WriteAsync(new byte[]{32});await request.Response.OutputStream.FlushAsync();
            await stalled.Task;request.Response.Abort();
        });
        var watch=System.Diagnostics.Stopwatch.StartNew();
        try{await client.ReplyTo(config,profile,"你好",default);throw new Exception("响应正文无限等待");}
        catch(OperationCanceledException){check(watch.Elapsed<TimeSpan.FromSeconds(7),"模型正文未受到总超时保护");}
        finally{stalled.SetResult();await slow;}
    }
}
