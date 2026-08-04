using TEngine;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    /// <summary>
    /// 带宽节省量调试面板。展示脏同步相比全量同步省了多少带宽。
    /// 数据由服务端 <c>S2C_BandwidthStats</c> 每 10 秒推送一次。
    ///
    /// 三种显示状态：
    /// - 正常：节省 XX% / 全量→实际 字节
    /// - 对照测量未开：提示需开 BATTLE_BANDWIDTH_FULLSYNC_BASELINE
    /// - 无样本/未连接：占位
    /// </summary>
    [Window(UILayer.System, fromResources: true)]
    internal class BandwidthStatsUI : UIWindow
    {
        #region 脚本工具生成的代码（手写绑定，参考 LogUI）

        private Text m_textResult; // 核心结论：节省量
        private Text m_textMeta;   // 状态行：帧号 / 开关提示
        private Button m_btnClose;

        protected override void ScriptGenerator()
        {
            m_textResult = FindChildComponent<Text>("m_text_Result");
            m_textMeta = FindChildComponent<Text>("m_text_Meta");
            m_btnClose = FindChildComponent<Button>("m_btn_Close");
            m_btnClose.onClick.AddListener(OnClickCloseBtn);
        }

        #endregion

        #region 事件

        private BattleClientController _controller;

        protected override void RegisterEvent()
        {
            AddUIEvent(IBattleUI_Event.OnBandwidthStatsUpdated, Refresh);
            AddUIEvent(IBattleUI_Event.OnPredictionErrorUpdated, Refresh);
        }

        protected override void OnRefresh()
        {
            // BattleClientController 由 GameplaySandboxView 创建并挂在 GameObject 上。
            _controller = Object.FindObjectOfType<BattleClientController>();
            Refresh();
        }

        private void Refresh()
        {
            if (_controller == null)
            {
                m_textMeta.text = "未连接战斗";
                m_textResult.text = "等待数据...";
                return;
            }

            string predictionSummary = "预测误差等待同步";
            string rollbackSummary = "回滚 0 次 · 最近帧 —";
            if (_controller.HasPredictionError)
            {
                BattleClientController.PredictionErrorSnapshot prediction = _controller.LatestPredictionError;
                predictionSummary =
                    $"误差 {prediction.LastCorrectionMagnitude:F3} | 平滑 {prediction.SmoothingRemainingSeconds * 1000.0f:F0}ms";
                rollbackSummary = $"回滚 {prediction.RollbackCount} 次 · 最近帧 {prediction.LastRollbackFrame}";
            }

            if (!_controller.HasBandwidthStats)
            {
                m_textMeta.text = predictionSummary;
                m_textResult.text = rollbackSummary + "\n尚未收到服务端带宽上报（每 10 秒一次）";
                return;
            }

            BattleClientController.BandwidthStatsSnapshot s = _controller.LatestBandwidthStats;

            if (!s.MeasureFullSyncBaseline)
            {
                m_textMeta.text = predictionSummary;
                m_textResult.text =
                    rollbackSummary + "\n" +
                    $"frame {s.FrameIndex} · 未开启带宽对照测量\n" +
                    "BATTLE_BANDWIDTH_FULLSYNC_BASELINE=1";
                return;
            }

            if (!s.HasSamples || s.FullSyncPayloadBytes <= 0)
            {
                m_textMeta.text = predictionSummary;
                m_textResult.text = rollbackSummary + $"\nframe {s.FrameIndex} · 带宽窗口无样本";
                return;
            }

            m_textMeta.text = predictionSummary;
            m_textResult.text =
                rollbackSummary + "\n" +
                $"frame {s.FrameIndex} · 带宽节省 {s.DirtySyncSavedRatio:F1}%\n" +
                $"全量 {s.FullSyncPayloadBytes} B → 实际 {s.ActualPayloadBytes} B\n" +
                $"（省 {s.DirtySyncSavedBytes} B）";
        }

        private void OnClickCloseBtn()
        {
            Close();
        }

        #endregion
    }
}
