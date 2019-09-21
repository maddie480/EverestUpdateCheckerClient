﻿using Celeste.Mod.UI;
using Microsoft.Xna.Framework;
using Monocle;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using YamlDotNet.Serialization;

namespace Celeste.Mod.UpdateChecker {
    // That's just me trying to implement a mod options submenu. Don't mind me
    // Heavily based off the OuiModOptions from Everest: https://github.com/EverestAPI/Everest/blob/master/Celeste.Mod.mm/Mod/UI/OuiModOptions.cs
    class OuiModUpdateList : Oui {

        private TextMenu menu;
        private TextMenuExt.SubHeaderExt subHeader;
        private TextMenu.Button fetchingButton;

        private const float onScreenX = 960f;
        private const float offScreenX = 2880f;

        private float alpha = 0f;

        private Task task;

        private bool shouldRestart = false;

        private class MostRecentUpdatedFirst : IComparer<ModUpdateInfo> {
            public int Compare(ModUpdateInfo x, ModUpdateInfo y) {
                if(x.LastUpdate != y.LastUpdate) {
                    return y.LastUpdate - x.LastUpdate;
                }
                // fall back to alphabetical order
                return x.Name.CompareTo(y.Name);
            }
        }

        private Dictionary<string, ModUpdateInfo> updateCatalog = null;
        private SortedDictionary<ModUpdateInfo, EverestModuleMetadata> availableUpdatesCatalog = new SortedDictionary<ModUpdateInfo, EverestModuleMetadata>(new MostRecentUpdatedFirst());

        public override IEnumerator Enter(Oui from) {
            menu = new TextMenu();

            // display the title and a dummy "Fetching" button
            menu.Add(new TextMenu.Header(Dialog.Clean("UPDATECHECKER_MENU_TITLE")));

            menu.Add(subHeader = new TextMenuExt.SubHeaderExt(Dialog.Clean("UPDATECHECKER_MENU_HEADER")));

            fetchingButton = new TextMenu.Button(Dialog.Clean("UPDATECHECKER_FETCHING"));
            fetchingButton.Disabled = true;
            menu.Add(fetchingButton);

            Scene.Add(menu);

            menu.Visible = Visible = true;
            menu.Focused = false;

            for (float p = 0f; p < 1f; p += Engine.DeltaTime * 4f) {
                menu.X = offScreenX + -1920f * Ease.CubeOut(p);
                alpha = Ease.CubeOut(p);
                yield return null;
            }

            menu.Focused = true;

            task = new Task(() => {
                // 1. Download the updates list
                Logger.Log("UpdateChecker", "Downloading last versions list");
                try {
                    using (WebClient wc = new WebClient()) {
                        string yamlData = wc.DownloadString("https://max480-random-stuff.appspot.com/celeste/everest_update.yaml");
                        updateCatalog = new Deserializer().Deserialize<Dictionary<string, ModUpdateInfo>>(yamlData);
                        foreach (string name in updateCatalog.Keys) {
                            updateCatalog[name].Name = name;
                        }
                        Logger.Log("UpdateChecker", $"Downloaded {updateCatalog.Count} item(s)");
                    }
                }
                catch (Exception e) {
                    Logger.Log("UpdateChecker", $"Download failed! {e.ToString()}");
                }

                // 2. Find out what actually has been updated
                availableUpdatesCatalog.Clear();

                if (updateCatalog != null) {
                    Logger.Log("UpdateChecker", "Checking for updates");

                    foreach(EverestModule module in Everest.Modules) {
                        EverestModuleMetadata metadata = module.Metadata;
                        if (metadata.PathArchive != null && updateCatalog.ContainsKey(metadata.Name)) {
                            string xxHashStringInstalled = BitConverter.ToString(metadata.Hash).Replace("-", "").ToLowerInvariant();
                            Logger.Log("UpdateChecker", $"Mod {metadata.Name}: installed hash {xxHashStringInstalled}, latest hash(es) {string.Join(", ", updateCatalog[metadata.Name].xxHash)}");
                            if(!updateCatalog[metadata.Name].xxHash.Contains(xxHashStringInstalled)) {
                                availableUpdatesCatalog.Add(updateCatalog[metadata.Name], metadata);
                            }
                        }
                    }

                    Logger.Log("UpdateChecker", $"{availableUpdatesCatalog.Count} update(s) available");
                }
            });

            task.Start();
        }

