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

        /// <summary>预测误差渲染状态更新。</summary>
        void OnPredictionErrorUpdated();

        /// <summary>服务端 RTT 统计上报到达（每逻辑帧，per-session）。</summary>
        void OnRttStatsUpdated();

        /// <summary>断线、重试、等待全量快照与恢复状态发生变化。</summary>
        void OnReconnectStatusUpdated();

    }
}
