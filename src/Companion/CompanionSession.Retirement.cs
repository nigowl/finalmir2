using C = ClientPackets;
using S = ServerPackets;

namespace Mir2.Companion;

public sealed partial class CompanionSession
{
    private long logoutSent,logoutRetryAt;
    private bool logoutConfirmed;

    private async Task Retire(long now)
    {
        if(protection.PendingItem!=0){protection.NextItem(state,preferences,now);return;}
        if(moving){if(now-moveSent>3000)throw new TimeoutException("下线前移动回执超时，停止自动调度。");return;}
        if(logoutSent!=0)
        {
            if(now-logoutSent>5000)throw new TimeoutException("退出角色回执未知，已停止且不会自动重发退出请求。");
            return;
        }
        CloseStorage();
        // 退出仍受战斗保护；保留原保命和近身自卫，不能站着挨打直到超时。
        if(state.Me?.Dead!=true && (await Protect(now) || Knowledge!=null && await Defend(now,state.CalculateStats())))return;
        if(now<logoutRetryAt)return;
        await Status("下线中","等待服务器确认退出角色");
        logoutSent=now;
        // 写入TCP不等于原服已退出；收到LogOutSuccess后才能关闭连接。
        await connection.Send(new C.LogOut());
    }

    private async Task<bool> RetirementPacket(Packet packet)
    {
        if(!retiring||logoutSent==0)return false;
        if(packet is S.LogOutSuccess)
        {
            logoutConfirmed=true;await Status("下线中","原服确认退出角色，关闭本次连接");return true;
        }
        if(packet is S.LogOutFailed)
        {
            // 只有明确拒绝才可再申请，继续受原战斗保护及宿主30秒退场预算约束。
            logoutSent=0;logoutRetryAt=Environment.TickCount64+1000;
            await Status("下线中","原服暂时拒绝退出，等待战斗保护结束后再申请");return true;
        }
        return false;
    }
}