        public override IEnumerator Leave(Oui next) {
            Audio.Play(SFX.ui_main_whoosh_large_out);
            menu.Focused = false;

            for (float p = 0f; p < 1f; p += Engine.DeltaTime * 4f) {
                menu.X = onScreenX + 1920f * Ease.CubeIn(p);
                alpha = 1f - Ease.CubeIn(p);
                yield return null;
            }

            menu.Visible = Visible = false;
            menu.RemoveSelf();
            menu = null;

            updateCatalog = null;
            availableUpdatesCatalog = new SortedDictionary<ModUpdateInfo, EverestModuleMetadata>(new MostRecentUpdatedFirst());
            task = null;
        }

        public override void Update() {
            if(menu != null && task != null && task.IsCompleted && fetchingButton != null) {
                Logger.Log("UpdateChecker", "Rendering updates");

                menu.Remove(fetchingButton);
                fetchingButton = null;

                if(updateCatalog == null) {
                    // display an error message
                    TextMenu.Button button = new TextMenu.Button(Dialog.Clean("MODOPTIONS_UPDATECHECKER_ERROR"));
                    button.Disabled = true;
                    menu.Add(button);
                } else if (availableUpdatesCatalog.Count == 0) {
                    // display a dummy "no update available" button
                    TextMenu.Button button = new TextMenu.Button(Dialog.Clean("MODOPTIONS_UPDATECHECKER_NOUPDATE"));
                    button.Disabled = true;
                    menu.Add(button);
                } else {
                    // display one button per update
                    foreach(ModUpdateInfo update in availableUpdatesCatalog.Keys) {
                        EverestModuleMetadata metadata = availableUpdatesCatalog[update];
                        TextMenu.Button button = new TextMenu.Button($"{metadata.Name} | v. {metadata.VersionString} > {update.Version} ({new DateTime(1970, 1, 1, 0, 0, 0, 0).AddSeconds(update.LastUpdate):yyyy-MM-dd})");
                        button.Pressed(() => {
                            // make the menu non-interactive
                            menu.Focused = false;
                            button.Disabled = true;

                            // trigger the update download
                            downloadModUpdate(update, metadata, button);
                        });

                        // if there is more than one hash, it means there is multiple downloads for this mod. Thus, we can't update it manually.
                        if (update.xxHash.Count > 1) button.Disabled = true;

                        menu.Add(button);
                    }
                }
            }

            if (menu != null && task != null && menu.Focused && Selected && Input.MenuCancel.Pressed && task.IsCompleted) {
                if(shouldRestart) {
                    Everest.QuickFullRestart();
                } else {
                    // go back to mod options instead
                    Audio.Play(SFX.ui_main_button_back);
                    Overworld.Goto<OuiModOptions>();
                }
            }

            base.Update();
        }

        private void downloadModUpdate(ModUpdateInfo update, EverestModuleMetadata mod, TextMenu.Button button) {
            task = new Task(() => {
                // we will download the mod to Celeste_Directory/mod-update.zip at first.
                string zipPath = Path.Combine(Everest.PathGame, "mod-update.zip");

                try {
                    // download it...
                    button.Label = $"{update.Name} ({Dialog.Clean("UPDATECHECKER_DOWNLOADING")})";
                    downloadMod(update, button, zipPath);

                    // verify its checksum
                    string actualHash = BitConverter.ToString(Everest.GetChecksum("mod-update.zip")).Replace("-", "").ToLowerInvariant();
                    string expectedHash = update.xxHash[0];
                    Logger.Log("UpdateChecker", $"Verifying checksum: actual hash is {actualHash}, expected hash is {expectedHash}");
                    if(expectedHash != actualHash) {
                        throw new IOException($"Checksum error: expected {expectedHash}, got {actualHash}");
                    }

                    // mark restarting as required, as we will do weird stuff like closing zips afterwards.
                    if (!shouldRestart) {
                        shouldRestart = true;
                        subHeader.TextColor = Color.OrangeRed;
                        subHeader.Title = $"{Dialog.Clean("UPDATECHECKER_MENU_HEADER")} ({Dialog.Clean("UPDATECHECKER_WILLRESTART")})";
                    }

                    // install it
                    button.Label = $"{update.Name} ({Dialog.Clean("UPDATECHECKER_INSTALLING")})";
                    installMod(update, mod, zipPath);

                    // done!
                    button.Label = $"{update.Name} ({Dialog.Clean("UPDATECHECKER_UPDATED")})";

                    // select another enabled option: the next one, or the last one if there is no next one.
                    if (menu.Selection + 1 > menu.LastPossibleSelection)
                        menu.Selection = menu.LastPossibleSelection;
                    else
                        menu.Selection++;
                } catch (Exception e) {
                    // update failed
                    button.Label = $"{update.Name} ({Dialog.Clean("UPDATECHECKER_FAILED")})";
                    Logger.Log("UpdateChecker", $"Updating {update.Name} failed");
                    Logger.LogDetailed(e);
                    button.Disabled = false;

                    // try to delete mod-update.zip if it still exists.
                    if(File.Exists(zipPath)) {
                        try {
                            Logger.Log("UpdateChecker", $"Deleting temp file {zipPath}");
                            File.Delete(zipPath);
                        } catch(Exception) {
                            Logger.Log("UpdateChecker", $"Removing {zipPath} failed");
                        }
                    }
                }

                // give the menu control back to the player
                menu.Focused = true;
            });
            task.Start();
        }

