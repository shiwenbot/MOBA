using TEngine;

namespace GameLogic
{
    public class CommonUIController : IUIController
    {
        public void RegUIMessage()
        {
            GameEvent.AddEventListener(ILoginUI_Event.OnLoginSuccess, OnLoginSuccess);
            GameEvent.AddEventListener(ILoginUI_Event.ShowLoginUI, ShowLoginUI);
            GameEvent.AddEventListener(ILoginUI_Event.CloseLoginUI, CloseLoginUI);
        }

        private void OnLoginSuccess()
        {
            GameplaySandboxView.Create();
        }

        private void ShowLoginUI()
        {
            GameModule.UI.ShowUIAsync<LoginUI>();
        }

        private void CloseLoginUI()
        {
            GameModule.UI.CloseUI<LoginUI>();
        }
    }
}
