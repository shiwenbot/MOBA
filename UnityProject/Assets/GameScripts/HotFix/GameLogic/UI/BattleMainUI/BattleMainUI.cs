using TEngine;
using UnityEngine;

namespace GameLogic
{
    [Window(UILayer.UI, location: "BattleMainUI")]
    class BattleMainUI : UIWindow
    {
        #region 脚本工具生成的代码
        private RectTransform _rectContainer;
        private GameObject _goTopInfo;
        private GameObject _itemRoleInfo;
        private GameObject _itemMonsterInfo;

        protected override void ScriptGenerator()
        {
            _rectContainer = FindChildComponent<RectTransform>("m_rectContainer");
            _goTopInfo = FindChild("m_goTopInfo").gameObject;
            _itemRoleInfo = FindChild("m_goTopInfo/m_itemRoleInfo").gameObject;
            _itemMonsterInfo = FindChild("m_goTopInfo/m_itemMonsterInfo").gameObject;
        }
        #endregion

        private BattleClientController _battleClientController;
        private BattleAutomationController _battleAutomationController;

        protected override void OnCreate()
        {
            base.OnCreate();

            _battleClientController = gameObject.GetComponent<BattleClientController>();
            if (_battleClientController == null)
            {
                _battleClientController = gameObject.AddComponent<BattleClientController>();
            }

            _battleClientController.Initialize();

            if (BattleAutomationConfig.Current.Enabled)
            {
                Log.Warning(
                    $"[Automation] BattleMainUI enabling automation controller. client={BattleAutomationConfig.Current.ClientId}");
                _battleAutomationController = gameObject.GetComponent<BattleAutomationController>();
                if (_battleAutomationController == null)
                {
                    _battleAutomationController = gameObject.AddComponent<BattleAutomationController>();
                }

                _battleAutomationController.Initialize(_battleClientController);
            }
        }

        protected override void OnDestroy()
        {
            _battleClientController?.DisposeController();
            _battleClientController = null;
            _battleAutomationController = null;
            base.OnDestroy();
        }
    }
}
