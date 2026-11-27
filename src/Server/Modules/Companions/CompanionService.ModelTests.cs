using System.Diagnostics;
using System.Text.Json;
using Mir2.Companions;

namespace Server.ConsoleHost;

public sealed partial class CompanionService
{
    private readonly SemaphoreSlim modelTests=new(2);
    private readonly Dictionary<Guid,DateTimeOffset> lastModelTest=[];
    public async Task<CompanionLlmTestResult> TestModel(CompanionLlmTest request,CancellationToken token)
    {
        CompanionValidation.Model(request.Model);
        if(request.Key?.Length>4096)throw new InvalidDataException("模型密钥过长。");
        if(!await modelTests.WaitAsync(0,token))return new(false,"已有两个模型正在测试，请稍后重试。",0);
        var watch=Stopwatch.StartNew();
        try
        {
            var connection=await runtime.Invoke(()=>
            {
                store.Ready();
                if(lastModelTest.GetValueOrDefault(request.Model.Id)>DateTimeOffset.UtcNow)throw new InvalidOperationException("同一模型请间隔5秒再测试。");
                if(!store.ReserveLlm())throw new InvalidOperationException("本版本今日模型请求额度已用完。");
                lastModelTest[request.Model.Id]=DateTimeOffset.UtcNow.AddSeconds(5);
                if(lastModelTest.Count>100)foreach(var id in lastModelTest.Where(p=>p.Value<DateTimeOffset.UtcNow).Select(p=>p.Key).ToArray())lastModelTest.Remove(id);
                var key=request.ClearKey?"":request.Key??CompanionModels.SavedKey(store.Configuration,request.Model.Id,request.Model.Endpoint);
                return new CompanionModelConnection(request.Model,key);
            },token);
            var text=await llm.ReplyTo(connection,new(){Name="模型连接检查"},"这是一条连接测试，请回复一句中文问候。",token);
            return new(text.Length>0,text.Length>0?"测试通过：接口、鉴权、模型和短文本 JSON 返回正常。":"接口已响应，但返回内容不符合陪玩短文本要求。",watch.ElapsedMilliseconds);
        }
        catch(HttpRequestException error)
        {return new(false,error.StatusCode is {} status?$"测试失败：接口返回 HTTP {(int)status}，请检查地址、密钥、模型权限。":"测试失败：无法连接接口，请检查地址、网络和证书。",watch.ElapsedMilliseconds);}
        catch(OperationCanceledException)when(!token.IsCancellationRequested)
        {return new(false,"测试超时：5秒内未收到完整响应。",watch.ElapsedMilliseconds);}
        catch(Exception error)when(error is JsonException or InvalidDataException or KeyNotFoundException or IndexOutOfRangeException or InvalidOperationException or ArgumentException)
        {return new(false,error.Message is "同一模型请间隔5秒再测试。"or"本版本今日模型请求额度已用完。"?error.Message:"测试失败：模型响应格式或内容不符合陪玩协议。",watch.ElapsedMilliseconds);}
        finally{modelTests.Release();}
    }
}