        // heavily copy-pasted from Everest.Updater!
        private static void downloadMod(ModUpdateInfo update, TextMenu.Button button, string zipPath) {
            Logger.Log("UpdateChecker", $"Downloading {update.URL} to {zipPath}");
            DateTime timeStart = DateTime.Now;

            if (File.Exists(zipPath))
                File.Delete(zipPath);

            // Manual buffered copy from web input to file output.
            // Allows us to measure speed and progress.
            using (WebClient wc = new WebClient())
            using (Stream input = wc.OpenRead(update.URL))
            using (FileStream output = File.OpenWrite(zipPath)) {
                long length;
                if (input.CanSeek) {
                    length = input.Length;
                } else {
                    length = _ContentLength(update.URL);
                }

                byte[] buffer = new byte[4096];
                DateTime timeLastSpeed = timeStart;
                int read = 1;
                int readForSpeed = 0;
                int pos = 0;
                int speed = 0;
                int count = 0;
                TimeSpan td;
                while (read > 0) {
                    count = length > 0 ? (int)Math.Min(buffer.Length, length - pos) : buffer.Length;
                    read = input.Read(buffer, 0, count);
                    output.Write(buffer, 0, read);
                    pos += read;
                    readForSpeed += read;

                    td = DateTime.Now - timeLastSpeed;
                    if (td.TotalMilliseconds > 100) {
                        speed = (int)((readForSpeed / 1024D) / td.TotalSeconds);
                        readForSpeed = 0;
                        timeLastSpeed = DateTime.Now;
                    }

                    if (length > 0) {
                        button.Label = $"{update.Name} ({((int)Math.Floor(100D * (pos / (double)length)))}% @ {speed} KiB/s)";
                    } else {
                        button.Label = $"{update.Name} ({((int)Math.Floor(pos / 1000D))}KiB @ {speed} KiB/s)";
                    }
                }
            }
        }

        private static void installMod(ModUpdateInfo update, EverestModuleMetadata mod, string zipPath) {
            // let's close the zip, as we will replace it now.
            foreach(ModContent content in Everest.Content.Mods) {
                if(content.GetType() == typeof(ZipModContent) && (content as ZipModContent).Mod.Name == mod.Name) {
                    ZipModContent modZip = content as ZipModContent;

                    Logger.Log("UpdateChecker", $"Closing mod .zip: {modZip.Path}");
                    modZip.Dispose();
                }
            }

            // delete the old zip, and move the new one.
            Logger.Log("UpdateChecker", $"Deleting mod .zip: {mod.PathArchive}");
            File.Delete(mod.PathArchive);

            Logger.Log("UpdateChecker", $"Moving {zipPath} to {mod.PathArchive}");
            File.Move(zipPath, mod.PathArchive);
        }

        private static long _ContentLength(string url) {
            try {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "HEAD";
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                    return response.ContentLength;
            } catch (Exception) {
                return 0;
            }
        }

        public override void Render() {
            if (alpha > 0f) {
                Draw.Rect(-10f, -10f, 1940f, 1100f, Color.Black * alpha * 0.4f);
            }
            base.Render();
        }
    }
}
