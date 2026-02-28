using UnityEngine;
using VRWorkspace.UI.RTT;
using VRWorkspace.UI.RTT.Controllers;

namespace VRWorkspace.Infrastructure.Streaming
{
    /// <summary>
    /// Factory for creating Remote Desktop app content.
    /// </summary>
    public class RemoteAppContentFactory : IRTTAppContentFactory
    {
        public RTTAppRegistry.AppType AppType => RTTAppRegistry.AppType.Remote;

        public void CreateContent(RTTAppInstance instance, AppContentContext context)
        {
            Debug.Log($"[RemoteAppContentFactory] Creating RemoteMenu content...");

            GameObject controllerObj = new GameObject($"RemoteMenuController_{instance.AppId}");
            controllerObj.transform.SetParent(context.ParentTransform);
            var controller = controllerObj.AddComponent<RTTRemoteMenuController>();

            var containerSize = instance.Frame.GetContentSize();

            instance.MenuContent = controller.CreateMenu(
                instance.Frame.ContentContainer,
                containerSize.x,
                containerSize.y,
                context.Font,
                context.PrimaryColor,
                context.AccentColor
            );

            instance.Controller = controller;
            controller.OnBackClicked += () => context.OnCloseApp?.Invoke(instance.AppId);
            instance.Frame.MarkDirty();
        }
    }
}
