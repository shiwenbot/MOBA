using TEngine;

namespace GameLogic
{
    /// <summary>
    /// 战斗相关 UI 事件。带宽统计上报等由 <see cref="BattleClientController"/> 派发，
    /// 调试面板监听刷新。
    /// </summary>
    [EventInterface(EEventGroup.GroupUI)]
    public interface IBattleUI
    {
        /// <summary>服务端带宽统计上报到达（每 10 秒一次，对应服务端报告窗口）。</summary>
        void OnBandwidthStatsUpdated();
    }
}
