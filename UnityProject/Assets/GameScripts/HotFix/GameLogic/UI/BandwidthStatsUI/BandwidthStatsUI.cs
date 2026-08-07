using TEngine;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    /// <summary>
    /// 带宽节省量 + RTT/超前量调试面板。
    /// 带宽数据由服务端 S2C_BandwidthStats 每 10 秒推送；
    /// RTT 数据由 S2C_RttStats 每逻辑帧 per-session 推送（S7）。
    /// </summary>
    [Window(UILayer.System, fromResources: true)]
    internal class BandwidthStatsUI : UIWindow
    {
        #region 脚本工具生成的代码（手写绑定，参考 LogUI）

        private Text m_textResult; // 核心结论：节省量
        private Text m_textMeta;   // 状态行：帧号 / 开关提示 / RTT
        private Button m_btnClose;

        protected override void ScriptGenerator()
        {
            m_textResult = FindChildComponent<Text>("m_text_Result");
            m_textMeta = FindChildComponent<Text>("m_text_Meta");
            m_btnClose = FindChildComponent<Button>("m_btn_Close");
            m_btnClose.onClick.AddListener(OnClickCloseBtn);

            rectTransform.sizeDelta = new Vector2(rectTransform.sizeDelta.x, 235.0f);
            m_textMeta.rectTransform.sizeDelta = new Vector2(-60.0f, 58.0f);
            m_textResult.rectTransform.anchoredPosition = new Vector2(15.0f, -73.0f);
            m_textResult.rectTransform.sizeDelta = new Vector2(-30.0f, 152.0f);
        }

        #endregion

        #region 事件

        private BattleClientController _controller;

        protected override void RegisterEvent()
        {
            AddUIEvent(IBattleUI_Event.OnBandwidthStatsUpdated, Refresh);
            AddUIEvent(IBattleUI_Event.OnPredictionErrorUpdated, Refresh);
            AddUIEvent(IBattleUI_Event.OnRttStatsUpdated, Refresh);
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

            string rttSummary = "RTT 等待探测";
            if (_controller.HasRttStats)
            {
                BattleClientController.RttStatsSnapshot rtt = _controller.LatestRttStats;
                if (!rtt.Enabled)
                {
                    rttSummary = "RTT 探测关闭";
                }
                else if (!rtt.HasSample)
                {
                    rttSummary = "RTT 尚无样本";
                }
                else
                {
                    // controlRtt 与 rttMin 分离、targetLead 与实际 lead 并列（S7 决策六）
                    rttSummary =
                        $"ctrl {rtt.ControlRttMs:F0}ms min {rtt.RttMinMs:F0}ms ema {rtt.RttEmaMs:F0}ms | " +
                        $"targetLead {rtt.AppliedTargetLeadFrames} lead {rtt.LeadFrames} n={rtt.RttSampleCount}";
                }
            }

            string gameplaySummary = "Dash 等待同步 | 体力 — | 击退 —";
            if (_controller.HasGameplayStatus)
            {
                BattleClientController.GameplayStatusSnapshot gameplay = _controller.LatestGameplayStatus;
                gameplaySummary =
                    $"Dash {gameplay.Phase} {gameplay.DashRemainingFrames}f | " +
                    $"体力 {gameplay.Stamina}/{gameplay.MaxStamina} | " +
                    $"击退 {gameplay.KnockbackRemainingFrames}f";
            }

            string metaSummary = gameplaySummary + "\n" + predictionSummary + "\n" + rttSummary;

            if (!_controller.HasBandwidthStats)
            {
                m_textMeta.text = metaSummary;
                m_textResult.text = rollbackSummary + "\n尚未收到服务端带宽上报（每 10 秒一次）";
                return;
            }

            BattleClientController.BandwidthStatsSnapshot s = _controller.LatestBandwidthStats;

            if (!s.MeasureFullSyncBaseline)
            {
                m_textMeta.text = metaSummary;
                m_textResult.text =
                    rollbackSummary + "\n" +
                    $"frame {s.FrameIndex} · 未开启带宽对照测量\n" +
                    "BATTLE_BANDWIDTH_FULLSYNC_BASELINE=1";
                return;
            }

            if (!s.HasSamples || s.FullSyncPayloadBytes <= 0)
            {
                m_textMeta.text = metaSummary;
                m_textResult.text = rollbackSummary + $"\nframe {s.FrameIndex} · 带宽窗口无样本";
                return;
            }

            m_textMeta.text = metaSummary;
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
