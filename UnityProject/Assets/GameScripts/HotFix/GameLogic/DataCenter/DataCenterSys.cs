using System.Collections.Generic;
using Fantasy;
using Fantasy.Async;
using TEngine;
using Log = TEngine.Log;

namespace GameLogic
{
    /// <summary>
    /// 鏁版嵁涓績妯″潡
    /// </summary>
    public partial class DataCenterSys : Singleton<DataCenterSys>, IUpdate
    {
        private readonly List<IDataCenterModule> m_dataCenterModuleList = new List<IDataCenterModule>();

        public string LoginToken { get; private set; } = string.Empty;
        public bool HasLoginToken => !string.IsNullOrWhiteSpace(LoginToken);

        protected override void OnInit()
        {
            RegCmdHandle();
            InitModule();
            InitOtherModule();
        }

        private void RegCmdHandle()
        {

        }

        #region 缃戠粶鎿嶄綔

        /// <summary>
        /// 娉ㄥ唽鏂拌处鍙枫€?
        /// </summary>
        /// <param name="address">鏈嶅姟鍣ㄥ湴鍧€</param>
        /// <param name="port">鏈嶅姟鍣ㄧ鍙?/param>
        /// <param name="userName">鐢ㄦ埛鍚?/param>
        /// <param name="password">瀵嗙爜</param>
        public async FTask Register(string address, int port, string userName, string password)
        {
            await GameClient.Instance.ConnectAsync(address, port);
            GameClient.Instance.Status = GameClientStatus.StatusRegister;
            var response = (A2C_RegisterResponse)await GameClient.Instance.Call(new C2A_RegisterRequest()
            {
                UserName = userName,
                Password = password
            });
            if (response.ErrorCode != 0)
            {
                Log.Warning($"Error: {response.ErrorCode}");
                return;
            }
            Log.Info("Registered Successfully");
        }

        /// <summary>
        /// 鐧诲綍璐﹀彿骞惰繛鎺ュ埌 Gate 鏈嶅姟鍣ㄣ€?
        /// </summary>
        /// <param name="address">璁よ瘉鏈嶅姟鍣ㄥ湴鍧€</param>
        /// <param name="port">璁よ瘉鏈嶅姟鍣ㄧ鍙?/param>
        /// <param name="userName">鐢ㄦ埛鍚?/param>
        /// <param name="password">瀵嗙爜</param>
        public async FTask Login(string address, int port, string userName, string password)
        {
            LoginToken = string.Empty;
            await GameClient.Instance.ConnectAsync(address, port);
            GameClient.Instance.Status = GameClientStatus.StatusLogin;
            var response = (A2C_LoginResponse)await GameClient.Instance.Call(new C2A_LoginRequest()
            {
                UserName = userName,
                Password = password,
                LoginType = 1
            });

            if (response.ErrorCode != 0)
            {
                Log.Warning($"Error: {response.ErrorCode}");
                return;
            }

            if (string.IsNullOrWhiteSpace(response.Token))
            {
                Log.Warning("Login failed: authentication server returned an empty token.");
                return;
            }

            LoginToken = response.Token.Trim();
            Log.Info("Login Successfully");
            GameClient.Instance.Status = GameClientStatus.StatusEnter;
            GameEvent.Get<ILoginUI>().OnLoginSuccess();
        }

        public async FTask<bool> EnsureAutomationLogin(
            string address,
            int port,
            string userName,
            string password)
        {
            if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
            {
                Log.Warning("Automation authentication requires non-empty credentials.");
                return false;
            }

            GameClient.Instance.Disconnect();
            GameClient.Instance.Status = GameClientStatus.StatusClose;
            bool connected = await GameClient.Instance.ConnectAsync(address, port);
            if (!connected)
            {
                Log.Warning($"Automation auth connection failed: {address}:{port}");
                return false;
            }

            GameClient.Instance.Status = GameClientStatus.StatusRegister;
            var registerResponse = (A2C_RegisterResponse)await GameClient.Instance.Call(
                new C2A_RegisterRequest
                {
                    UserName = userName,
                    Password = password
                });
            if (registerResponse == null ||
                (registerResponse.ErrorCode != 0u && registerResponse.ErrorCode != 1002u))
            {
                Log.Warning($"Automation account preparation failed. Error={registerResponse?.ErrorCode ?? 0u}");
                return false;
            }

            GameClient.Instance.Disconnect();
            GameClient.Instance.Status = GameClientStatus.StatusClose;
            await Login(address, port, userName, password);
            return HasLoginToken;
        }

        /// <summary>
        /// 直接连接战斗场景（MVP 阶段硬连接，不经过大厅/匹配）。
        /// </summary>
        public async FTask<bool> ConnectBattle(string address, int port)
        {
            GameClient.Instance.Disconnect();
            GameClient.Instance.Status = GameClientStatus.StatusClose;
            bool connected = await GameClient.Instance.ConnectAsync(address, port);
            if (!connected)
            {
                Log.Warning($"Connect battle failed: {address}:{port}");
                return false;
            }

            GameClient.Instance.Status = GameClientStatus.StatusEnter;
            Log.Info($"Connected battle: {address}:{port}");
            return true;
        }

        public void ClearLoginToken()
        {
            LoginToken = string.Empty;
        }

        #endregion

        #region Module鐩稿叧

        private void InitOtherModule()
        {
        }

        partial void InitModule();

        #endregion

        /// <summary>
        /// 姣忓抚鏇存柊鎵€鏈夊凡娉ㄥ唽鐨勬ā鍧椼€?
        /// </summary>
        public void OnUpdate()
        {
            foreach (var module in m_dataCenterModuleList)
            {
                module.OnUpdate();
            }
        }

        /// <summary>
        /// 娓呴櫎瀹㈡埛绔暟鎹紝鍏抽棴鎵€鏈夌獥鍙ｅ苟閫氱煡鎵€鏈夋ā鍧楄鑹茬櫥鍑恒€?
        /// </summary>
        public void ClearClientData()
        {
            ClearLoginToken();
            UIModule.Instance.CloseAll();
            for (int i = 0; i < m_dataCenterModuleList.Count; i++)
            {
                m_dataCenterModuleList[i].OnRoleLogout();
            }
        }
    }
}
