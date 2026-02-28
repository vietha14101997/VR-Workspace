using UnityEngine;
using VRWorkspace.UI.RTT;
using VRWorkspace.Media.Core;

namespace VRWorkspace.Infrastructure.Media
{
    /// <summary>
    /// Factory for creating Media Player app content.
    /// </summary>
    public class MediaAppContentFactory : IRTTAppContentFactory
    {
        public RTTAppRegistry.AppType AppType => RTTAppRegistry.AppType.Media;

        public void CreateContent(RTTAppInstance instance, AppContentContext context)
        {
            Debug.Log($"[MediaAppContentFactory] Creating Media content...");

            GameObject controllerObj = new GameObject($"MediaController_{instance.AppId}");
            controllerObj.transform.SetParent(context.ParentTransform);
            var controller = controllerObj.AddComponent<VRMediaAppController>();

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
