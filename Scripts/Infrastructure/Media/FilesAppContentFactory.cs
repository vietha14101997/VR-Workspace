using UnityEngine;
using VRWorkspace.UI.RTT;
using VRWorkspace.UI.RTT.Controllers;

namespace VRWorkspace.Infrastructure.FileManager
{
    /// <summary>
    /// Factory for creating File Manager app content.
    /// </summary>
    public class FilesAppContentFactory : IRTTAppContentFactory
    {
        public RTTAppRegistry.AppType AppType => RTTAppRegistry.AppType.Files;

        public void CreateContent(RTTAppInstance instance, AppContentContext context)
        {
            Debug.Log($"[FilesAppContentFactory] Creating FileManager content...");

            GameObject controllerObj = new GameObject($"FileManagerController_{instance.AppId}");
            controllerObj.transform.SetParent(context.ParentTransform);
            var controller = controllerObj.AddComponent<RTTFileManagerController>();

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
