using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using TEngine;

namespace GameLogic
{
    [Window(UILayer.UI, location : "LoginUI")]
    public partial class LoginUI
    {
        #region Override

        protected override void RegisterEvent()
        {
            AddUIEvent(ILoginUI_Event.OnLoginSuccess, OnLoginSuccess);
        }

        #endregion

        #region 事件

        private void OnLoginSuccess()
        {
            Close();
        }

        private partial void OnClickRegisterBtn()
        {
            if (DataCenterSys.Instance.IsBattleTestRunning)
            {
                DataCenterSys.Instance.StopBattleTest();
                Log.Info("[BattleTest] stop requested from LoginUI.");
                return;
            }

            DataCenterSys.Instance.Register("127.0.0.1", 20001, m_inputAccount.text, m_inputPassword.text).Coroutine();
        }

        private partial void OnClickLoginBtn()
        {
            if (TryStartBattleTest())
            {
                return;
            }

            DataCenterSys.Instance.Login("127.0.0.1", 20001, m_inputAccount.text, m_inputPassword.text).Coroutine();
        }

        private bool TryStartBattleTest()
        {
            var account = m_inputAccount.text?.Trim();
            if (!string.Equals(account, "/battle", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var durationSeconds = 300;
            if (int.TryParse(m_inputPassword.text?.Trim(), out var parsedDuration) && parsedDuration > 0)
            {
                durationSeconds = parsedDuration;
            }

            DataCenterSys.Instance.StartBattleTest("127.0.0.1", 20101, durationSeconds, 30).Coroutine();
            Log.Info($"[BattleTest] start requested from LoginUI. duration={durationSeconds}s, hz=30");
            return true;
        }

        #endregion
    }
}
