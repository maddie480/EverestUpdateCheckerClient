using System;
using FMOD.Studio;
using Celeste.Mod.UI;

namespace Celeste.Mod.UpdateChecker {
    public class UpdateCheckerModule : EverestModule {

        public static UpdateCheckerModule Instance;

        public override Type SettingsType => null;

        public UpdateCheckerModule() {
            Instance = this;
        }

        // ================ Module loading ================
        
        public override void CreateModMenuSection(TextMenu menu, bool inGame, EventInstance snapshot) {
            if(!inGame) {
                menu.Add(new TextMenu.SubHeader(Dialog.Clean("MODOPTIONS_UPDATECHECKER_TITLE") + " | v." + Metadata.VersionString));

                menu.Add(new TextMenu.Button(Dialog.Clean("UPDATECHECKER_OPEN_MENU")).Pressed(() => {
                    OuiModOptions.Instance.Overworld.Goto<OuiModUpdateList>();
                }));
            }
        }

        public override void Load() {
            // nothing
        }

        public override void Unload() {
            // nothing
        }
    }
}
