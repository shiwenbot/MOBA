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

        protected override void OnCreate()
        {
            base.OnCreate();

            _battleClientController = gameObject.GetComponent<BattleClientController>();
            if (_battleClientController == null)
            {
                _battleClientController = gameObject.AddComponent<BattleClientController>();
            }

            _battleClientController.Initialize();
        }

        protected override void OnDestroy()
        {
            _battleClientController?.DisposeController();
            _battleClientController = null;
            base.OnDestroy();
        }
    }
}
