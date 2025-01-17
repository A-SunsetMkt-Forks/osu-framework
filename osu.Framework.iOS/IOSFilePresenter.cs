// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using Foundation;
using UIKit;
using UniformTypeIdentifiers;

namespace osu.Framework.iOS
{
    public class IOSFilePresenter : UIDocumentInteractionControllerDelegate
    {
        private readonly IIOSWindow window;
        private readonly UIDocumentInteractionController documentInteraction = new UIDocumentInteractionController();

        public IOSFilePresenter(IIOSWindow window)
        {
            this.window = window;
        }

        public bool OpenFile(string filename)
        {
            setupViewController(filename);

            if (documentInteraction.PresentPreview(true))
                return true;

            var gameView = window.ViewController.View!;
            return documentInteraction.PresentOpenInMenu(gameView.Bounds, gameView, true);
        }

        public bool PresentFile(string filename)
        {
            setupViewController(filename);

            var gameView = window.ViewController.View!;
            return documentInteraction.PresentOptionsMenu(gameView.Bounds, gameView, true);
        }

        private void setupViewController(string filename)
        {
            var url = NSUrl.FromFilename(filename);

            documentInteraction.Url = url;
            documentInteraction.Delegate = this;

            if (OperatingSystem.IsIOSVersionAtLeast(14))
                documentInteraction.Uti = UTType.CreateFromExtension(Path.GetExtension(filename))?.Identifier ?? UTTypes.Data.Identifier;
        }

        public override UIViewController ViewControllerForPreview(UIDocumentInteractionController controller) => window.ViewController;

        public override void WillBeginSendingToApplication(UIDocumentInteractionController controller, string? application)
        {
            // this path is triggered when a user opens the presented document in another application,
            // the menu does not dismiss afterward and locks the game indefinitely. dismiss it manually.
            documentInteraction.DismissMenu(true);
        }
    }
}
